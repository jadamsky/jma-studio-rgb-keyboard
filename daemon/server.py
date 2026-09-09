"""
Background daemon: owns the HID connection and the currently-running
effect. The CLI and any future UI talk to this over HTTP -- neither
ever touches the hardware directly.

Run with:  uvicorn daemon.server:app --port 8420
"""

import asyncio
import importlib
import json
import os
import pkgutil
import time

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from daemon.input_listener import InputListener
from effects.layout import cell_positions
from hardware.device import Keyboard, NUM_CELLS
from hardware.lightbar import Lightbar

app = FastAPI()

_PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_KEYMAP_PATH = os.path.join(_PROJECT_ROOT, "keymap.json")
_PRESETS_PATH = os.path.join(_PROJECT_ROOT, "presets.json")
_LIGHTBAR_PRESETS_PATH = os.path.join(_PROJECT_ROOT, "lightbar_presets.json")
_LIGHTBAR_REACTIVE_PATH = os.path.join(_PROJECT_ROOT, "lightbar_reactive.json")
_CONFIG_PATH = os.path.join(_PROJECT_ROOT, "config.json")
_GUI_DIR = os.path.join(_PROJECT_ROOT, "gui")
_KEY_STATE_MAX_AGE = 5.0  # seconds of press history kept for effects to read
_REACTIVE_TICK = 0.08  # seconds -- ~12.5Hz, matched to what the lightbar's WMI protocol can sustain (see Lightbar.flash_zones), not the keyboard's 30fps
_REACTIVE_PRESS_MAX_AGE = 0.15  # seconds -- how long a keypress keeps its zone "flashed" before reverting to background

_keyboard = None
_lightbar = None
_effects = {}  # name -> render function
_current_effect = "static"
_current_params = {"color": (0, 0, 0)}
_start_time = time.monotonic()
_input_listener = None
_last_frame = []  # most recently rendered [r,g,b] per cell, for the GUI's live preview
_last_sent_frame = None  # the frame actually written to hardware last, to skip redundant writes
_frames_rendered = 0  # total render() calls since startup
_frames_written = 0   # of those, how many actually differed and got sent to hardware

# Cell index -> (row, col), for mapping a keypress to one of the reactive
# feature's 4 keyboard zones by column -- same position data the gradient
# effect uses, loaded once here rather than importing gradient.py's
# private zone-index helper across module boundaries.
_KB_POSITIONS = cell_positions(_KEYMAP_PATH)


def _zone_for_column(col: float, boundaries: list) -> int:
    """0-based zone index for a column position, given sorted boundary
    thresholds -- same logic as effects/gradient.py's _zone_index(),
    kept as an independent copy since input_listener/reactive-loop code
    stays self-contained rather than reaching into effects/ internals."""
    zone = 0
    for b in sorted(boundaries):
        if col >= b:
            zone += 1
        else:
            break
    return zone

_DEFAULT_REACTIVE_CONFIG = {
    "enabled": False,
    "zone_boundaries": [],  # captured column thresholds; empty until captured once
    "background_color": [0, 0, 0],
    "zone_flash_colors": {"1": [255, 0, 0], "2": [0, 255, 0], "3": [0, 0, 255]},
    "all_flash_color": [255, 220, 0],
}
_last_reactive_targets = None  # dedupe -- only write to hardware when something actually changed


def _load_effects():
    """Scan the effects/ package for modules exposing NAME + render()."""
    import effects
    for _, mod_name, _ in pkgutil.iter_modules(effects.__path__):
        mod = importlib.import_module(f"effects.{mod_name}")
        if hasattr(mod, "render") and hasattr(mod, "NAME"):
            _effects[mod.NAME] = mod.render


def _load_json(path: str) -> dict:
    try:
        with open(path) as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def _load_startup_default():
    """If config.json names a default_preset that exists in
    presets.json (and whose effect is actually loaded), returns its
    (effect, params); otherwise None, leaving the built-in off/black
    default in place. Changed via `cli.py set-default <name>`."""
    name = _load_json(_CONFIG_PATH).get("default_preset")
    if not name:
        return None
    preset = _load_json(_PRESETS_PATH).get(name)
    if not preset or preset.get("effect") not in _effects:
        print(f"[daemon] WARNING: default_preset {name!r} not found or invalid, "
              f"using built-in default")
        return None
    print(f"[daemon] startup default: preset {name!r} ({preset['effect']})")
    return preset["effect"], preset.get("params", {})


def _load_lightbar_startup_default():
    """Mirrors _load_startup_default() for the lightbar's own,
    separate preset store/config key. Changed via the lightbar
    window's "Set as default" button."""
    name = _load_json(_CONFIG_PATH).get("lightbar_default_preset")
    if not name:
        return None
    preset = _load_json(_LIGHTBAR_PRESETS_PATH).get(name)
    if not preset:
        print(f"[daemon] WARNING: lightbar_default_preset {name!r} not found, "
              f"using built-in default")
        return None
    print(f"[daemon] lightbar startup default: preset {name!r}")
    return preset


class EffectRequest(BaseModel):
    name: str
    params: dict = {}


class ColorRequest(BaseModel):
    hex: str  # e.g. "ff0000"


class KeypressRequest(BaseModel):
    name: str  # keymap.json key name


class LightbarZoneRequest(BaseModel):
    zone: int  # 1 (left), 2 (center), or 3 (right)
    hex: str


class LightbarColorRequest(BaseModel):
    hex: str


class LightbarBrightnessRequest(BaseModel):
    value: int  # 0-100


class LightbarPresetSaveRequest(BaseModel):
    name: str


class LightbarDefaultRequest(BaseModel):
    name: str


def _hex_to_rgb(hex_str: str):
    hex_str = hex_str.lstrip("#")
    return int(hex_str[0:2], 16), int(hex_str[2:4], 16), int(hex_str[4:6], 16)


@app.on_event("startup")
async def startup():
    global _keyboard, _lightbar, _input_listener, _current_effect, _current_params
    _load_effects()
    try:
        _keyboard = Keyboard()
    except RuntimeError as e:
        print(f"[daemon] WARNING: running without hardware -- {e}")
        _keyboard = None
    try:
        _lightbar = Lightbar()
        print("[daemon] lightbar initialized")
        lightbar_default = _load_lightbar_startup_default()
        if lightbar_default is not None:
            try:
                _lightbar.apply_state(lightbar_default.get("lightbar", lightbar_default))
                if "reactive" in lightbar_default:
                    _apply_reactive_settings(lightbar_default["reactive"])
            except Exception as e:
                print(f"[daemon] WARNING: failed to apply lightbar startup default -- {e}")
    except Exception as e:
        print(f"[daemon] WARNING: running without lightbar -- {e}")
        _lightbar = None
    try:
        _input_listener = InputListener(_KEYMAP_PATH)
        _input_listener.start()
    except Exception as e:
        print(f"[daemon] WARNING: keyboard input listener failed to start -- {e}")
        _input_listener = None

    startup_default = _load_startup_default()
    if startup_default is not None:
        _current_effect, _current_params = startup_default

    asyncio.create_task(_render_loop())
    asyncio.create_task(_lightbar_reactive_loop())


async def _render_loop(fps: int = 30):
    global _last_frame, _last_sent_frame, _frames_rendered, _frames_written
    interval = 1 / fps
    loop = asyncio.get_event_loop()
    while True:
        if _keyboard is not None and _current_effect in _effects:
            t = time.monotonic() - _start_time
            frame_params = dict(_current_params)
            if _input_listener is not None:
                frame_params["key_state"] = _input_listener.snapshot(_KEY_STATE_MAX_AGE)
            colors = _effects[_current_effect](t, NUM_CELLS, frame_params)
            _last_frame = colors
            _frames_rendered += 1
            # Skip the actual USB write when the frame is visually
            # identical to the last one actually sent -- a fully static
            # effect (a flat gradient, "off", gaming_zone, ...) was
            # otherwise re-sent 30x/sec forever, a constant stream of
            # USB traffic for a keyboard that never visibly changes.
            # Still recomputed every frame either way (cheap for every
            # effect here), just not re-written to hardware when nothing
            # actually changed.
            if colors != _last_sent_frame:
                try:
                    # Run the blocking HID write in a worker thread rather than
                    # inline on the event loop -- this is a real hardware write
                    # (syscall-level, milliseconds), and doing it synchronously
                    # here was suspected of stalling the event loop long enough
                    # (30x/sec) to starve the `keyboard` library's OS-level
                    # hook thread of GIL time, which made the typing-reactive
                    # chase stop responding whenever the GUI's frequent polling
                    # added extra threadpool contention.
                    await loop.run_in_executor(None, _keyboard.send_frame, colors)
                    _last_sent_frame = colors
                    _frames_written += 1
                except Exception as e:
                    print(f"[daemon] frame write failed: {e}")
        await asyncio.sleep(interval)


async def _lightbar_reactive_loop():
    """Flashes lightbar zones in response to keyboard presses, mapped by
    which of the keyboard's 4 gradient zones (captured once via
    POST /lightbar/reactive/capture_zones) the pressed key falls in --
    zones 1-3 flash the matching lightbar zone, zone 4 flashes all
    three. Ticks much slower than _render_loop() (see _REACTIVE_TICK)
    since the lightbar's WMI protocol can't sustain 30Hz.

    Reads _input_listener.snapshot() with the SAME max_age the keyboard's
    own render loop uses (_KEY_STATE_MAX_AGE), then filters the result
    down to a much shorter window locally -- snapshot() prunes its
    internal press history to whatever max_age is passed in as a side
    effect, so calling it here with a short max_age would have silently
    cut short the keyboard's own typing_reactive decay/bolt-travel
    window. Reading with the same max_age as that loop keeps this
    read-only from typing_reactive's point of view."""
    global _last_reactive_targets
    loop = asyncio.get_event_loop()
    while True:
        config = dict(_DEFAULT_REACTIVE_CONFIG)
        config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
        boundaries = config.get("zone_boundaries")
        if _lightbar is not None and config.get("enabled") and _input_listener is not None and boundaries:
            snap = _input_listener.snapshot(_KEY_STATE_MAX_AGE)
            active_zones = set()
            for idx, ages in snap.items():
                if min(ages) > _REACTIVE_PRESS_MAX_AGE:
                    continue
                pos = _KB_POSITIONS.get(idx)
                if pos is None:
                    continue
                active_zones.add(_zone_for_column(pos[1], boundaries) + 1)

            bg = tuple(config["background_color"])
            if 4 in active_zones:
                allc = tuple(config["all_flash_color"])
                targets = {1: allc, 2: allc, 3: allc}
            else:
                zone_colors = config["zone_flash_colors"]
                targets = {
                    z: (tuple(zone_colors[str(z)]) if z in active_zones else bg)
                    for z in (1, 2, 3)
                }

            if targets != _last_reactive_targets:
                try:
                    await loop.run_in_executor(None, _lightbar.flash_zones, targets)
                    _last_reactive_targets = targets
                except Exception as e:
                    print(f"[daemon] lightbar reactive write failed: {e}")
        await asyncio.sleep(_REACTIVE_TICK)


@app.get("/effects")
def list_effects():
    return {"available": sorted(_effects.keys()), "current": _current_effect}


@app.post("/effect")
def set_effect(req: EffectRequest):
    global _current_effect, _current_params
    if req.name not in _effects:
        return {"ok": False, "error": f"unknown effect '{req.name}'"}
    _current_effect = req.name
    _current_params = req.params
    return {"ok": True}


@app.post("/color")
def set_color(req: ColorRequest):
    global _current_effect, _current_params
    r, g, b = _hex_to_rgb(req.hex)
    _current_effect = "static"
    _current_params = {"color": (r, g, b)}
    return {"ok": True}


@app.post("/off")
def off():
    global _current_effect, _current_params
    _current_effect = "static"
    _current_params = {"color": (0, 0, 0)}
    return {"ok": True}


@app.post("/lightbar/zone")
def set_lightbar_zone(req: LightbarZoneRequest):
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    r, g, b = _hex_to_rgb(req.hex)
    try:
        _lightbar.set_zone(req.zone, r, g, b)
    except Exception as e:
        return {"ok": False, "error": str(e)}
    return {"ok": True}


@app.post("/lightbar/all")
def set_lightbar_all(req: LightbarColorRequest):
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    r, g, b = _hex_to_rgb(req.hex)
    try:
        _lightbar.set_all(r, g, b)
    except Exception as e:
        return {"ok": False, "error": str(e)}
    return {"ok": True}


@app.post("/lightbar/off")
def lightbar_off():
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    try:
        _lightbar.off()
    except Exception as e:
        return {"ok": False, "error": str(e)}
    return {"ok": True}


@app.post("/lightbar/brightness")
def set_lightbar_brightness(req: LightbarBrightnessRequest):
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    try:
        _lightbar.set_brightness(req.value)
    except Exception as e:
        return {"ok": False, "error": str(e)}
    return {"ok": True}


class LightbarModeRequest(BaseModel):
    mode: str  # breathing / neon / rainbow / wave / ripple / scanner / strobe
    hex: str
    speed: int = 5
    brightness: int = 100


@app.post("/lightbar/mode")
def set_lightbar_mode(req: LightbarModeRequest):
    """Firmware-native animated mode -- whole-bar single color only (no
    per-zone control), confirmed working live on real hardware. See
    hardware/lightbar.py's set_mode() docstring for the protocol
    details/history."""
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    r, g, b = _hex_to_rgb(req.hex)
    try:
        _lightbar.set_mode(req.mode, r, g, b, req.speed, req.brightness)
    except Exception as e:
        return {"ok": False, "error": str(e)}
    return {"ok": True}


class LightbarReactiveConfigRequest(BaseModel):
    enabled: bool
    background_color: list  # [r,g,b]
    zone_flash_colors: dict  # {"1": [r,g,b], "2": [...], "3": [...]}
    all_flash_color: list  # [r,g,b]


@app.get("/lightbar/reactive")
def get_lightbar_reactive_config():
    config = dict(_DEFAULT_REACTIVE_CONFIG)
    config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
    return config


@app.post("/lightbar/reactive")
def set_lightbar_reactive_config(req: LightbarReactiveConfigRequest):
    global _last_reactive_targets
    config = dict(_DEFAULT_REACTIVE_CONFIG)
    config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
    config["enabled"] = req.enabled
    config["background_color"] = req.background_color
    config["zone_flash_colors"] = req.zone_flash_colors
    config["all_flash_color"] = req.all_flash_color
    with open(_LIGHTBAR_REACTIVE_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    _last_reactive_targets = None  # force the loop to re-apply on its next tick
    return {"ok": True}


@app.post("/lightbar/reactive/capture_zones")
def capture_lightbar_reactive_zones():
    """Reads the keyboard's CURRENT gradient zone boundaries (wherever
    they live -- plain gradient, or gradient wrapped as typing_reactive's
    base_effect) and stores them for the reactive loop's zone mapping.
    A one-time snapshot, not a live link -- if the keyboard's zones
    change later, this needs to be called again."""
    params = _current_params
    effect = _current_effect
    if effect == "typing_reactive" and params.get("base_effect") == "gradient":
        params = params.get("base_params", {})
        effect = "gradient"
    if effect != "gradient" or "boundaries" not in params or "colors" not in params:
        return {
            "ok": False,
            "error": "the keyboard isn't currently running a multi-zone gradient "
                     "(with explicit boundaries) to capture from",
        }
    config = dict(_DEFAULT_REACTIVE_CONFIG)
    config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
    config["zone_boundaries"] = list(params["boundaries"])
    with open(_LIGHTBAR_REACTIVE_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    return {"ok": True, "zone_boundaries": config["zone_boundaries"], "num_zones": len(params["colors"])}


@app.get("/lightbar/presets")
def list_lightbar_presets():
    return _load_json(_LIGHTBAR_PRESETS_PATH)


def _reactive_settings_snapshot() -> dict:
    """The reactive fields worth capturing in a preset -- deliberately
    excludes zone_boundaries, which describes the KEYBOARD's own zone
    geometry (captured once via /lightbar/reactive/capture_zones) rather
    than anything that should vary preset to preset."""
    config = dict(_DEFAULT_REACTIVE_CONFIG)
    config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
    return {
        "enabled": config["enabled"],
        "background_color": config["background_color"],
        "zone_flash_colors": config["zone_flash_colors"],
        "all_flash_color": config["all_flash_color"],
    }


def _apply_reactive_settings(settings: dict):
    global _last_reactive_targets
    config = dict(_DEFAULT_REACTIVE_CONFIG)
    config.update(_load_json(_LIGHTBAR_REACTIVE_PATH))
    config.update(settings)  # leaves zone_boundaries untouched
    with open(_LIGHTBAR_REACTIVE_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    _last_reactive_targets = None  # force the reactive loop to re-apply on its next tick


@app.post("/lightbar/presets/save")
def save_lightbar_preset(req: LightbarPresetSaveRequest):
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    presets = _load_json(_LIGHTBAR_PRESETS_PATH)
    presets[req.name] = {
        "lightbar": _lightbar.get_state(),
        "reactive": _reactive_settings_snapshot(),
    }
    with open(_LIGHTBAR_PRESETS_PATH, "w") as f:
        json.dump(presets, f, indent=2, sort_keys=True)
    return {"ok": True}


@app.post("/lightbar/presets/{name}/apply")
def apply_lightbar_preset(name: str):
    if _lightbar is None:
        return {"ok": False, "error": "lightbar not available"}
    presets = _load_json(_LIGHTBAR_PRESETS_PATH)
    preset = presets.get(name)
    if preset is None:
        return {"ok": False, "error": f"unknown preset '{name}'"}
    # Back-compat: presets saved before reactive settings were bundled in
    # are a flat {"type": ..., ...} dict with no "lightbar"/"reactive"
    # keys -- treat the whole thing as the lightbar part and leave
    # reactive settings as they currently are.
    lightbar_state = preset.get("lightbar", preset)
    try:
        _lightbar.apply_state(lightbar_state)
    except Exception as e:
        return {"ok": False, "error": str(e)}
    if "reactive" in preset:
        _apply_reactive_settings(preset["reactive"])
    return {"ok": True}


@app.delete("/lightbar/presets/{name}")
def delete_lightbar_preset(name: str):
    presets = _load_json(_LIGHTBAR_PRESETS_PATH)
    if name not in presets:
        return {"ok": False, "error": f"unknown preset '{name}'"}
    del presets[name]
    with open(_LIGHTBAR_PRESETS_PATH, "w") as f:
        json.dump(presets, f, indent=2, sort_keys=True)
    return {"ok": True}


@app.get("/lightbar/default")
def get_lightbar_default():
    return {"default_preset": _load_json(_CONFIG_PATH).get("lightbar_default_preset")}


@app.post("/lightbar/default")
def set_lightbar_default(req: LightbarDefaultRequest):
    presets = _load_json(_LIGHTBAR_PRESETS_PATH)
    if req.name not in presets:
        return {"ok": False, "error": f"unknown preset '{req.name}'"}
    config = _load_json(_CONFIG_PATH)
    config["lightbar_default_preset"] = req.name
    with open(_CONFIG_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    return {"ok": True}


@app.get("/status")
async def status():
    return {
        "hardware_connected": _keyboard is not None,
        "lightbar_connected": _lightbar is not None,
        "current_effect": _current_effect,
        "params": _current_params,
        "num_cells": NUM_CELLS,
        "input_listener": (
            _input_listener.diagnostics() if _input_listener is not None else None
        ),
        "render_stats": {
            "frames_rendered": _frames_rendered,
            "frames_written": _frames_written,
        },
    }


@app.get("/frame")
async def frame():
    """The actual most-recently-rendered per-cell colors, for a GUI
    live preview that mirrors the real keyboard (including reactive
    effects) rather than recomputing frames itself."""
    return {
        "colors": _last_frame,
        "current_effect": _current_effect,
        "params": _current_params,
    }


@app.post("/keypress")
async def keypress(req: KeypressRequest):
    """Registers a key press by keymap.json name directly, bypassing
    the OS-level global hook. The GUI's own JS calls this from a
    `keydown` listener as a focus-independent fallback input path --
    see InputListener.register_named_press for why this is needed."""
    if _input_listener is None:
        return {"ok": False, "error": "input listener not running"}
    matched = _input_listener.register_named_press(req.name)
    return {"ok": matched}


@app.get("/layout")
def layout():
    """Physical (row, col) grid position + key name for every mapped
    cell, so a GUI can draw a keyboard-shaped preview without
    duplicating keymap.json/layout.py parsing itself."""
    keymap = _load_json(_KEYMAP_PATH)
    positions = cell_positions(_KEYMAP_PATH)
    cells = [
        {"index": idx, "name": keymap.get(str(idx), ""), "row": pos[0], "col": pos[1]}
        for idx, pos in sorted(positions.items())
    ]
    return {"cells": cells, "num_cells": NUM_CELLS}


class PresetSaveRequest(BaseModel):
    name: str


@app.get("/presets")
def list_presets():
    return _load_json(_PRESETS_PATH)


@app.post("/presets/save")
def save_preset(req: PresetSaveRequest):
    presets = _load_json(_PRESETS_PATH)
    presets[req.name] = {"effect": _current_effect, "params": _current_params}
    with open(_PRESETS_PATH, "w") as f:
        json.dump(presets, f, indent=2, sort_keys=True)
    return {"ok": True}


@app.post("/presets/{name}/apply")
def apply_preset(name: str):
    global _current_effect, _current_params
    presets = _load_json(_PRESETS_PATH)
    preset = presets.get(name)
    if preset is None:
        return {"ok": False, "error": f"unknown preset '{name}'"}
    if preset["effect"] not in _effects:
        return {"ok": False, "error": f"preset '{name}' uses unknown effect '{preset['effect']}'"}
    _current_effect = preset["effect"]
    _current_params = preset.get("params", {})
    return {"ok": True}


@app.delete("/presets/{name}")
def delete_preset(name: str):
    presets = _load_json(_PRESETS_PATH)
    if name not in presets:
        return {"ok": False, "error": f"unknown preset '{name}'"}
    del presets[name]
    with open(_PRESETS_PATH, "w") as f:
        json.dump(presets, f, indent=2, sort_keys=True)
    return {"ok": True}


class DefaultRequest(BaseModel):
    name: str


@app.get("/default")
def get_default():
    return {"default_preset": _load_json(_CONFIG_PATH).get("default_preset")}


@app.post("/default")
def set_default(req: DefaultRequest):
    presets = _load_json(_PRESETS_PATH)
    if req.name not in presets:
        return {"ok": False, "error": f"unknown preset '{req.name}'"}
    config = _load_json(_CONFIG_PATH)
    config["default_preset"] = req.name
    with open(_CONFIG_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    return {"ok": True}


# Serves the GUI's static frontend (see gui/) at /app -- kept as plain
# static files so the GUI is just another thin HTTP client, same as
# cli.py and tray.py, with no server-side templating.
if os.path.isdir(_GUI_DIR):
    app.mount("/app", StaticFiles(directory=_GUI_DIR, html=True), name="gui")
