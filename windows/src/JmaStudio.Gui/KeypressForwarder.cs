// The fix for Phase 7's "Critical finding" (see HANDOFF.md): a real
// installed Windows Service runs in Session 0, which cannot see
// keystrokes typed on the interactive desktop (Session 0 Isolation) --
// so typing_reactive-family keyboard effects and the lightbar's
// keyboard-driven reactive flash never trigger against a real installed
// service, even though their background renders fine. This class moves
// key CAPTURE into the GUI (which runs in the interactive session) and
// forwards each real keydown to the Service's POST /keypress
// (Endpoints.MapInput), which feeds the exact same InputListener the
// render loop already reads -- from the render loop's point of view, a
// forwarded keypress is indistinguishable from one the Service's own
// (dev-mode-only) local hook would have produced.
//
// This machine is a gaming laptop, used as one -- the user explicitly
// probed on real-world input latency before agreeing to this design, so
// two requirements here are non-negotiable, not just nice-to-haves:
//
// 1. The hook callback must NEVER block on the network call. A
//    WH_KEYBOARD_LL hook is synchronous and system-wide; blocking it on
//    an HTTP round trip would mean real, system-wide input lag on every
//    single keystroke, not just an app-local slowdown. Forwarding is
//    fired via Task.Run and never awaited from the hook thread.
// 2. Auto-repeat must be filtered before forwarding. A held key (WASD
//    during actual gameplay is the obvious case) fires the low-level
//    hook repeatedly for as long as it's held -- only the initial
//    keydown transition should reach the Service. GlobalKeyboardHook
//    (this project's copy) reports real key-up transitions specifically
//    so this class can track currently-held keys and drop repeats.
//
// On top of those two hard requirements, forwarding is also gated on
// whether a reactive-type feature is actually active (a typing_reactive
// effect, or the lightbar's keyboard-reactive flash) -- not required for
// correctness, but means there's exactly zero hook-triggered network
// traffic the rest of the time, which matters given the stated gaming-
// performance concern. Re-checked periodically since the active effect/
// config can change at any time from the GUI, tray, or another client.

using System.Windows.Threading;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public sealed class KeypressForwarder : IDisposable
{
    private static readonly TimeSpan GatePollInterval = TimeSpan.FromSeconds(3);

    private readonly ApiClient _api;
    private readonly GlobalKeyboardHook _hook;
    private readonly object _heldLock = new();
    private readonly HashSet<string> _heldKeys = new();
    private readonly DispatcherTimer _gateTimer;

    private volatile bool _forwardingEnabled;

    public KeypressForwarder(ApiClient api)
    {
        _api = api;
        _hook = new GlobalKeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        // DispatcherTimer, not System.Threading.Timer -- this class is
        // constructed on the UI thread (App.xaml.cs) and GetStatusAsync/
        // GetLightbarReactiveConfigAsync's continuations are cheapest to
        // reason about back on that same thread; the hook callbacks
        // themselves (OnKeyDown/OnKeyUp) still run on the hook's own
        // message-loop thread regardless, per GlobalKeyboardHook's own
        // header comment.
        _gateTimer = new DispatcherTimer { Interval = GatePollInterval };
        _gateTimer.Tick += async (_, _) => await RefreshGateAsync();
    }

    public void Start()
    {
        _hook.Start();
        _gateTimer.Start();
        _ = RefreshGateAsync(); // don't wait a full interval for the first check
    }

    private async Task RefreshGateAsync()
    {
        try
        {
            StatusResponse? status = await _api.GetStatusAsync();
            bool typingReactive = status?.CurrentEffect == "typing_reactive";

            LightbarReactiveConfig? reactive = await _api.GetLightbarReactiveConfigAsync();
            bool lightbarReactive = reactive?.Enabled ?? false;

            _forwardingEnabled = typingReactive || lightbarReactive;
        }
        catch
        {
            // Service unreachable or a transient error -- default to not
            // forwarding. There's nothing useful to forward to anyway if
            // the Service can't even answer a status check.
            _forwardingEnabled = false;
        }
    }

    private void OnKeyDown(string name)
    {
        lock (_heldLock)
        {
            if (!_heldKeys.Add(name)) return; // already held -- auto-repeat, drop it
        }
        if (!_forwardingEnabled) return;
        _ = Task.Run(() => _api.PostKeypressAsync(name));
    }

    private void OnKeyUp(string name)
    {
        lock (_heldLock)
        {
            _heldKeys.Remove(name);
        }
    }

    public void Dispose()
    {
        _gateTimer.Stop();
        _hook.Dispose();
    }
}
