// Phase 8 (V2) Feature 3: mutable holder for the DualSense connection.
// Before this, `Controller?` was a single reference captured once at
// Program.cs startup and handed out directly to RenderLoopService,
// DiagnosticsManager, IdleScreensaverManager, and several Endpoints.cs
// handlers -- meaning none of them could ever observe a controller that
// wasn't already connected when the Service started (a real bug the
// user found live: connect the controller AFTER the Service is already
// running and it never works). This holder is the fix: every consumer
// now reads `.Current` each time instead of holding the raw reference,
// so a `Replace()` (triggered by the GUI's "Discover" button, or a
// future auto-retry) is visible everywhere immediately.
//
// Read from the 30fps render loop AND from HTTP request handlers
// concurrently, so this needs real thread safety, not just a bare
// nullable field -- a lock around get/set is more than sufficient,
// this isn't a hot path (reads happen at most ~30-60/sec, replaces are
// rare user-triggered events).

namespace JmaStudio.Hardware;

public sealed class ControllerHolder
{
    private readonly object _lock = new();
    private Controller? _current;

    public ControllerHolder(Controller? initial)
    {
        _current = initial;
    }

    public Controller? Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Scans for a DualSense over USB or Bluetooth (whichever
    /// responds first -- see Controller.TryDiscover) and, if found,
    /// swaps it in as the new Current, disposing whatever was there
    /// before. Returns true iff a controller was found and connected --
    /// callers (the GUI's "Discover" button, DiagnosticsManager.Rescan)
    /// use this to report success/failure back to the user.</summary>
    public bool TryDiscover()
    {
        Controller? found = Controller.TryDiscover();
        if (found is null) return false;

        Controller? old;
        lock (_lock)
        {
            old = _current;
            _current = found;
        }
        old?.Dispose();
        return true;
    }
}
