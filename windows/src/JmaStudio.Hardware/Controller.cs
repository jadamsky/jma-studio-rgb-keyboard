// PS5 DualSense controller input via USB HID, for the controller-
// reactive keyboard effect. Port of hardware/controller.py on `main`
// (added there in a session after this port's original scope was set,
// then explicitly brought back into scope by the user -- see
// windows/HANDOFF.md's "Scope reversal" note).
//
// Wired (USB) only, matching the Python side -- Bluetooth uses a
// different report ID/length and was deliberately deferred there too.
//
// Byte offsets confirmed empirically on the Python side against a real
// DualSense Edge controller -- not re-derived here, just re-verify the
// .NET HID transport carries them the same way (same spirit as the
// keyboard/lightbar ports -- see HANDOFF.md's Phase 2 verification).

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

    // The pad streams reports continuously at ~1000Hz even at rest, so
    // any gap this long with zero reports means the *handle* has gone
    // stale, not that the controller stopped talking.
    private static readonly TimeSpan ReconnectStall = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private HidStream _stream;
    private ControllerState _state = ControllerState.Idle;
    private volatile bool _connected = true;
    private volatile bool _running = true;
    private readonly Thread _thread;

    private Controller(HidStream stream)
    {
        _stream = stream;
        _thread = new Thread(PollLoop) { IsBackground = true };
        _thread.Start();
    }

    public static Controller Open()
    {
        HidDevice device = FindControllerDevice()
            ?? throw new InvalidOperationException(
                "No DualSense controller found over USB. Bluetooth isn't supported yet -- connect via USB cable.");
        if (!device.TryOpen(out HidStream? stream) || stream is null)
        {
            throw new InvalidOperationException($"Found the DualSense controller but couldn't open it (path: {device.DevicePath}).");
        }
        stream.ReadTimeout = ReadTimeoutMs;
        return new Controller(stream);
    }

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
                data = new byte[64];
                int read = _stream.Read(data, 0, data.Length);
                if (read < 11) data = null;
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
            ParseReport(data);
        }
    }

    private void ParseReport(byte[] data)
    {
        double lx = (data[1] - StickCenter) / (double)StickRange;
        double ly = (data[2] - StickCenter) / (double)StickRange;
        double rx = (data[3] - StickCenter) / (double)StickRange;
        double ry = (data[4] - StickCenter) / (double)StickRange;
        double l2 = data[5] / 255.0;
        double r2 = data[6] / 255.0;

        // bits 4-7 = Square/Cross/Circle/Triangle. Low nibble = D-pad hat
        // switch: 0=up,1=up-right,2=right,3=down-right,4=down,5=down-left,
        // 6=left,7=up-left,8=neutral -- only the 4 cardinals are exposed.
        byte buttons1 = data[8];
        int hat = buttons1 & 0x0F;
        byte buttons2 = data[9]; // bit0=L1, bit1=R1
        // DualSense Edge only: bit4=left Fn, bit5=right Fn, bit6=left
        // paddle (L4), bit7=right paddle (R4).
        byte buttons3 = data[10];

        var newState = new ControllerState
        {
            LeftX = Math.Clamp(lx, -1.0, 1.0),
            LeftY = Math.Clamp(ly, -1.0, 1.0),
            RightX = Math.Clamp(rx, -1.0, 1.0),
            RightY = Math.Clamp(ry, -1.0, 1.0),
            LeftTrigger = l2,
            RightTrigger = r2,
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
