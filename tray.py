"""System tray icon: a thin client that switches between saved
presets (and raw effects) by calling the daemon's HTTP API -- never
touches hardware directly, exactly like cli.py.

Run the daemon first:
    uvicorn daemon.server:app --port 8420

Then, in another terminal (or as a startup shortcut):
    python tray.py

The preset list is read from presets.json once at startup; restart
the tray icon after saving a new preset (via cli.py preset-save) to
pick it up.
"""

import json
import os
import subprocess
import sys

import pystray
import requests
from PIL import Image

BASE = "http://127.0.0.1:8420"
_ROOT = os.path.dirname(os.path.abspath(__file__))
PRESETS_PATH = os.path.join(_ROOT, "presets.json")
GUI_PATH = os.path.join(_ROOT, "gui.py")
LOGO_PATH = os.path.join(_ROOT, "gui", "logo.png")


def _load_presets() -> dict:
    try:
        with open(PRESETS_PATH) as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def _make_icon_image():
    # Same RGB-gradient "JMA" wordmark used in the GUI's header, so the
    # tray icon and the app look like one product. Regenerate via
    # gui/make_logo.py if it ever needs to change.
    return Image.open(LOGO_PATH)


def _safe_post(icon, path, json_body=None):
    try:
        requests.post(f"{BASE}{path}", json=json_body, timeout=2)
    except requests.exceptions.RequestException as e:
        icon.notify(f"Daemon unreachable: {e}", title="JMA Studio")


def _apply_preset(preset):
    def handler(icon, item):
        _safe_post(icon, "/effect", {"name": preset["effect"], "params": preset["params"]})
    return handler


def _turn_off(icon, item):
    _safe_post(icon, "/off")


def _open_gui(icon, item):
    # Detached, independent process -- closing the tray (or this one
    # exiting) shouldn't take the GUI window down with it.
    subprocess.Popen([sys.executable, GUI_PATH], cwd=_ROOT,
                      creationflags=subprocess.CREATE_NO_WINDOW)


def _quit(icon, item):
    icon.stop()


def build_menu():
    presets = _load_presets()
    items = [
        pystray.MenuItem(name, _apply_preset(preset))
        for name, preset in sorted(presets.items())
    ]
    if not items:
        items = [pystray.MenuItem("(no presets saved)", None, enabled=False)]
    return pystray.Menu(
        pystray.MenuItem("Open Control Panel", _open_gui, default=True),
        pystray.Menu.SEPARATOR,
        *items,
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Off", _turn_off),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Quit", _quit),
    )


def main():
    icon = pystray.Icon("rgb_keyboard", _make_icon_image(), "JMA Studio", build_menu())
    icon.run()


if __name__ == "__main__":
    main()
