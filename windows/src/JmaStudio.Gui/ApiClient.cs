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

    public async Task<bool> SetDefaultPresetAsync(string name)
    {
        var response = await _http.PostAsJsonAsync("/default", new { Name = name }, Json);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> TurnOffAsync() => await SetEffectAsync("static", new StaticParams());

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

    public async Task<Dictionary<string, LightbarPreset>> GetLightbarPresetsAsync() =>
        await _http.GetFromJsonAsync<Dictionary<string, LightbarPreset>>("/lightbar/presets", Json) ?? new();

    public async Task<bool> ApplyLightbarPresetAsync(string name)
    {
        var response = await _http.PostAsync($"/lightbar/presets/{Uri.EscapeDataString(name)}/apply", null);
        return response.IsSuccessStatusCode;
    }

    // ---- controller-reactive ----

    public async Task<ControllerReactiveParams?> GetControllerReactiveSettingsAsync() =>
        await _http.GetFromJsonAsync<ControllerReactiveParams>("/controller-reactive/settings", Json);

    public async Task<bool> EnableControllerReactiveAsync() =>
        (await _http.PostAsync("/controller-reactive/enable", null)).IsSuccessStatusCode;

    public async Task<bool> DisableControllerReactiveAsync() =>
        (await _http.PostAsync("/controller-reactive/disable", null)).IsSuccessStatusCode;
}
