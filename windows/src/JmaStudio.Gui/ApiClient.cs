// Thin HTTP client wrapper around JmaStudio.Service's API (Endpoints.cs)
// -- reuses the exact same strongly-typed models (EffectParams,
// KeyboardPreset, LightbarPreset, RgbColor, ...) via project references,
// and the exact same JSON options (PresetJsonOptions.Default) the
// service itself uses, so the EffectParamsJsonConverter/enum-as-string/
// IncludeFields behavior all just line up automatically -- no separate
// GUI-side JSON configuration to keep in sync.

using System.Net.Http;
using System.Net.Http.Json;
using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public sealed record StatusResponse(
    bool KeyboardConnected, bool ControllerConnected, string CurrentEffect,
    EffectParams Params, int NumCells, long FramesRendered, long FramesWritten);

public sealed record FrameResponse(RgbColor[] Colors);

public sealed record LightbarStatusResponse(bool Connected, LightbarState? State);

public sealed record LayoutCell(int Index, string Name, double Row, double Col);
public sealed record LayoutResponse(LayoutCell[] Cells, int NumCells);

public sealed record DefaultPresetResponse(string? DefaultPreset);
public sealed record CaptureZonesResponse(IReadOnlyList<double> ZoneBoundaries, int NumZones);
public sealed record ControllerReactiveStatusResponse(bool Connected, bool Enabled);
public sealed record ControllerReactiveDefaultsResponse(
    bool BackgroundEnabled, RgbColor BackgroundColor, RgbColor GroupColor, double Deadzone);

// ---- diagnostics ----

public sealed record LatencySnapshot(double Min, double Avg, double Max, long Count);
public sealed record DiagnosticsKeyboardInfo(bool Connected, bool Detected, int VendorId, int ProductId);
public sealed record DiagnosticsLightbarInfo(bool Connected, bool Detected, LightbarState? State);
public sealed record DiagnosticsControllerInfo(bool Connected, bool Detected);
public sealed record DiagnosticsPerfInfo(long FramesRendered, long FramesWritten, LatencySnapshot HidLatencyMs, LatencySnapshot WmiLatencyMs);
public sealed record AcerLightingServiceInfo(string Status, string StartMode);
public sealed record PythonInstallInfo(bool Installed, bool ScheduledTaskExists, bool ScheduledTaskEnabled, string RepoRoot);

public sealed record DiagnosticsStatusResponse(
    double UptimeSeconds, string LogFilePath,
    DiagnosticsKeyboardInfo Keyboard, DiagnosticsLightbarInfo Lightbar, DiagnosticsControllerInfo Controller,
    DiagnosticsPerfInfo Perf, AcerLightingServiceInfo AcerLightingService,
    string[] SuspiciousProcesses, PythonInstallInfo Python);

public sealed record RescanResponse(bool KeyboardDetected, bool LightbarDetected, bool ControllerDetected);
public sealed record ControllerLiveResponse(bool Connected, ControllerState? State);
public sealed record LogsResponse(string[] Lines);

public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl = "http://127.0.0.1:8420")
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
    }

    private static System.Text.Json.JsonSerializerOptions Json => PresetJsonOptions.Default;

    public async Task<StatusResponse?> GetStatusAsync() =>
        await _http.GetFromJsonAsync<StatusResponse>("/status", Json);

    public async Task<FrameResponse?> GetFrameAsync() =>
        await _http.GetFromJsonAsync<FrameResponse>("/frame", Json);

    public async Task<string[]> GetEffectsAsync() =>
        await _http.GetFromJsonAsync<string[]>("/effects", Json) ?? Array.Empty<string>();

    public async Task<bool> SetEffectAsync(string effect, EffectParams parameters)
    {
        var response = await _http.PostAsJsonAsync("/effect", new KeyboardPreset { Effect = effect, Params = parameters }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<Dictionary<string, KeyboardPreset>> GetPresetsAsync() =>
        await _http.GetFromJsonAsync<Dictionary<string, KeyboardPreset>>("/presets", Json) ?? new();

    public async Task<bool> SavePresetAsync(string name)
    {
        var response = await _http.PostAsJsonAsync("/presets/save", new { Name = name }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ApplyPresetAsync(string name)
    {
        var response = await _http.PostAsync($"/presets/{Uri.EscapeDataString(name)}/apply", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeletePresetAsync(string name)
    {
        var response = await _http.DeleteAsync($"/presets/{Uri.EscapeDataString(name)}");
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> GetDefaultPresetAsync()
    {
        var res = await _http.GetFromJsonAsync<DefaultPresetResponse>("/default", Json);
        return res?.DefaultPreset;
    }

    public async Task<bool> SetDefaultPresetAsync(string name)
    {
        var response = await _http.PostAsJsonAsync("/default", new { Name = name }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> TurnOffAsync() => await SetEffectAsync("static", new StaticParams());

    public async Task<bool> SetAllWhiteAsync() =>
        await SetEffectAsync("static", new StaticParams { Color = new RgbColor(255, 255, 255) });

    // ---- system (tray icon's "Close and End Service") ----

    public async Task<bool> ShutdownServiceAsync()
    {
        try
        {
            var response = await _http.PostAsync("/system/shutdown", null);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // Expected in the common case -- the Service starts tearing
            // itself down before the response necessarily finishes
            // flushing back to this client.
            return true;
        }
    }

    // ---- input forwarding (KeypressForwarder) ----

    /// <summary>Forwards one real, already repeat-filtered keydown to the
    /// Service's InputListener -- see KeypressForwarder.cs and
    /// Endpoints.MapInput's header comment for why this exists (Session 0
    /// isolation). Swallows connection failures the same way
    /// ShutdownServiceAsync does: a missed keystroke here should never
    /// throw or crash the hook's caller.</summary>
    public async Task<bool> PostKeypressAsync(string key)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/keypress", new { Key = key }, Json);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<bool> ApplyEffectDefaultAsync(string name) =>
        (await _http.PostAsync($"/effects/{Uri.EscapeDataString(name)}/apply-default", null)).IsSuccessStatusCode;

    public async Task<LayoutResponse?> GetLayoutAsync() =>
        await _http.GetFromJsonAsync<LayoutResponse>("/layout", Json);

    // ---- lightbar ----

    public async Task<LightbarStatusResponse?> GetLightbarStatusAsync() =>
        await _http.GetFromJsonAsync<LightbarStatusResponse>("/lightbar/status", Json);

    public async Task<bool> SetLightbarAllAsync(RgbColor color)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/all", new { Color = color }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetLightbarZoneAsync(int zone, RgbColor color)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/zone", new { Zone = zone, Color = color }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetLightbarModeAsync(LightbarMode mode, RgbColor color, int speed, int brightness)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/mode",
            new { Mode = mode, Color = color, Speed = speed, Brightness = brightness }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetLightbarBrightnessAsync(int value)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/brightness", new { Value = value }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> TurnOffLightbarAsync() =>
        (await _http.PostAsync("/lightbar/off", null)).IsSuccessStatusCode;

    public async Task<Dictionary<string, LightbarPreset>> GetLightbarPresetsAsync() =>
        await _http.GetFromJsonAsync<Dictionary<string, LightbarPreset>>("/lightbar/presets", Json) ?? new();

    public async Task<bool> SaveLightbarPresetAsync(string name)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/presets/save", new { Name = name }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ApplyLightbarPresetAsync(string name)
    {
        var response = await _http.PostAsync($"/lightbar/presets/{Uri.EscapeDataString(name)}/apply", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteLightbarPresetAsync(string name)
    {
        var response = await _http.DeleteAsync($"/lightbar/presets/{Uri.EscapeDataString(name)}");
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> GetLightbarDefaultPresetAsync()
    {
        var res = await _http.GetFromJsonAsync<DefaultPresetResponse>("/lightbar/default", Json);
        return res?.DefaultPreset;
    }

    public async Task<bool> SetLightbarDefaultPresetAsync(string name)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/default", new { Name = name }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<LightbarReactiveConfig?> GetLightbarReactiveConfigAsync() =>
        await _http.GetFromJsonAsync<LightbarReactiveConfig>("/lightbar/reactive", Json);

    public async Task<bool> SetLightbarReactiveConfigAsync(
        bool enabled, RgbColor backgroundColor, IReadOnlyDictionary<int, RgbColor> zoneFlashColors, RgbColor allFlashColor)
    {
        var response = await _http.PostAsJsonAsync("/lightbar/reactive", new
        {
            Enabled = enabled, BackgroundColor = backgroundColor,
            ZoneFlashColors = zoneFlashColors, AllFlashColor = allFlashColor,
        }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<CaptureZonesResponse?> CaptureLightbarReactiveZonesAsync()
    {
        var response = await _http.PostAsync("/lightbar/reactive/capture_zones", null);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<CaptureZonesResponse>(Json)
            : null;
    }

    // ---- controller-reactive ----

    public async Task<ControllerReactiveParams?> GetControllerReactiveSettingsAsync() =>
        await _http.GetFromJsonAsync<ControllerReactiveParams>("/controller-reactive/settings", Json);

    public async Task<bool> SetControllerReactiveSettingsAsync(ControllerReactiveParams settings)
    {
        var response = await _http.PostAsJsonAsync("/controller-reactive/settings", settings, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SaveControllerReactiveSettingsAsync() =>
        (await _http.PostAsync("/controller-reactive/settings/save", null)).IsSuccessStatusCode;

    public async Task<bool> EnableControllerReactiveAsync() =>
        (await _http.PostAsync("/controller-reactive/enable", null)).IsSuccessStatusCode;

    public async Task<bool> DisableControllerReactiveAsync() =>
        (await _http.PostAsync("/controller-reactive/disable", null)).IsSuccessStatusCode;

    public async Task<ControllerReactiveStatusResponse?> GetControllerReactiveStatusAsync() =>
        await _http.GetFromJsonAsync<ControllerReactiveStatusResponse>("/controller-reactive/status", Json);

    public async Task<ControllerReactiveDefaultsResponse?> GetControllerReactiveDefaultsAsync() =>
        await _http.GetFromJsonAsync<ControllerReactiveDefaultsResponse>("/controller-reactive/defaults", Json);

    // ---- diagnostics ----

    public async Task<DiagnosticsStatusResponse?> GetDiagnosticsStatusAsync() =>
        await _http.GetFromJsonAsync<DiagnosticsStatusResponse>("/diagnostics/status", Json);

    public async Task<bool> TestKeyboardAsync() =>
        (await _http.PostAsync("/diagnostics/test-keyboard", null)).IsSuccessStatusCode;

    public async Task<bool> TestLightbarAsync() =>
        (await _http.PostAsync("/diagnostics/test-lightbar", null)).IsSuccessStatusCode;

    public async Task<RescanResponse?> RescanAsync()
    {
        var response = await _http.PostAsync("/diagnostics/rescan", null);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<RescanResponse>(Json) : null;
    }

    public async Task<ControllerLiveResponse?> GetControllerLiveAsync() =>
        await _http.GetFromJsonAsync<ControllerLiveResponse>("/diagnostics/controller-live", Json);

    public async Task<LogsResponse?> GetLogsAsync(int lines = 200) =>
        await _http.GetFromJsonAsync<LogsResponse>($"/diagnostics/logs?lines={lines}", Json);
}
