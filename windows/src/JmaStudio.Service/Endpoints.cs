// HTTP API -- C# analogue of daemon/server.py's endpoint set, but
// modernized per settled decision #7: real status codes / Problem-
// shaped errors instead of always-200 {"ok":...,"error":...}. Not a
// byte-for-byte route mirror of the Python daemon (paths use kebab-case
// for the newer controller-reactive group; the older keyboard/lightbar
// groups mirror Python's own paths since there's no reason to churn
// those) -- this is a NEW client's API surface, not a compatibility layer.
//
// Route coverage is deliberately a solid CORE subset, not an exhaustive
// mirror of every Python endpoint (e.g. no /keypress forwarding path --
// WPF's native controls don't have WebView2's focus-stealing problem, so
// this hasn't been needed) -- see windows/HANDOFF.md's Phase 5/6 sections
// for the explicit list of what's NOT here yet.

using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public static class Endpoints
{
    public static void MapKeyboard(
        WebApplication app, DaemonState state, EffectRegistry registry, PresetStore store,
        Keyboard? keyboard, Controller? controller)
    {
        app.MapGet("/status", () =>
        {
            (string effectName, EffectParams parameters) = state.GetEffect();
            (long rendered, long written) = state.Stats;
            return Results.Ok(new
            {
                keyboardConnected = keyboard is not null,
                controllerConnected = controller?.IsConnected ?? false,
                currentEffect = effectName,
                @params = parameters,
                numCells = KeyboardConstants.NumCells,
                framesRendered = rendered,
                framesWritten = written,
            });
        });

        app.MapGet("/frame", () => Results.Ok(new { colors = state.LastFrame }));

        app.MapGet("/effects", () => Results.Ok(registry.All.Keys.OrderBy(n => n)));

        app.MapPost("/effect", (KeyboardPreset request) =>
        {
            if (registry.TryGet(request.Effect) is null)
            {
                return Results.NotFound(new { error = $"unknown effect '{request.Effect}'" });
            }
            state.SetEffect(request.Effect, request.Params);
            return Results.Ok();
        });

        app.MapPost("/effects/{name}/apply-default", (string name) =>
        {
            IEffect? effect = registry.TryGet(name);
            if (effect is null) return Results.NotFound(new { error = $"unknown effect '{name}'" });
            state.SetEffect(name, effect.DefaultParams);
            return Results.Ok();
        });

        app.MapGet("/presets", () => Results.Ok(store.KeyboardPresets.Load()));

        app.MapPost("/presets/save", (PresetSaveRequest req) =>
        {
            (string effectName, EffectParams parameters) = state.GetEffect();
            var presets = store.KeyboardPresets.Load();
            presets[req.Name] = new KeyboardPreset { Effect = effectName, Params = parameters };
            store.KeyboardPresets.Save(presets);
            return Results.Ok();
        });

        app.MapPost("/presets/{name}/apply", (string name) =>
        {
            var presets = store.KeyboardPresets.Load();
            if (!presets.TryGetValue(name, out KeyboardPreset? preset))
            {
                return Results.NotFound(new { error = $"unknown preset '{name}'" });
            }
            if (registry.TryGet(preset.Effect) is null)
            {
                return Results.UnprocessableEntity(new { error = $"preset '{name}' uses unknown effect '{preset.Effect}'" });
            }
            state.SetEffect(preset.Effect, preset.Params);
            return Results.Ok();
        });

        app.MapDelete("/presets/{name}", (string name) =>
        {
            var presets = store.KeyboardPresets.Load();
            if (!presets.Remove(name)) return Results.NotFound(new { error = $"unknown preset '{name}'" });
            store.KeyboardPresets.Save(presets);
            return Results.Ok();
        });

        app.MapGet("/default", () => Results.Ok(new { defaultPreset = store.AppConfig.Load().DefaultPreset }));

        app.MapPost("/default", (DefaultRequest req) =>
        {
            if (!store.KeyboardPresets.Load().ContainsKey(req.Name))
            {
                return Results.NotFound(new { error = $"unknown preset '{req.Name}'" });
            }
            store.AppConfig.Save(store.AppConfig.Load() with { DefaultPreset = req.Name });
            return Results.Ok();
        });
    }

    public static void MapLightbar(
        WebApplication app, LightbarController lb, PresetStore store,
        DaemonState keyboardState, LightbarReactiveManager reactive)
    {
        app.MapGet("/lightbar/status", () => lb.Available
            ? Results.Ok(new { connected = true, state = lb.GetState() })
            : Results.Ok(new { connected = false, state = (LightbarState?)null }));

        app.MapPost("/lightbar/zone", (LightbarZoneRequest req) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (req.Zone is < 1 or > 3) return Results.BadRequest(new { error = "zone must be 1, 2, or 3" });
            lb.SetZone(req.Zone, req.Color);
            return Results.Ok();
        });

        app.MapPost("/lightbar/all", (LightbarColorRequest req) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            lb.SetAll(req.Color);
            return Results.Ok();
        });

        app.MapPost("/lightbar/mode", (LightbarModeRequest req) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            lb.SetMode(req.Mode, req.Color, req.Speed, req.Brightness);
            return Results.Ok();
        });

        app.MapPost("/lightbar/brightness", (LightbarBrightnessRequest req) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            lb.SetBrightness(req.Value);
            return Results.Ok();
        });

        app.MapPost("/lightbar/off", () =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            lb.Off();
            return Results.Ok();
        });

        app.MapGet("/lightbar/presets", () => Results.Ok(store.LightbarPresets.Load()));

        app.MapPost("/lightbar/presets/save", (PresetSaveRequest req) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var presets = store.LightbarPresets.Load();
            presets[req.Name] = new LightbarPreset { Lightbar = lb.GetState()! };
            store.LightbarPresets.Save(presets);
            return Results.Ok();
        });

        app.MapPost("/lightbar/presets/{name}/apply", (string name) =>
        {
            if (!lb.Available) return Results.Problem("Lightbar not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var presets = store.LightbarPresets.Load();
            if (!presets.TryGetValue(name, out LightbarPreset? preset))
            {
                return Results.NotFound(new { error = $"unknown preset '{name}'" });
            }
            lb.ApplyState(preset.Lightbar);
            return Results.Ok();
        });

        app.MapDelete("/lightbar/presets/{name}", (string name) =>
        {
            var presets = store.LightbarPresets.Load();
            if (!presets.Remove(name)) return Results.NotFound(new { error = $"unknown preset '{name}'" });
            store.LightbarPresets.Save(presets);
            return Results.Ok();
        });

        app.MapGet("/lightbar/default", () => Results.Ok(new { defaultPreset = store.AppConfig.Load().LightbarDefaultPreset }));

        app.MapPost("/lightbar/default", (DefaultRequest req) =>
        {
            if (!store.LightbarPresets.Load().ContainsKey(req.Name))
            {
                return Results.NotFound(new { error = $"unknown preset '{req.Name}'" });
            }
            store.AppConfig.Save(store.AppConfig.Load() with { LightbarDefaultPreset = req.Name });
            return Results.Ok();
        });

        // ---- reactive (keyboard -> lightbar), port of daemon/server.py's
        // GET/POST /lightbar/reactive + /lightbar/reactive/capture_zones ----

        app.MapGet("/lightbar/reactive", () => Results.Ok(store.LightbarReactiveConfig.Load()));

        app.MapPost("/lightbar/reactive", (LightbarReactiveConfigRequest req) =>
        {
            LightbarReactiveConfig existing = store.LightbarReactiveConfig.Load();
            store.LightbarReactiveConfig.Save(existing with
            {
                Enabled = req.Enabled,
                BackgroundColor = req.BackgroundColor,
                ZoneFlashColors = req.ZoneFlashColors,
                AllFlashColor = req.AllFlashColor,
            });
            reactive.ResetDedup(); // force the loop to re-apply on its next tick
            return Results.Ok();
        });

        app.MapPost("/lightbar/reactive/capture_zones", () =>
        {
            // Reads the keyboard's CURRENT gradient zone boundaries
            // (wherever they live -- plain gradient, or gradient wrapped
            // as typing_reactive's base_effect) and stores them for the
            // reactive loop's zone mapping. A one-time snapshot, not a
            // live link -- if the keyboard's zones change later, this
            // needs to be called again.
            (string effectName, EffectParams parameters) = keyboardState.GetEffect();
            GradientParams? gradientParams = (effectName, parameters) switch
            {
                ("gradient", GradientParams gp) => gp,
                ("typing_reactive", TypingReactiveParams { BaseEffectName: "gradient", BaseParams: GradientParams bgp }) => bgp,
                _ => null,
            };
            if (gradientParams is not { Colors: { Count: > 0 }, Boundaries: { Count: > 0 } })
            {
                return Results.BadRequest(new
                {
                    error = "the keyboard isn't currently running a multi-zone gradient " +
                            "(with explicit boundaries) to capture from",
                });
            }
            LightbarReactiveConfig config = store.LightbarReactiveConfig.Load() with
            {
                ZoneBoundaries = gradientParams.Boundaries,
            };
            store.LightbarReactiveConfig.Save(config);
            return Results.Ok(new { zoneBoundaries = config.ZoneBoundaries, numZones = gradientParams.Colors.Count });
        });
    }

    /// <summary>Cell layout -- so the GUI (or any client) can lay out a
    /// visual keyboard without needing its own filesystem access to
    /// keymap.json. Mirrors daemon/server.py's own GET /layout shape.</summary>
    public static void MapLayout(WebApplication app, string keymapPath)
    {
        app.MapGet("/layout", () =>
        {
            var positions = Layout.CellPositions(keymapPath);
            var indexByName = Layout.NameToIndex(keymapPath);
            var nameByIndex = indexByName.ToDictionary(kv => kv.Value, kv => kv.Key);
            var cells = positions
                .OrderBy(kv => kv.Key)
                .Select(kv => new
                {
                    index = kv.Key,
                    name = nameByIndex.GetValueOrDefault(kv.Key, ""),
                    row = kv.Value.Row,
                    col = kv.Value.Col,
                });
            return Results.Ok(new { cells, numCells = KeyboardConstants.NumCells });
        });
    }

    public static void MapControllerReactive(WebApplication app, ControllerReactiveManager manager, PresetStore store, Controller? controller)
    {
        app.MapGet("/controller-reactive/settings", () => Results.Ok(manager.GetLiveSettings()));

        app.MapPost("/controller-reactive/settings", (ControllerReactiveParams settings) =>
        {
            manager.UpdateLiveSettings(settings);
            return Results.Ok();
        });

        app.MapPost("/controller-reactive/settings/save", () =>
        {
            manager.SaveSettings();
            return Results.Ok();
        });

        app.MapPost("/controller-reactive/enable", () =>
        {
            manager.Enable();
            return Results.Ok();
        });

        app.MapPost("/controller-reactive/disable", () =>
        {
            manager.Disable();
            return Results.Ok();
        });

        app.MapGet("/controller-reactive/status", () => Results.Ok(new
        {
            connected = controller is not null && controller.IsConnected,
            enabled = manager.Enabled,
        }));

        // The "Default" button's target values -- a single source of
        // truth shared with ControllerReactiveParams' own record
        // defaults, rather than the GUI hardcoding a second copy of
        // these numbers. Mirrors daemon/server.py's own
        // GET /controller_reactive/defaults.
        app.MapGet("/controller-reactive/defaults", () =>
        {
            var defaults = new ControllerReactiveParams();
            return Results.Ok(new
            {
                backgroundEnabled = defaults.BackgroundEnabled,
                backgroundColor = defaults.BackgroundColor,
                groupColor = defaults.LeftStick.Idle,
                deadzone = defaults.Deadzone,
            });
        });
    }
}
