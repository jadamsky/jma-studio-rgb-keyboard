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
_CONFIG_PATH = os.path.join(_PROJECT_ROOT, "config.json")
_GUI_DIR = os.path.join(_PROJECT_ROOT, "gui")
_KEY_STATE_MAX_AGE = 5.0  # seconds of press history kept for effects to read

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
