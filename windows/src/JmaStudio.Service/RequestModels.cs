using JmaStudio.Hardware;

namespace JmaStudio.Service;

public sealed record PresetSaveRequest(string Name);
public sealed record LightbarZoneRequest(int Zone, RgbColor Color);
public sealed record LightbarColorRequest(RgbColor Color);
public sealed record LightbarModeRequest(LightbarMode Mode, RgbColor Color, int Speed = 5, int Brightness = 100);
public sealed record LightbarBrightnessRequest(int Value);
public sealed record DefaultRequest(string Name);
