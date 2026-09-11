// HID interface to the Acer Predator PH16-71 per-key RGB keyboard.
//
// This is a straight port of the Python reference implementation
// (hardware/device.py on the `main` branch) -- the wire protocol byte
// values below are facts confirmed against real hardware there, not
// re-derived here. See that file's docstring for the full protocol
// writeup and sources. Only the transport (HidSharp instead of
// Python's hidapi wrapper) and language are new; nothing about the
// byte-level protocol has been re-verified independently yet -- see
// windows/HANDOFF.md's "Verified vs. unverified" section.
//
// Hardware facts:
//   - USB VID 0x04F2, PID 0x0117.
//   - Lighting lives on a vendor-specific HID interface (usage page
//     0xFF02), separate from the normal keyboard-input interface.
//   - Every command is an 8-byte payload {op, p1, p2, p3, p4, p5, p6,
//     checksum}, sent as a HID FEATURE report (report ID 0).
//     checksum = (0xFF - (sum(op,p1..p6) & 0xFF)) & 0xFF.
//   - A full apply is a short sequence of these, not a single one.
//   - Full per-key control additionally pushes eight 64-byte HID
//     interrupt-OUT packets (512 bytes = 128 cells x {0x00,R,G,B})
//     between the mode-select and commit commands.

using HidSharp;

namespace JmaStudio.Hardware;

public static class KeyboardConstants
{
    public const int VendorId = 0x04F2;
    public const int ProductId = 0x0117;
    public const int LightingUsagePage = 0xFF02;

    public const int NumCells = 128;
    public const int BytesPerCell = 4; // {0x00, R, G, B}
    public const int FramePacketCount = 8;
    public const int FramePacketSize = 64; // 8 packets * 64 bytes = 512 bytes = 128 * 4

    public const byte DefaultBrightness = 200;

    // ---- wire protocol constants ----
    public const byte CmdHandshake = 0x88;
    public const byte CmdSelectSimpleMode = 0xB1;
    public const byte CmdSelectPerKeyMode = 0x12;
    public const byte CmdWriteColor = 0x14;
    public const byte CmdCommit = 0x08;

    public const byte CommitActionApply = 0x02;
    public const byte CommitReservedTag = 0x05;
    public const byte CommitPersistFlag = 0x01;

    public const byte EffectSolid = 0x01;
    public const byte EffectPerKeyBuffer = 0x33;

    public const byte TargetZone = 0x01;
    public const byte TargetPerKey = 0x08;
}

public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>
/// Owns the open HID handle to the PH16-71's lighting interface.
/// Not thread-safe by itself -- callers (the future daemon/service
/// layer) are responsible for serializing access, same as the Python
/// Keyboard class implicitly relied on FastAPI's request handling.
/// </summary>
public sealed class Keyboard : IDisposable
{
    // Diagnostics window's perf tile -- records the full SendFrame cost
    // (8 interrupt-OUT packets + the mode-select/commit feature reports),
    // not just one packet, since that's the number that actually matters
    // against the render loop's ~33ms/frame budget.
    public static readonly LatencyStats HidLatency = new();

    private readonly HidStream _stream;

    private Keyboard(HidStream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// Finds and opens the PH16-71's vendor-specific lighting HID
    /// interface. Throws InvalidOperationException (mirroring the
    /// Python side's RuntimeError) if it isn't found on this machine.
    /// </summary>
    public static Keyboard Open()
    {
        var device = FindLightingDevice()
            ?? throw new InvalidOperationException(
                "PH16-71 lighting interface not found. Confirm this laptop's " +
                "keyboard controller really is 04F2:0117 -- some revisions/report " +
                "other PIDs (e.g. 011A has also been seen on related models).");

        if (!device.TryOpen(out HidStream? stream) || stream is null)
        {
            throw new InvalidOperationException(
                $"Found the PH16-71 lighting interface but couldn't open it " +
                $"(path: {device.DevicePath}). Another process may have it open exclusively.");
        }

        return new Keyboard(stream);
    }

    /// <summary>
    /// Enumerates HID devices and returns the vendor-specific (0xFF02)
    /// lighting interface on the PH16-71 keyboard controller, or null
    /// if it isn't present. HidSharp exposes usage page via
    /// GetMaxFeatureReportLength()-adjacent report descriptor parsing;
    /// unlike Python's hidapi wrapper, the usage page isn't a plain
    /// enumerate() field -- it has to be read from each candidate
    /// device's report descriptor. Verify this actually discriminates
    /// correctly against real hardware (see HANDOFF.md) -- if the
    /// keyboard exposes only one interface at this VID/PID, this may need
    /// to be simplified.
    /// </summary>
    public static HidDevice? FindLightingDevice()
    {
        foreach (var device in DeviceList.Local.GetHidDevices(
                     KeyboardConstants.VendorId, KeyboardConstants.ProductId))
        {
            try
            {
                var descriptor = device.GetReportDescriptor();
                foreach (uint usage in descriptor.DeviceItems.SelectMany(di => di.Usages.GetAllValues()))
                {
                    int usagePage = (int)((usage >> 16) & 0xFFFF);
                    if (usagePage == KeyboardConstants.LightingUsagePage)
                    {
                        return device;
                    }
                }
            }
            catch
            {
                // Some HID interfaces (e.g. the plain keyboard-input one)
                // may not expose a readable report descriptor from a
                // non-elevated process the same way; skip and keep looking.
            }
        }
        return null;
    }

    private static byte Checksum(ReadOnlySpan<byte> body)
    {
        int sum = 0;
        foreach (byte b in body) sum += b;
        return (byte)((0xFF - (sum & 0xFF)) & 0xFF);
    }

    private static byte[] BuildCommand(byte op, byte p1 = 0, byte p2 = 0, byte p3 = 0,
        byte p4 = 0, byte p5 = 0, byte p6 = 0)
    {
        Span<byte> body = stackalloc byte[7] { op, p1, p2, p3, p4, p5, p6 };
        byte[] result = new byte[8];
        body.CopyTo(result);
        result[7] = Checksum(body);
        return result;
    }

    /// <summary>
    /// Sends one 8-byte feature-report command. HidSharp's SetFeature
    /// (like Python's hidapi wrapper) requires a leading report-ID byte
    /// (0x00 -- this device declares no report IDs) that never actually
    /// reaches the wire; the real 8-byte command follows it.
    /// </summary>
    private void SendCommand(byte op, byte p1 = 0, byte p2 = 0, byte p3 = 0,
        byte p4 = 0, byte p5 = 0, byte p6 = 0)
    {
        byte[] command = BuildCommand(op, p1, p2, p3, p4, p5, p6);
        byte[] report = new byte[9];
        report[0] = 0x00;
        command.CopyTo(report, 1);
        _stream.SetFeature(report);
    }

    /// <summary>
    /// Solid color across the whole keyboard. A 4-command sequence
    /// (handshake, select zone mode, write color, commit) -- matches
    /// PredatorSense's own captured USB traffic.
    /// </summary>
    public void SetStaticColor(byte r, byte g, byte b, byte brightness = 255)
    {
        SendCommand(KeyboardConstants.CmdHandshake);
        SendCommand(KeyboardConstants.CmdSelectSimpleMode);
        SendCommand(KeyboardConstants.CmdWriteColor, 0, 0, r, g, b, 0);
        SendCommand(KeyboardConstants.CmdCommit, KeyboardConstants.CommitActionApply,
            KeyboardConstants.EffectSolid, KeyboardConstants.CommitReservedTag,
            brightness, KeyboardConstants.TargetZone, KeyboardConstants.CommitPersistFlag);
    }

    /// <summary>
    /// Pushes a full per-key frame. `colors` must contain exactly
    /// NumCells entries, one per cell index.
    /// </summary>
    public void SendFrame(IReadOnlyList<RgbColor> colors)
    {
        if (colors.Count != KeyboardConstants.NumCells)
        {
            throw new ArgumentException(
                $"expected {KeyboardConstants.NumCells} colors, got {colors.Count}", nameof(colors));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            SendFrameCore(colors);
        }
        finally
        {
            HidLatency.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    private void SendFrameCore(IReadOnlyList<RgbColor> colors)
    {
        byte[] buf = new byte[KeyboardConstants.NumCells * KeyboardConstants.BytesPerCell];
        for (int i = 0; i < colors.Count; i++)
        {
            int offset = i * KeyboardConstants.BytesPerCell;
            buf[offset] = 0x00;
            buf[offset + 1] = colors[i].R;
            buf[offset + 2] = colors[i].G;
            buf[offset + 3] = colors[i].B;
        }

        SendCommand(KeyboardConstants.CmdHandshake);
        SendCommand(KeyboardConstants.CmdSelectPerKeyMode, 0, 0, KeyboardConstants.TargetPerKey);

        // Eight raw 64-byte interrupt-OUT packets, each prefixed with the
        // same leading 0x00 report-ID byte the feature reports use.
        for (int chunkIndex = 0; chunkIndex < KeyboardConstants.FramePacketCount; chunkIndex++)
        {
            int start = chunkIndex * KeyboardConstants.FramePacketSize;
            byte[] packet = new byte[KeyboardConstants.FramePacketSize + 1];
            packet[0] = 0x00;
            Array.Copy(buf, start, packet, 1, KeyboardConstants.FramePacketSize);
            _stream.Write(packet);
        }

        SendCommand(KeyboardConstants.CmdCommit, KeyboardConstants.CommitActionApply,
            KeyboardConstants.EffectPerKeyBuffer, KeyboardConstants.CommitReservedTag,
            KeyboardConstants.DefaultBrightness, KeyboardConstants.TargetPerKey,
            KeyboardConstants.CommitPersistFlag);
    }

    public void Dispose() => _stream.Dispose();
}
