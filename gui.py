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
import sys

import requests
import webview

BASE = "http://127.0.0.1:8420"

WINDOW_WIDTH = 1100
WINDOW_HEIGHT = 860


def daemon_reachable() -> bool:
    try:
        requests.get(f"{BASE}/status", timeout=2)
        return True
    except requests.exceptions.RequestException:
        return False


def _initial_position():
    """Centered left-to-right, flush against the top of the screen --
    pywebview's own default placement left the window too low,
    requiring a manual drag up every time it opened."""
    if sys.platform != "win32":
        return None, None
    screen_width = ctypes.windll.user32.GetSystemMetrics(0)  # SM_CXSCREEN
    x = max(0, (screen_width - WINDOW_WIDTH) // 2)
    return x, 0


def main():
    if not daemon_reachable():
        print("Daemon not reachable at 127.0.0.1:8420 -- start it first:")
        print("  uvicorn daemon.server:app --port 8420")
        sys.exit(1)

    x, y = _initial_position()
    webview.create_window(
        "JMA Studio", f"{BASE}/app/",
        width=WINDOW_WIDTH, height=WINDOW_HEIGHT, min_size=(780, 620),
        x=x, y=y,
        background_color="#0b0b12",
    )
    webview.start()


if __name__ == "__main__":
    main()
