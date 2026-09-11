// Diagnostic surface for the AcerGamingFunction WMI class, kept around
// (not just a throwaway Phase-2 script) as a standing tool for
// re-verifying low-level WMI behavior if something ever seems off --
// e.g. after a Windows update changes WMI/COM marshaling behavior, or
// when debugging a future "why did this stop working" report.
//
// The specific open questions this was built to answer (see
// windows/HANDOFF.md's "Highest-risk area" section) are now RESOLVED,
// confirmed live on real hardware: System.Management can call this
// class's Set* methods (given elevation), a native ulong is the right
// marshaling for SetGamingRgbKb (no VT_BSTR-decimal-string workaround
// needed), and a single ManagementObject instance is safely reusable
// across threads. See Lightbar.cs's file-header comment for the full
// writeup. What remains here is just the reusable probing capability,
// not open questions.

using System.Management;

namespace JmaStudio.Hardware;

public static class LightbarDiagnostics
{
    private const string WmiNamespace = @"root\wmi";
    private const string WmiClass = "AcerGamingFunction";

    /// <summary>Raw instance resolution, exposed for ad-hoc diagnostics
    /// and the thread-affinity probe below.</summary>
    public static ManagementObject? FindRawInstance()
    {
        using var searcher = new ManagementObjectSearcher(WmiNamespace, $"SELECT * FROM {WmiClass}");
        foreach (ManagementBaseObject obj in searcher.Get())
        {
            return (ManagementObject)obj;
        }
        return null;
    }

    /// <summary>Same as FindRawInstance but never throws -- swallows and
    /// reports the exception to `log` instead, for diagnosing WHY
    /// resolution failed (permission vs. genuinely absent) rather than
    /// just reporting null either way.</summary>
    public static ManagementObject? TryFindRawInstance(TextWriter log)
    {
        try
        {
            return FindRawInstance();
        }
        catch (Exception ex)
        {
            log.WriteLine($"FindRawInstance threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static readonly byte[] LedPayload =
        { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00 };

    /// <summary>
    /// Resolves ONE instance on the calling thread, then invokes a
    /// harmless priming call (SetGamingLED -- identical payload every
    /// time, safe to spam) against that SAME cached instance from
    /// `threadCount` different Task.Run worker-pool threads concurrently.
    /// Confirmed clean with 8 threads on real hardware (2026-09-09); a
    /// THREW line here in the future would mean something changed.
    /// </summary>
    public static async Task ProbeThreadAffinityAsync(int threadCount, TextWriter log)
    {
        using ManagementObject? shared = FindRawInstance()
            ?? throw new InvalidOperationException("AcerGamingFunction not found.");

        var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(() =>
        {
            try
            {
                using ManagementBaseObject inParams = shared.GetMethodParameters("SetGamingLED");
                inParams["gmInput"] = LedPayload;
                using ManagementBaseObject outParams = shared.InvokeMethod("SetGamingLED", inParams, null);
                log.WriteLine($"  thread {i} (managed tid {Environment.CurrentManagedThreadId}): OK, gmOutput={outParams["gmOutput"]}");
            }
            catch (Exception ex)
            {
                log.WriteLine($"  thread {i} (managed tid {Environment.CurrentManagedThreadId}): THREW {ex.GetType().Name}: {ex.Message}");
            }
        }));

        await Task.WhenAll(tasks);
    }
}
