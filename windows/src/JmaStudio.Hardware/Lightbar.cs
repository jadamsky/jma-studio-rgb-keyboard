// Rear lightbar control via Windows ACPI-WMI (AcerGamingFunction) on
// the Predator PH16-71.
//
// This is a port of the Python reference implementation
// (hardware/lightbar.py on the `main` branch) -- every byte value
// below is a fact confirmed there against real hardware via Frida
// instrumentation of Acer's own software, not re-derived here. See
// LIGHTBAR_REVERSE_ENGINEERING.md (main branch) for the full story.
//
// The .NET-specific risks flagged early in this port (see
// windows/HANDOFF.md's "Highest-risk area" section) are now RESOLVED,
// confirmed live against real hardware (zone 1 turned red, then off,
// twice, on the user's own PH16-71):
//   1. System.Management (ManagementObjectSearcher/InvokeMethod) CAN
//      call this WMI class's Set* methods, given an elevated process --
//      same Administrator requirement the Python side had, now
//      reconfirmed for .NET's own WMI stack (which, unlike Python's
//      win32com.client, also required elevation just to ENUMERATE the
//      class, not only to call Set* methods on it).
//   2. SetGamingRgbKb's packed ulong parameter marshals correctly as a
//      plain native `ulong` through System.Management -- no VT_BSTR
//      decimal-string workaround needed, despite that being what the
//      Python side observed on the wire from Acer's own software. (That
//      finding was about how OpenRGB.exe's own COM automation happened
//      to send it, not a requirement of the receiving driver.)
//   3. A single ManagementObject instance IS safely reusable across
//      threads -- 8 concurrent Task.Run threads sharing one instance
//      all succeeded with zero exceptions. No .NET equivalent of
//      Python's threading.local()+pythoncom.CoInitialize() per-thread
//      dance is needed.
//
// UPDATE (2026-09-10, Lightbar reactive window): the "resolution is
// cheap, don't bother caching" assumption above turned out to be WRONG
// for .NET specifically, unlike the Python side it was carried over
// from. Measured live: a single CallMethod() (one ManagementObjectSearcher
// WQL query + InvokeMethod) took 83-102ms end to end -- FlashZones()
// makes 5 of these per call (1x SetGamingLED, 1x SetGamingKBBacklight,
// 3x SetGamingRgbKb), so a single reactive-loop tick could take
// 400-500ms against an 80ms budget, which is exactly why the keyboard
// -> lightbar reactive flash felt "glitchier and slower" than the
// Python original on real hardware. Now cached (GetCachedInstance()
// below) -- resolved once, reused for every call, re-resolved only if
// a call against the cached instance throws (e.g. a WMI provider
// restart invalidated it).
//
// Requires an elevated (Administrator) process for every Set* method.

using System.Management;
using System.Threading.Tasks;

namespace JmaStudio.Hardware;

public enum LightbarMode
{
    Breathing,
    Neon,
    Rainbow,
    Wave,
    Ripple,
    Scanner,
    Strobe,
}

/// <summary>Last-commanded lightbar state, for saving as a preset -- either
/// an active firmware animated mode, or static per-zone colors, whichever
/// is actually live. Mirrors the two shapes hardware/lightbar.py's
/// get_state()/apply_state() produce/accept (including that real,
/// already-saved presets predating this shape may have "static" implied
/// by the ABSENCE of Mode -- see Lightbar.ApplyState).</summary>
public sealed class LightbarState
{
    public LightbarMode? Mode { get; init; }
    public RgbColor ModeColor { get; init; } = new(255, 0, 0);
    public int ModeSpeed { get; init; } = 5;
    public IReadOnlyDictionary<int, RgbColor>? ZoneColors { get; init; }
    public int Brightness { get; init; } = 100;
}

/// <summary>
/// Owns the last-known color for each zone (every commit re-asserts all
/// three zones at once, matching the real protocol -- this cache is what
/// lets changing one zone leave the other two untouched).
/// </summary>
public sealed class Lightbar
{
    private const string WmiNamespace = @"root\wmi";
    private const string WmiClass = "AcerGamingFunction";

    // Fixed priming/"arm" trigger Acer's own software sends immediately
    // before every color commit -- identical every single time regardless
    // of color, zone, or brightness. Do not try to vary this.
    private static readonly byte[] LedPayload =
        { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00 };

    // Zone number (matches the physical left-to-right layout viewed from
    // the front of the laptop with the lid up) -> the mask byte
    // SetGamingRgbKb actually expects.
    private static readonly IReadOnlyDictionary<int, int> ZoneMasks =
        new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 4 };

    public const int NumZones = 3;

    private static readonly IReadOnlyDictionary<LightbarMode, byte> Modes = new Dictionary<LightbarMode, byte>
    {
        [LightbarMode.Breathing] = 0x01,
        [LightbarMode.Neon] = 0x02,
        [LightbarMode.Rainbow] = 0x03,
        [LightbarMode.Wave] = 0x04,
        [LightbarMode.Ripple] = 0x05,
        [LightbarMode.Scanner] = 0x06,
        [LightbarMode.Strobe] = 0x07,
    };

    // How many times to repeat the full commit sequence, and the delay
    // between repeats -- matches Acer's own real software's cadence
    // exactly (captured live on the Python side).
    private const int CommitRounds = 3;
    private static readonly TimeSpan CommitRoundDelay = TimeSpan.FromMilliseconds(65);

    private readonly Dictionary<int, RgbColor> _colors = new()
    {
        [1] = new RgbColor(0, 0, 0),
        [2] = new RgbColor(0, 0, 0),
        [3] = new RgbColor(0, 0, 0),
    };
    private int _brightness = 100;
    private LightbarMode? _mode;
    private RgbColor _modeColor = new(255, 0, 0);
    private int _modeSpeed = 5;

    private Lightbar() { }

    /// <summary>
    /// Confirms the AcerGamingFunction WMI class exists on this machine
    /// and returns a Lightbar ready to use. Does NOT require elevation --
    /// only the Set* calls made later do.
    /// </summary>
    public static Lightbar Open()
    {
        using ManagementObject? probe = FindInstance();
        if (probe is null)
        {
            throw new InvalidOperationException(
                "AcerGamingFunction WMI class not found. This laptop may not be " +
                "a PH16-71 with a rear lightbar, or the WMI provider isn't " +
                "available in this process.");
        }
        return new Lightbar();
    }

    /// <summary>
    /// Returns a fresh AcerGamingFunction WMI instance, or null if this
    /// machine doesn't expose it. Used for the one-time existence probe
    /// in Open() and to (re-)populate the cache in GetCachedInstance() --
    /// NOT used directly for every Set* call anymore, see that method's
    /// comment and the header comment's 2026-09-10 update for why.
    /// </summary>
    private static ManagementObject? FindInstance()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClass}");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                return (ManagementObject)obj;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Resolved once, reused for every subsequent Set* call -- see the
    // header comment's 2026-09-10 update. Static (not per-Lightbar-
    // instance) since only one Lightbar is ever constructed in this
    // process, matching the rest of this class's existing static
    // CallMethod/CallArray/CallU64 helpers. Confirmed safe to share
    // across threads via JmaStudio.HardwareTest's thread-affinity probe.
    private static ManagementObject? _cachedInstance;

    private static ManagementObject GetCachedInstance()
    {
        return _cachedInstance ??= FindInstance()
            ?? throw new InvalidOperationException("AcerGamingFunction WMI class not found.");
    }

    // GetMethodParameters(method) fetches the WMI class's method
    // signature from the CIM repository -- another round trip, done on
    // every single call if not cached. Only 3 distinct method names are
    // ever used (SetGamingLED, SetGamingKBBacklight, SetGamingRgbKb), so
    // caching the parameter-template object per method name removes this
    // cost too. The template describes the CLASS's method shape, not any
    // one instance, so it stays valid even if _cachedInstance itself is
    // later invalidated and re-resolved -- cleared together below purely
    // for simplicity, not because it would otherwise go stale.
    // ConcurrentDictionary (not a plain Dictionary + shared mutable
    // template) because FlashZones now fires its 3 SetGamingRgbKb calls
    // concurrently (see that method's own comment) -- each call clones
    // its own copy of the cached template before setting "gmInput" and
    // invoking, so 3 threads mutating 3 clones in parallel can never
    // race on one shared property bag the way sharing the template
    // object itself would.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ManagementBaseObject> _paramTemplates = new();

    private static object CallMethod(string method, object gmInput)
    {
        try
        {
            return CallMethodOn(GetCachedInstance(), method, gmInput);
        }
        catch (ManagementException)
        {
            // The cached instance may have been invalidated (e.g. a WMI
            // provider restart) -- drop it and resolve fresh exactly
            // once before giving up, rather than caching a dead handle
            // forever after the first hiccup.
            _cachedInstance?.Dispose();
            _cachedInstance = null;
            _paramTemplates.Clear();
            return CallMethodOn(GetCachedInstance(), method, gmInput);
        }
    }

    private static object CallMethodOn(ManagementObject instance, string method, object gmInput)
    {
        ManagementBaseObject template = _paramTemplates.GetOrAdd(method, instance.GetMethodParameters);
        // Clone rather than mutate the cached template directly -- see
        // the field's own comment on why this matters once multiple
        // zone writes can be in flight concurrently.
        using ManagementBaseObject inParams = (ManagementBaseObject)template.Clone();
        inParams["gmInput"] = gmInput;
        using ManagementBaseObject outParams = instance.InvokeMethod(method, inParams, null);
        return outParams["gmOutput"];
    }

    private static object CallArray(string method, byte[] arr) => CallMethod(method, arr);

    /// <summary>
    /// Sends gmInput as a native ulong -- confirmed live on real
    /// hardware that System.Management marshals this correctly with no
    /// VT_BSTR workaround needed (see the risk note at the top of this
    /// file).
    /// </summary>
    private static object CallU64(string method, ulong value) => CallMethod(method, value);

    private static void SendLed() => CallArray("SetGamingLED", LedPayload);

    private static void SendKbCommit(int brightness) =>
        CallArray("SetGamingKBBacklight",
            new byte[] { 0, 0, (byte)brightness, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0 });

    private static void SendRgbKb(int mask, byte r, byte g, byte b)
    {
        ulong value = ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24) | (0x08UL << 32) | ((ulong)mask << 40);
        CallU64("SetGamingRgbKb", value);
    }

    private void Commit()
    {
        for (int i = 0; i < CommitRounds; i++)
        {
            SendLed();
            SendKbCommit(_brightness);
            foreach ((int zone, int mask) in ZoneMasks)
            {
                RgbColor c = _colors[zone];
                SendRgbKb(mask, c.R, c.G, c.B);
            }
            Thread.Sleep(CommitRoundDelay);
        }
    }

    /// <summary>
    /// Firmware-native animated mode. Mode byte values documented by
    /// Venator's kernel driver, with color embedded directly in the same
    /// 16-byte SetGamingKBBacklight buffer. Only ONE color for the whole
    /// bar is possible in these modes (confirmed on the Python side, see
    /// hardware/lightbar.py's set_mode() docstring).
    /// </summary>
    public void SetMode(LightbarMode mode, byte r, byte g, byte b, int speed = 5, int brightness = 100)
    {
        brightness = Math.Clamp(brightness, 0, 100);
        byte[] buf = { Modes[mode], (byte)speed, (byte)brightness, 0x00, 0x01, r, g, b, 0x03, 0x02, 0, 0, 0, 0, 0, 0 };
        for (int i = 0; i < CommitRounds; i++)
        {
            SendLed();
            CallArray("SetGamingKBBacklight", buf);
            Thread.Sleep(CommitRoundDelay);
        }
        _mode = mode;
        _modeColor = new RgbColor(r, g, b);
        _modeSpeed = speed;
        _brightness = brightness;
    }

    public void SetZone(int zone, byte r, byte g, byte b)
    {
        if (!ZoneMasks.ContainsKey(zone))
            throw new ArgumentOutOfRangeException(nameof(zone), $"zone must be one of {string.Join(",", ZoneMasks.Keys)}, got {zone}");
        _colors[zone] = new RgbColor(r, g, b);
        _mode = null;
        Commit();
    }

    public void SetAll(byte r, byte g, byte b)
    {
        foreach (int zone in ZoneMasks.Keys) _colors[zone] = new RgbColor(r, g, b);
        _mode = null;
        Commit();
    }

    /// <summary>
    /// Fast single-round zone update for the keyboard-reactive lightbar
    /// loop -- skips the 3x-repeat/sleep cadence Commit() uses for
    /// "set and forget" reliability. `targets` need only include the
    /// zone(s) actually changing; the rest keep their last-known color.
    /// </summary>
    public void FlashZones(IReadOnlyDictionary<int, RgbColor> targets)
    {
        _mode = null;
        foreach ((int zone, RgbColor rgb) in targets)
        {
            if (!ZoneMasks.ContainsKey(zone))
                throw new ArgumentOutOfRangeException(nameof(targets), $"zone must be one of {string.Join(",", ZoneMasks.Keys)}, got {zone}");
            _colors[zone] = rgb;
        }
        SendLed();
        SendKbCommit(_brightness);
        // Fired concurrently, not one after another: each SetGamingRgbKb
        // call costs ~12-15ms even after caching (see the header
        // comment's 2026-09-10 update), so a sequential loop over 3
        // zones left a real ~25-30ms gap between zone 1's color actually
        // landing on the hardware and zone 3's -- visible as "zone 1 out
        // of sync with 2/3" specifically on an all-zone flash (zone 4),
        // where all three are supposed to change at once. Confirmed safe
        // via JmaStudio.HardwareTest's thread-affinity probe (concurrent
        // InvokeMethod calls against one shared, cached ManagementObject
        // already proven safe from 8 concurrent threads) -- each thread
        // here clones its own parameter object (see CallMethodOn) so
        // there's no shared-mutable-state race despite the shared cache.
        Parallel.ForEach(ZoneMasks, kv =>
        {
            (int zone, int mask) = (kv.Key, kv.Value);
            RgbColor c = _colors[zone];
            SendRgbKb(mask, c.R, c.G, c.B);
        });
    }

    /// <summary>
    /// 0-100. Re-applies immediately with the current colors -- if an
    /// animated mode is active, re-sends THAT (with the new brightness)
    /// instead of falling through to a static commit, which would
    /// silently cancel the animation.
    /// </summary>
    public void SetBrightness(int value)
    {
        value = Math.Clamp(value, 0, 100);
        if (_mode is { } mode)
        {
            SetMode(mode, _modeColor.R, _modeColor.G, _modeColor.B, _modeSpeed, value);
        }
        else
        {
            _brightness = value;
            Commit();
        }
    }

    public void Off() => SetAll(0, 0, 0);

    public LightbarState GetState() => new()
    {
        Mode = _mode,
        ModeColor = _modeColor,
        ModeSpeed = _modeSpeed,
        ZoneColors = _mode is null ? new Dictionary<int, RgbColor>(_colors) : null,
        Brightness = _brightness,
    };

    /// <summary>Restores a state previously captured by GetState().</summary>
    public void ApplyState(LightbarState state)
    {
        if (state.Mode is { } mode)
        {
            SetMode(mode, state.ModeColor.R, state.ModeColor.G, state.ModeColor.B, state.ModeSpeed, state.Brightness);
            return;
        }
        if (state.ZoneColors is not null)
        {
            foreach ((int zone, RgbColor rgb) in state.ZoneColors)
            {
                if (!ZoneMasks.ContainsKey(zone))
                    throw new ArgumentOutOfRangeException(nameof(state), $"zone must be one of {string.Join(",", ZoneMasks.Keys)}, got {zone}");
                _colors[zone] = rgb;
            }
        }
        _brightness = Math.Clamp(state.Brightness, 0, 100);
        _mode = null;
        Commit();
    }
}
