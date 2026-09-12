// PS5 DualSense controller input via USB or Bluetooth HID, for the
// controller-reactive keyboard effect. Port of hardware/controller.py
// on `main` (added there in a session after this port's original scope
// was set, then explicitly brought back into scope by the user -- see
// windows/HANDOFF.md's "Scope reversal" note). Bluetooth support (Phase
// 8 (V2) Feature 3) is a C#-only addition -- the Python side stayed
// USB-only.
//
// USB byte offsets confirmed empirically on the Python side against a
// real DualSense Edge controller, then re-confirmed on THIS controller
// over USB again during Feature 3's investigation (see below) -- still
// accurate, unchanged.
//
// Bluetooth report format confirmed empirically live (2026-09-12) via
// JmaStudio.HardwareTest's `controller-raw-dump`/`controller-enhance-
// test` commands against a real DualSense Edge paired over Bluetooth on
// this machine -- NOT assumed from community reverse-engineering alone,
// though that research (Linux kernel's hid-playstation driver) is what
// pointed at the right mechanism and byte layout, cross-checked against
// live captures rather than trusted blind.
//
// TWO Bluetooth report modes exist. By DEFAULT the controller only
// sends a "basic" 0x01 report (same report ID as USB, 78-byte HID
// transfer but only the first ~9 bytes are meaningful -- sticks/
// buttons/triggers work, but paddles/Fn/touchpad/gyro/battery are all
// permanently 0). Reading HID feature report 0x05 (the calibration
// report -- DS_FEATURE_REPORT_CALIBRATION in the Linux driver; also
// documented in libsdl-org/SDL#10086, "Unable to enable Enhanced
// Reports on DualSense over Bluetooth") switches the controller into
// sending "enhanced" 0x31 reports instead, which include everything.
// TryEnableBluetoothEnhancedMode does this read once at connect/
// reconnect time (see its own comment) -- confirmed live via
// `controller-enhance-test`: reading feature report 0x05 immediately
// caused paddles/Fn/gyro/accel data to start appearing where it was
// all-zero before, with ZERO user interaction needed (gyro/accel show
// real values from gravity alone, at rest).
//
// Enhanced (0x31) report layout, confirmed against the Linux driver's
// `dualsense_input_report_common` (which starts at data[2] for
// Bluetooth -- data[1] is a seq_tag/framing byte the driver itself
// skips, not part of the common struct) and cross-checked live (a real
// at-rest sample read data[9]=0x08, the same idle/neutral-hat value
// USB uses, and data[6]/data[7]=0, idle triggers -- exactly as
// expected): sticks at 2-5, L2/R2 analog at 6-7, sequence counter at 8,
// buttons1/buttons2/buttons3 (paddles/Fn included) at 9/10/11. This
// SUPERSEDES an earlier "basic-mode" mapping this file used before
// TryEnableBluetoothEnhancedMode existed. Reports are 78 bytes over
// Bluetooth vs. 64 over USB either way -- confirmed via HidDevice.
// GetMaxInputReportLength(), which is also how transport is detected
// at discovery time (no need to parse the device path).

using HidSharp;

namespace JmaStudio.Hardware;

public sealed class ControllerState
{
    // -1.0..1.0, 0 = centered. *Y: negative = up, positive = down
    // (confirmed live on the Python side for the left stick; the right
    // stick uses the same report layout so this is assumed to hold).
    public double LeftX { get; init; }
    public double LeftY { get; init; }
    public double RightX { get; init; }
    public double RightY { get; init; }
    public double LeftTrigger { get; init; } // 0.0..1.0
    public double RightTrigger { get; init; }
    public bool L1 { get; init; }
    public bool R1 { get; init; }
    // Stick clicks -- confirmed live (2026-09-12) on both USB and
    // Bluetooth at buttons2 bit6 (L3, 0x40) / bit7 (R3, 0x80), same
    // byte as L1/R1. Previously read but never modeled or wired to
    // anything, per the user's explicit request to add them.
    public bool L3 { get; init; }
    public bool R3 { get; init; }
    public bool Square { get; init; }
    public bool Cross { get; init; }
    public bool Circle { get; init; }
    public bool Triangle { get; init; }
    public bool DpadUp { get; init; }
    public bool DpadRight { get; init; }
    public bool DpadDown { get; init; }
    public bool DpadLeft { get; init; }
    // DualSense Edge only -- rear paddles and Function buttons.
    public bool LeftFn { get; init; }
    public bool RightFn { get; init; }
    public bool LeftPaddle { get; init; }
    public bool RightPaddle { get; init; }

    public static readonly ControllerState Idle = new();
}

/// <summary>Runs a background thread continuously reading raw HID reports
/// and parsing out controller state. GetState() is thread-safe and cheap
/// -- meant to be called once per rendered frame.
///
/// Auto-reconnects after a sleep/wake-induced handle stall (Windows
/// re-enumerates the USB device on resume, which silently kills the
/// already-open handle) -- this was a real bug hit and fixed on the
/// Python side (see main's HANDOFF.md), reproduced here from the start
/// rather than waiting to hit it again.</summary>
public sealed class Controller : IDisposable
{
    private const int VendorId = 0x054C; // Sony Interactive Entertainment
    private static readonly int[] ProductIds = { 0x0CE6, 0x0DF2 }; // DualSense, DualSense Edge
    private const int UsagePage = 1; // Generic Desktop
    private const int Usage = 5; // Game Pad

    private const int StickCenter = 128;
    private const int StickRange = 127; // distance from center to an extreme (0 or 255)

    private const int ReadTimeoutMs = 100;

    // Empirically confirmed: USB DualSense/Edge reports are 64 bytes,
    // Bluetooth-paired ones are 78 (see this file's header comment) --
    // used both to size the read buffer generously enough for either
    // transport and to DETECT which transport a just-opened device is
    // using (HidDevice.GetMaxInputReportLength(), read once at open
    // time -- no device-path string parsing needed).
    private const int UsbMaxReportLength = 64;
    private const int ReadBufferSize = 96; // headroom above BT's 78

    // The pad streams reports continuously (USB ~1000Hz, Bluetooth
    // slower but still frequent) even at rest, so any gap this long
    // with zero reports means the *handle* has gone stale, not that the
    // controller stopped talking.
    private static readonly TimeSpan ReconnectStall = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private HidStream _stream;
    private ControllerState _state = ControllerState.Idle;
    private volatile bool _connected = true;
    private volatile bool _running = true;
    private volatile bool _isBluetooth;
    private readonly Thread _thread;

    private Controller(HidStream stream, bool isBluetooth)
    {
        _stream = stream;
        _isBluetooth = isBluetooth;
        _thread = new Thread(PollLoop) { IsBackground = true };
        _thread.Start();
    }

    /// <summary>True if the currently-open connection is over Bluetooth,
    /// false for USB. Reflects whatever transport TryReconnect() most
    /// recently found, not just the transport at initial Open()/
    /// TryDiscover() time.</summary>
    public bool IsBluetooth => _isBluetooth;

    public static Controller Open() =>
        TryDiscover() ?? throw new InvalidOperationException(
            "No DualSense controller found over USB or Bluetooth. Connect via USB cable, or pair it over Bluetooth first.");

    /// <summary>Like Open(), but returns null instead of throwing --
    /// used both by Open() itself and by ControllerHolder.TryDiscover()
    /// for the GUI's "Discover" button and DiagnosticsManager.Rescan(),
    /// neither of which should crash the caller on failure. Searches
    /// USB and Bluetooth in one pass (FindControllerDevice() enumerates
    /// both transports via the same VID/PID HID lookup -- Windows
    /// exposes a paired, connected Bluetooth HID device through the
    /// exact same enumeration as USB) -- no separate transport picker,
    /// matching the user's explicit UX request.</summary>
    public static Controller? TryDiscover()
    {
        HidDevice? device = FindControllerDevice();
        if (device is null) return null;
        if (!device.TryOpen(out HidStream? stream) || stream is null) return null;
        stream.ReadTimeout = ReadTimeoutMs;
        bool isBluetooth = device.GetMaxInputReportLength() > UsbMaxReportLength;
        if (isBluetooth) TryEnableBluetoothEnhancedMode(device, stream);
        return new Controller(stream, isBluetooth);
    }

    // Paddles/Fn (and touchpad/gyro/battery, unused here) are absent
    // from the DualSense's DEFAULT Bluetooth input report -- confirmed
    // live (2026-09-12): every byte past the sticks/buttons/triggers
    // stayed 0 through a real paddle-press capture. Per community
    // reverse-engineering (Linux kernel's hid-playstation driver,
    // DS_FEATURE_REPORT_CALIBRATION = 0x05; the SDL project's own
    // "Unable to enable Enhanced Reports on DualSense over Bluetooth"
    // issue, libsdl-org/SDL#10086), simply READING this feature report
    // once is what causes the controller to start including the full
    // data set in its regular input reports -- no response parsing
    // needed, the read itself is the trigger. This is a best-effort,
    // NOT YET LIVE-VERIFIED call (see this method's own limitation
    // note) -- if it doesn't work on this exact controller/firmware,
    // it fails silently and paddles/Fn just stay unavailable over
    // Bluetooth as before, with zero regression risk to the fields that
    // already work (sticks/buttons/triggers).
    private static void TryEnableBluetoothEnhancedMode(HidDevice device, HidStream stream)
    {
        const byte CalibrationFeatureReportId = 0x05;
        try
        {
            int len = device.GetMaxFeatureReportLength();
            if (len <= 0) return;
            byte[] buffer = new byte[len];
            buffer[0] = CalibrationFeatureReportId;
            stream.GetFeature(buffer);
        }
        catch
        {
            // Best-effort -- wrong buffer size for this firmware, the
            // request isn't supported, or a transient BT hiccup. Falls
            // back to the default (basic) report either way.
        }
    }

    /// <summary>Cheap presence-only probe for the Diagnostics window's
    /// "Re-scan hardware" button -- distinct from a running Controller's
    /// own IsConnected (reflects a currently-open handle's live read
    /// health), this re-enumerates HID devices (USB or Bluetooth) right
    /// now.</summary>
    public static bool IsPresent() => FindControllerDevice() is not null;

    private static HidDevice? FindControllerDevice()
    {
        foreach (int productId in ProductIds)
        {
            foreach (HidDevice device in DeviceList.Local.GetHidDevices(VendorId, productId))
            {
                try
                {
                    var descriptor = device.GetReportDescriptor();
                    bool matches = descriptor.DeviceItems
                        .SelectMany(di => di.Usages.GetAllValues())
                        .Any(usage => ((usage >> 16) & 0xFFFF) == UsagePage && (usage & 0xFFFF) == Usage);
                    if (matches) return device;
                }
                catch
                {
                    // Some interfaces on this device (non-gamepad ones)
                    // may not expose a readable report descriptor here.
                }
            }
        }
        return null;
    }

    private void PollLoop()
    {
        DateTime lastDataAt = DateTime.UtcNow;
        DateTime lastReconnectAttempt = DateTime.MinValue;

        while (_running)
        {
            byte[]? data = null;
            try
            {
                data = new byte[ReadBufferSize];
                int read = _stream.Read(data, 0, data.Length);
                if (read < 12) data = null; // BT enhanced mode needs data[11] (paddles/Fn) -- see ParseBluetoothReport
            }
            catch
            {
                data = null;
            }

            if (data is null)
            {
                DateTime now = DateTime.UtcNow;
                if (now - lastDataAt > ReconnectStall && now - lastReconnectAttempt > ReconnectStall)
                {
                    lastReconnectAttempt = now;
                    TryReconnect();
                }
                continue;
            }

            lastDataAt = DateTime.UtcNow;
            _connected = true;
            if (_isBluetooth) ParseBluetoothReport(data); else ParseUsbReport(data);
        }
    }

    private void ParseUsbReport(byte[] data) => ApplyReport(
        lx: data[1], ly: data[2], rx: data[3], ry: data[4],
        l2: data[5], r2: data[6],
        buttons1: data[8], buttons2: data[9], buttons3: data[10]);

    // Bluetooth, ENHANCED mode (report ID 0x31 -- see this file's header
    // comment and TryEnableBluetoothEnhancedMode). Offsets confirmed
    // against the Linux kernel's hid-playstation driver (dualsense_
    // input_report_common starts at data[2] for Bluetooth -- data[1] is
    // a seq_tag/framing byte the driver itself skips over, not part of
    // the common struct at all) AND cross-checked live: a real at-rest
    // sample read data[9]=0x08 (neutral hat, matching USB's own idle
    // value exactly) and data[6]/data[7]=0 (idle triggers), both exactly
    // as expected. This SUPERSEDES the old "basic-mode" Bluetooth
    // mapping this method used before enhanced mode existed (buttons at
    // 5-6, triggers at 8-9, no paddle data anywhere) -- now that every
    // Bluetooth connection is switched to enhanced mode at discovery/
    // reconnect time, the basic-mode report is never read anymore.
    private void ParseBluetoothReport(byte[] data) => ApplyReport(
        lx: data[2], ly: data[3], rx: data[4], ry: data[5],
        l2: data[6], r2: data[7],
        buttons1: data[9], buttons2: data[10], buttons3: data[11]);

    // Bit meanings are IDENTICAL across both transports (only the byte
    // OFFSETS feeding this differ) -- confirmed live for buttons1/
    // buttons2, matches the pre-existing (Python-verified) USB meanings.
    private void ApplyReport(byte lx, byte ly, byte rx, byte ry, byte l2, byte r2, byte buttons1, byte buttons2, byte buttons3)
    {
        double lxN = (lx - StickCenter) / (double)StickRange;
        double lyN = (ly - StickCenter) / (double)StickRange;
        double rxN = (rx - StickCenter) / (double)StickRange;
        double ryN = (ry - StickCenter) / (double)StickRange;
        double l2N = l2 / 255.0;
        double r2N = r2 / 255.0;

        // bits 4-7 = Square/Cross/Circle/Triangle. Low nibble = D-pad hat
        // switch: 0=up,1=up-right,2=right,3=down-right,4=down,5=down-left,
        // 6=left,7=up-left,8=neutral -- only the 4 cardinals are exposed.
        int hat = buttons1 & 0x0F;
        // buttons2: bit0=L1, bit1=R1 (bit2/bit3=L2/R2 digital click, bit6/
        // bit7=L3/R3 -- confirmed present live but not modeled in
        // ControllerState, matching the pre-existing USB scope).

        var newState = new ControllerState
        {
            LeftX = Math.Clamp(lxN, -1.0, 1.0),
            LeftY = Math.Clamp(lyN, -1.0, 1.0),
            RightX = Math.Clamp(rxN, -1.0, 1.0),
            RightY = Math.Clamp(ryN, -1.0, 1.0),
            LeftTrigger = l2N,
            RightTrigger = r2N,
            Square = (buttons1 & 0x10) != 0,
            Cross = (buttons1 & 0x20) != 0,
            Circle = (buttons1 & 0x40) != 0,
            Triangle = (buttons1 & 0x80) != 0,
            DpadUp = hat == 0,
            DpadRight = hat == 2,
            DpadDown = hat == 4,
            DpadLeft = hat == 6,
            L1 = (buttons2 & 0x01) != 0,
            R1 = (buttons2 & 0x02) != 0,
            L3 = (buttons2 & 0x40) != 0,
            R3 = (buttons2 & 0x80) != 0,
            // DualSense Edge only: bit4=left Fn, bit5=right Fn, bit6=left
            // paddle (L4), bit7=right paddle (R4).
            LeftFn = (buttons3 & 0x10) != 0,
            RightFn = (buttons3 & 0x20) != 0,
            LeftPaddle = (buttons3 & 0x40) != 0,
            RightPaddle = (buttons3 & 0x80) != 0,
        };
        lock (_lock) { _state = newState; }
    }

    private void TryReconnect()
    {
        HidDevice? device = FindControllerDevice();
        if (device is null || !device.TryOpen(out HidStream? newStream) || newStream is null)
        {
            _connected = false;
            return;
        }
        newStream.ReadTimeout = ReadTimeoutMs;
        HidStream old = _stream;
        _stream = newStream;
        // Re-detect transport on every reconnect, not just at initial
        // Open()/TryDiscover() time -- covers the (rare but real) case
        // of the SAME logical connection resuming over a different
        // transport than it started on (e.g. it was on Bluetooth, went
        // to sleep, and got plugged into USB before waking).
        _isBluetooth = device.GetMaxInputReportLength() > UsbMaxReportLength;
        if (_isBluetooth) TryEnableBluetoothEnhancedMode(device, newStream);
        _connected = true;
        try { old.Dispose(); } catch { /* best effort */ }
    }

    public ControllerState GetState()
    {
        lock (_lock) return _state;
    }

    /// <summary>True once real reports have been (or are again being)
    /// received -- reflects live read health, not just "was Open()
    /// called successfully."</summary>
    public bool IsConnected => _connected;

    public void Dispose()
    {
        _running = false;
        _thread.Join(TimeSpan.FromSeconds(1));
        _stream.Dispose();
    }
}
