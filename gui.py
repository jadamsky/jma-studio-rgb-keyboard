"""Native window wrapper around the GUI's HTML/CSS/JS, served by the
daemon at /app. This file is just a thin native shell -- all real
logic lives in gui/app.js talking to the daemon's HTTP API, exactly
like cli.py and tray.py. Nothing here touches hardware.

Run the daemon first:
    uvicorn daemon.server:app --port 8420

Then:
    python gui.py
"""

import ctypes
import os
import sys

import requests
import webview

BASE = "http://127.0.0.1:8420"

WINDOW_WIDTH = 1100
WINDOW_HEIGHT = 860
ICON_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gui", "app_icon.ico")


def daemon_reachable() -> bool:
    try:
        requests.get(f"{BASE}/status", timeout=2)
        return True
    except requests.exceptions.RequestException:
        return False


class Api:
    """Exposed to gui/app.js as `pywebview.api.*`. Only holds UI-level
    concerns (opening windows) -- nothing here touches hardware or the
    daemon directly, same thin-client rule as the rest of this file."""

    def open_lightbar(self):
        webview.create_window(
            "JMA Studio -- Lightbar", f"{BASE}/app/lightbar.html",
            width=520, height=680, min_size=(460, 600),
            background_color="#0b0b12",
        )


def _initial_position():
    """Centered left-to-right, flush against the top of the screen --
    pywebview's own default placement left the window too low,
    requiring a manual drag up every time it opened."""
    if sys.platform != "win32":
        return None, None
    screen_width = ctypes.windll.user32.GetSystemMetrics(0)  # SM_CXSCREEN
    x = max(0, (screen_width - WINDOW_WIDTH) // 2)
    return x, 0


def _set_app_identity():
    """Without this, Windows' taskbar groups/identifies this window by
    its host process (python.exe) rather than by the window itself --
    the title bar draws straight from Form.Icon so it shows the right
    icon regardless, but the taskbar button falls back to python.exe's
    own icon (or none) for identity purposes. Giving the process its
    own distinct AppUserModelID tells Windows to treat it as its own
    application instead of generic python.exe, which is what actually
    makes the taskbar button pick up the window's real icon. Must be
    called before any window is created."""
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("JMA.Studio.RGBKeyboard")
    except (AttributeError, OSError):
        pass


def main():
    if not daemon_reachable():
        print("Daemon not reachable at 127.0.0.1:8420 -- start it first:")
        print("  uvicorn daemon.server:app --port 8420")
        sys.exit(1)

    _set_app_identity()

    x, y = _initial_position()
    webview.create_window(
        "JMA Studio", f"{BASE}/app/",
        width=WINDOW_WIDTH, height=WINDOW_HEIGHT, min_size=(780, 620),
        x=x, y=y,
        background_color="#0b0b12",
        js_api=Api(),
    )
    # pywebview's create_window() has no working `icon` on Windows --
    # this is set at start() instead, which the WinForms backend does
    # support (Icon(_state['icon']) in webview/platforms/winforms.py).
    icon = ICON_PATH if os.path.isfile(ICON_PATH) else None
    webview.start(icon=icon)


if __name__ == "__main__":
    main()
