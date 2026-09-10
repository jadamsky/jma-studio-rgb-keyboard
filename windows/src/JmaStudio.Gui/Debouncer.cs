using System.Windows.Threading;

namespace JmaStudio.Gui;

// Matches gui/app.js's debounce(fn, ms) helper: each tuning panel's
// live-apply needs its own independent debounce window (dragging a
// gradient slider shouldn't cancel a pending custom-key-color apply),
// so this is instantiated once per panel rather than shared.
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;

    public Debouncer(int milliseconds, Action fire)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            fire();
        };
    }

    public void Fire()
    {
        _timer.Stop();
        _timer.Start();
    }
}
