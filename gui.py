"""Native window wrapper around the GUI's HTML/CSS/JS, served by the
daemon at /app. This file is just a thin native shell -- all real
logic lives in gui/app.js talking to the daemon's HTTP API, exactly
like cli.py and tray.py. Nothing here touches hardware.

Run the daemon first:
    uvicorn daemon.server:app --port 8420

Then:
    python gui.py
"""

import sys

import requests
import webview

BASE = "http://127.0.0.1:8420"


def daemon_reachable() -> bool:
    try:
        requests.get(f"{BASE}/status", timeout=2)
        return True
    except requests.exceptions.RequestException:
        return False


def main():
    if not daemon_reachable():
        print("Daemon not reachable at 127.0.0.1:8420 -- start it first:")
        print("  uvicorn daemon.server:app --port 8420")
        sys.exit(1)

    webview.create_window(
        "JMA Studio", f"{BASE}/app/",
        width=1100, height=860, min_size=(780, 620),
        background_color="#0b0b12",
    )
    webview.start()


if __name__ == "__main__":
    main()
