// System tray icon -- added at the user's explicit request (2026-09-10),
// ahead of Phase 7's installer: "we need to work on the tray icon
// first... I don't think that was ever mentioned in the original port
// prompts." Not in the original settled decisions (see windows/
// HANDOFF.md) -- this whole file's design came out of a short back-
// and-forth with the user, not a port of anything from `main` (the
// Python `tray.py` was used as a starting reference only, then
// diverged in several deliberate ways below).
//
// Lives in the SAME process as the rest of JmaStudio.Gui (the user's
// explicit choice over a separate JmaStudio.Tray process, unlike
// Python's tray.py/gui.py split) -- MainWindow already exists as a
// single long-lived instance (StartupUri-created) this can just Show/
// Hide/Activate directly, no second process to coordinate.
//
// Uses System.Windows.Forms.NotifyIcon, since WPF has no tray-icon
// primitive of its own -- same WinForms-interop pattern already
// established by ColorSwatchButton.cs (UseWindowsForms is already on
// in the .csproj), including that file's convention of fully-qualifying
// every System.Windows.Forms/System.Drawing type inline rather than
// aliasing, since the global usings for both are removed project-wide
// to avoid colliding with WPF's own System.Windows.* types.
//
// Design, per the user's explicit answers (2026-09-10):
// - Left-click opens the Studio window. There is deliberately NO
//   "Open Control Panel" duplicate in the right-click menu.
// - There is deliberately NO plain "Quit". The user's own words:
//   "if the tray is gone the services aren't running" -- the ONLY way
//   to make this icon (and everything else) go away is "Close and End
//   Service" below.
// - The preset list mirrors gui/app.js's dynamic behavior but goes
//   further: BOTH keyboard and lightbar presets are shown (Python's
//   tray.py only ever listed keyboard presets), in two clearly
//   separate submenus rather than one flat list, and refreshed on a
//   timer (Python's tray.py only read presets.json once at startup and
//   required a full restart to see new ones).

using System.Windows;
using System.Windows.Threading;
using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public sealed class TrayIconManager : IDisposable
{
    // Hardcoded, deliberately NOT applied by calling the named
    // "gradient_only"/"BLUE" presets -- the user's own words: "I want
    // you to hard code this not have it call the preset as if it gets
    // deleted that could cause problems". Keyboard values transcribed
    // verbatim from the real migrated gradient_only preset
    // (windows/data/keyboard-presets.json); the lightbar color is the
    // user's own explicit RGB choice, not any existing preset's colors
    // (the real "BLUE" preset isn't actually uniform across zones).
    private static readonly GradientParams ShutdownKeyboardParams = new()
    {
        Colors = null,
        Boundaries = null,
        LeftColor = new RgbColor(20, 90, 230),
        RightColor = new RgbColor(200, 20, 160),
        Boundary = 13.5,
        Hard = true,
        LeftOverrides = new[] { "backspace", "del", "f11", "f12", "ins", "prtsc" },
        RightOverrides = new[] { "backslash", "enter", "left_arrow", "right_ctrl", "right_shift" },
        CustomColors = new Dictionary<string, RgbColor> { ["space"] = new RgbColor(79, 131, 236) },
        Brightness = 0.65,
    };
    private static readonly RgbColor ShutdownLightbarColor = new(18, 46, 255);

    private readonly ApiClient _api;
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.ToolStripMenuItem _keyboardPresetsItem = new("Keyboard Presets");
    private readonly System.Windows.Forms.ToolStripMenuItem _lightbarPresetsItem = new("Lightbar Presets");
    private readonly System.Windows.Forms.ToolStripMenuItem _controllerReactiveItem = new("Controller Reactive");
    private readonly DispatcherTimer _refreshTimer;

    public TrayIconManager(ApiClient api)
    {
        _api = api;

        _menu.Items.Add(_keyboardPresetsItem);
        _menu.Items.Add(_lightbarPresetsItem);
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        _controllerReactiveItem.Click += ControllerReactiveItem_Click;
        _menu.Items.Add(_controllerReactiveItem);
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var offItem = new System.Windows.Forms.ToolStripMenuItem("Off");
        offItem.Click += async (_, _) => await SafeCall(_api.TurnOffAsync());
        _menu.Items.Add(offItem);

        var allWhiteItem = new System.Windows.Forms.ToolStripMenuItem("All White");
        allWhiteItem.Click += async (_, _) => await SafeCall(_api.SetAllWhiteAsync());
        _menu.Items.Add(allWhiteItem);
        _menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var closeAndEndItem = new System.Windows.Forms.ToolStripMenuItem("Close and End Service");
        closeAndEndItem.Click += CloseAndEndServiceItem_Click;
        _menu.Items.Add(closeAndEndItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "JMA Studio",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // NotifyIcon.Click fires on a left-click; a right-click shows
        // ContextMenuStrip automatically without raising Click at all --
        // standard (if under-documented) WinForms NotifyIcon behavior
        // this design relies on for "left-click opens the Studio, no
        // duplicate entry needed in the right-click menu".
        _notifyIcon.Click += NotifyIcon_Click;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        System.IO.Stream stream = Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app_icon.ico"))!.Stream;
        using (stream) return new System.Drawing.Icon(stream);
    }

    private void NotifyIcon_Click(object? sender, EventArgs e)
    {
        if (e is System.Windows.Forms.MouseEventArgs { Button: System.Windows.Forms.MouseButtons.Left })
        {
            ShowMainWindow();
        }
    }

    private static void ShowMainWindow()
    {
        if (Application.Current.MainWindow is not { } window) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>Re-fetches presets + controller-reactive status and
    /// rebuilds the menu -- called on a 5s timer and once at startup, so
    /// "dynamically change as I add or remove presets" holds without
    /// needing every save/delete call site across MainWindow/
    /// LightbarWindow to remember to notify this class.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            Dictionary<string, KeyboardPreset> keyboardPresets = await _api.GetPresetsAsync();
            RebuildPresetSubmenu(_keyboardPresetsItem, keyboardPresets.Keys, name => SafeCall(_api.ApplyPresetAsync(name)));

            Dictionary<string, LightbarPreset> lightbarPresets = await _api.GetLightbarPresetsAsync();
            RebuildPresetSubmenu(_lightbarPresetsItem, lightbarPresets.Keys, name => SafeCall(_api.ApplyLightbarPresetAsync(name)));

            ControllerReactiveStatusResponse? status = await _api.GetControllerReactiveStatusAsync();
            _controllerReactiveItem.Checked = status?.Enabled ?? false;
        }
        catch
        {
            // Service unreachable -- leave whatever the menu last showed;
            // the next 5s tick retries once it's reachable again.
        }
    }

    private static void RebuildPresetSubmenu(
        System.Windows.Forms.ToolStripMenuItem parent, IEnumerable<string> names, Func<string, Task> onClick)
    {
        parent.DropDownItems.Clear();
        string[] sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        if (sorted.Length == 0)
        {
            parent.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("(none saved)") { Enabled = false });
            return;
        }
        foreach (string name in sorted)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(name);
            item.Click += async (_, _) => await onClick(name);
            parent.DropDownItems.Add(item);
        }
    }

    private async void ControllerReactiveItem_Click(object? sender, EventArgs e)
    {
        bool enable = !_controllerReactiveItem.Checked;
        await SafeCall(enable ? _api.EnableControllerReactiveAsync() : _api.DisableControllerReactiveAsync());
        _controllerReactiveItem.Checked = enable;
    }

    private async void CloseAndEndServiceItem_Click(object? sender, EventArgs e)
    {
        // Hardcoded final visual state, then a graceful Service shutdown
        // (POST /system/shutdown -- the Service stops itself, releasing
        // its HID/WMI handles on normal process exit; nothing here tries
        // to force-kill it directly, since this GUI process runs
        // unelevated and the Service runs elevated -- Windows won't let
        // a lower-integrity process terminate a higher one anyway), then
        // this process exits too. See this file's header comment for why
        // none of this calls the named gradient_only/BLUE presets.
        //
        // Each call gets its own try/catch -- NOT one shared block. A
        // shared block would mean one early failure (e.g. the Service
        // going down mid-sequence) silently skips every call after it,
        // which is exactly what happened live once (2026-09-10): the
        // keyboard gradient landed but the lightbar brightness/color
        // calls never even ran because the whole block had already
        // thrown. Independent try/catches mean each command gets a real
        // attempt regardless of whether an earlier one failed.
        await SafeCall(_api.SetEffectAsync("gradient", ShutdownKeyboardParams));
        await SafeCall(_api.SetLightbarBrightnessAsync(100));
        await SafeCall(_api.SetLightbarAllAsync(ShutdownLightbarColor));
        await SafeCall(_api.ShutdownServiceAsync());

        App.IsShuttingDown = true;
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    private static async Task SafeCall(Task<bool> call)
    {
        try { await call; }
        catch { /* tray menu actions fail silently -- there's no window here to show a toast in */ }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
