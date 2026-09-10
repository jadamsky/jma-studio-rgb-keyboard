// Settled decision #10 (windows/HANDOFF.md): target AcerLightingService
// only -- already tested live that stopping it prevents PredatorSense
// from regaining control of the lighting. Installer sets its startup
// type to Disabled (not just Stop-Service) so it stays off across
// reboots; the service also does this cheap check at its own startup
// as insurance against a future Windows/driver update re-enabling it
// (same insurance start_all.ps1 has on the Python side).

using System.Management;
using System.ServiceProcess;

namespace JmaStudio.Service;

public static class AcerLightingServiceManager
{
    private const string TargetServiceName = "AcerLightingService";

    /// <summary>Stops AcerLightingService (if running) and sets its
    /// startup type to Disabled. Safe to call unconditionally -- a
    /// no-op if the service doesn't exist on this machine (not a
    /// PH16-71, or the service was never installed) or is already
    /// stopped and disabled.</summary>
    public static void StopAndDisable()
    {
        using ServiceController? controller = TryGetController();
        if (controller is null) return;

        if (controller.Status != ServiceControllerStatus.Stopped)
        {
            try
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Best-effort -- even if it won't stop right now, disabling
                // the startup type still prevents it coming back on reboot.
            }
        }

        SetStartupDisabled();
    }

    private static ServiceController? TryGetController()
    {
        try
        {
            var controller = new ServiceController(TargetServiceName);
            _ = controller.Status; // throws InvalidOperationException if the service doesn't exist
            return controller;
        }
        catch
        {
            return null;
        }
    }

    private static void SetStartupDisabled()
    {
        using var searcher = new ManagementObjectSearcher(
            $"SELECT * FROM Win32_Service WHERE Name='{TargetServiceName}'");
        foreach (ManagementBaseObject result in searcher.Get())
        {
            using var service = (ManagementObject)result;
            service.InvokeMethod("ChangeStartMode", new object[] { "Disabled" });
        }
    }
}
