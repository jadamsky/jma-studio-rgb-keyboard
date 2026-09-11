using System.Windows.Threading;

namespace JmaStudio.Gui;

// Matches gui/app.js's debounce(fn, ms) helper: each tuning panel's
// live-apply needs its own independent debounce window (dragging a
// gradient slider shouldn't cancel a pending custom-key-color apply),
// so this is instantiated once per panel rather than shared.
//
// `fire` is Func<Task>, not Action -- every real call site passes an
// `async () => await ...()` lambda, which compiles to unsafe `async
// void` against an Action parameter (the exact same lambda syntax
// compiles to a proper awaitable Task against Func<Task>, so no call
// site needed to change for this fix). That distinction is exactly
// what crashed the whole app live (2026-09-10): with `Action`, a
// transient Service-unreachable moment inside SendLiveUpdateAsync threw
// on the Dispatcher with nothing able to catch it, taking down the
// entire process -- including the tray icon, which is now the thing
// signaling "is the Service still running" to begin with. Awaiting and
// catching here fixes every existing Debouncer call site at once.
public sealed class Debouncer
{
    private readonly DispatcherTimer _timer;

    public Debouncer(int milliseconds, Func<Task> fire)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            try
            {
                await fire();
            }
            catch
            {
                // A live-apply failing (Service unreachable, transient
                // network hiccup) should never take the whole app down --
                // the next debounced change gets another chance.
            }
        };
    }

    public void Fire()
    {
        _timer.Stop();
        _timer.Start();
    }
}
