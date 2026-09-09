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
import win32api
import win32con
import win32event
import win32gui

BASE = "http://127.0.0.1:8420"

WINDOW_WIDTH = 1100
WINDOW_HEIGHT = 860
ICON_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gui", "app_icon.ico")
MAIN_WINDOW_TITLE = "JMA Studio"

# Named per-user (not "Global\\") so this only enforces one instance for
# the current user, matching how the rest of this app's autostart/
# elevation already scopes to the logged-in user rather than the whole
# machine.
_SINGLE_INSTANCE_MUTEX_NAME = "JMAStudioGUI_SingleInstance"


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
        # Reuses the EXACT (x, y) already computed for the main window
        # in main() -- on the main thread, once -- rather than
        # recomputing anything here. This runs on the JS-bridge
        # callback thread, and re-deriving the position from scratch
        # here (via _initial_position() again, or via GetWindowRect)
        # both produced wildly wrong/inconsistent results across
        # attempts (a DPI-virtualization coordinate-space mismatch
        # between threads/callers -- see git history on this file for
        # the two reverted attempts). Reusing the cached, already-
        # correct numbers sidesteps the whole problem.
        x = (_main_window_pos[0] or 0) + 60
        # 60px down felt ~0.25in too low on this 16in/2560x1600 panel
        # (~189 real PPI, but GetSystemMetrics on this process reports a
        # DPI-virtualized 1463x914 -- a 1.75x scale -- so 0.25in of real
        # screen distance is ~27px in this coordinate space). Trimmed to
        # 33 to nudge the window up without recomputing anything else.
        y = (_main_window_pos[1] or 0) + 33
        webview.create_window(
            "JMA Studio -- Lightbar", f"{BASE}/app/lightbar.html",
            width=WINDOW_WIDTH, height=WINDOW_HEIGHT, min_size=(780, 620),
            x=x, y=y,
            background_color="#0b0b12",
        )


# Set once in main(), on the main thread, right after computing the
# main window's own position -- see Api.open_lightbar()'s comment.
_main_window_pos = (None, None)


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


def _focus_existing_instance():
    """Brings an already-running main window to the foreground -- best
    effort; if the window can't be found for some reason, this just
    silently does nothing rather than failing the "only one instance"
    check itself."""
    try:
        hwnd = win32gui.FindWindow(None, MAIN_WINDOW_TITLE)
        if hwnd:
            if win32gui.IsIconic(hwnd):
                win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
            win32gui.SetForegroundWindow(hwnd)
    except Exception:
        pass



# Holds the mutex handle for the whole process lifetime -- see the
# comment in _acquire_single_instance_lock() for why this global is
# required, not just a nice-to-have.
_instance_mutex = None


def _acquire_single_instance_lock() -> bool:
    """Returns True if this is the only running instance (and the
    caller should proceed to open windows), False if another instance
    already holds the lock (and the existing window was focused
    instead).

    The returned handle MUST be kept referenced somewhere for the
    whole process lifetime (hence the module-level global below) --
    if it's left as a local variable here, its refcount hits zero the
    moment this function returns, pywin32's PyHANDLE.__del__ closes the
    underlying handle immediately, and the named mutex is destroyed
    within microseconds of every launch. That silently defeats the
    entire single-instance check: a later launch (even minutes later)
    finds no mutex at all and opens a full duplicate instance instead
    of detecting the "existing" one. Confirmed via a minimal repro
    outside this file before applying this fix -- this was a real,
    reproducible bug, not a hypothetical one."""
    global _instance_mutex
    _instance_mutex = win32event.CreateMutex(None, False, _SINGLE_INSTANCE_MUTEX_NAME)
    already_running = win32api.GetLastError() == 183  # ERROR_ALREADY_EXISTS
    if already_running:
        _focus_existing_instance()
        return False
    return True


def main():
    global _main_window_pos

    if not _acquire_single_instance_lock():
        sys.exit(0)

    if not daemon_reachable():
        print("Daemon not reachable at 127.0.0.1:8420 -- start it first:")
        print("  uvicorn daemon.server:app --port 8420")
        sys.exit(1)

    _set_app_identity()

    x, y = _initial_position()
    _main_window_pos = (x, y)
    main_window = webview.create_window(
        MAIN_WINDOW_TITLE, f"{BASE}/app/",
        width=WINDOW_WIDTH, height=WINDOW_HEIGHT, min_size=(780, 620),
        x=x, y=y,
        background_color="#0b0b12",
        js_api=Api(),
    )
    # Closing the main window ends the whole app, including any other
    # window it opened (the lightbar window) -- pywebview otherwise
    # keeps running until every window is closed individually, which
    # left the lightbar window orphaned if you closed just the main one.
    #
    # This destroys the other window(s) instead of os._exit()-ing the
    # process directly -- an earlier version called os._exit(0) here,
    # which measurably added ~2s to every close (confirmed by timing
    # WM_CLOSE -> callback-fires -> process-actually-gone: the callback
    # itself fired in ~7ms, but the process lingered for ~2s after
    # os._exit(0) was called). That gap is Windows/WebView2 tearing
    # down its child GPU/renderer processes slower after an abrupt
    # process kill than after letting pywebview's own normal per-window
    # close path run. Calling .destroy() on every OTHER window instead
    # lets each one go through that same normal path -- pywebview's own
    # winforms.py backend already calls Application.Exit() once every
    # window (including this one) has closed, so the process still
    # ends on its own, just via the fast path instead of a hard kill.
    def _on_main_closed():
        for w in list(webview.windows):
            if w is not main_window:
                w.destroy()
    main_window.events.closed += _on_main_closed

    # pywebview's create_window() has no working `icon` on Windows --
    # this is set at start() instead, which the WinForms backend does
    # support (Icon(_state['icon']) in webview/platforms/winforms.py).
    icon = ICON_PATH if os.path.isfile(ICON_PATH) else None
    webview.start(icon=icon)
    # No os._exit() here -- webview.start() already blocks until every
    # window (destroyed above or closed directly) is gone, and letting
    # the interpreter exit normally from here is what avoids the ~2s
    # tax documented above. Confirmed nothing keeps the process alive
    # afterward (no lingering non-daemon threads) via direct timing.


if __name__ == "__main__":
    main()
