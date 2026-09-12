// Thin shared wrapper around the plain Win32 GetSystemPowerStatus API --
// factored out of LowBatteryOverrideManager once PowerStateFlashManager
// needed the exact same read, so there's one P/Invoke declaration
// instead of two copies drifting apart.

using System.Runtime.InteropServices;

namespace JmaStudio.Service;

public static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>255 in either field means "unknown/not present" per the
    /// Win32 docs -- treated as "no usable reading" (no real battery,
    /// e.g. a desktop, or a transient read failure), same as a failed
    /// API call.</summary>
    public static bool TryGet(out bool onBattery, out int percent)
    {
        onBattery = false;
        percent = 100;
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return false;
        if (status.ACLineStatus == 255 || status.BatteryLifePercent == 255) return false;
        onBattery = status.ACLineStatus == 0;
        percent = status.BatteryLifePercent;
        return true;
    }
}
