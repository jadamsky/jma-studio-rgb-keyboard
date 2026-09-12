// Phase 8 (V2) idle screensaver: keyboard/mouse activity detection.
// Windows' own idle-tracking API, GetLastInputInfo, reliably covers
// keyboard AND mouse -- but NOT controller input, since games read a
// DualSense via raw HID/XInput, bypassing the OS input pipeline
// entirely (the same reason Windows' own screensaver ignores
// controller-only play). Controller activity is handled separately, in
// the Service, since it already polls Controller.GetState() every frame
// -- see IdleScreensaverManager.cs.
//
// Deliberately NOT a new custom low-level mouse hook -- that would add a
// second always-on global hook next to the existing keyboard one,
// working against this whole project's "stay light on a gaming laptop"
// principle. GetLastInputInfo is a single cheap on-demand Win32 call,
// polled every couple of seconds, not a hook intercepting every mouse
// move.
//
// Only pings the Service when NEW input is actually detected since the
// last check (idle time going DOWN, meaning something happened in
// between) -- not a constant heartbeat, so there's zero network traffic
// the rest of the time.

using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace JmaStudio.Gui;

public sealed class IdleActivityMonitor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    private readonly ApiClient _api;
    private readonly DispatcherTimer _timer;
    private uint _lastObservedIdleMs;

    public IdleActivityMonitor(ApiClient api)
    {
        _api = api;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public void Start() => _timer.Start();

    private async Task CheckAsync()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return;
        uint idleMs = GetTickCount() - info.dwTime;

        // Idle time going DOWN since the last check means real new input
        // happened in between -- ping the Service. If it just grew by
        // roughly the poll interval, nothing happened and there's
        // nothing worth reporting.
        if (idleMs < _lastObservedIdleMs)
        {
            await _api.PingIdleActivityAsync();
        }
        _lastObservedIdleMs = idleMs;
    }

    public void Dispose() => _timer.Stop();
}
