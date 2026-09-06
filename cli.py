"""Simple CLI for talking to the daemon while testing.

Run the daemon first:
    uvicorn daemon.server:app --port 8420

Then, in another terminal:
    python cli.py static ff0000
    python cli.py effect rainbow
    python cli.py off
    python cli.py status
    python cli.py list
    python cli.py discover
    python cli.py discover --indices 7,8,9,14
    python cli.py preset-save white_on_white
    python cli.py preset-load white_on_white
    python cli.py preset-list
    python cli.py set-default gradient_only
    python cli.py show-default
"""

import argparse
import json
import os

import requests

BASE = "http://127.0.0.1:8420"
PROBE_COLOR = [255, 255, 255]
PRESETS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "presets.json")
CONFIG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "config.json")


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)

    p_static = sub.add_parser("static", help="set a solid color, e.g. ff0000")
    p_static.add_argument("hex")

    p_effect = sub.add_parser("effect", help="switch to an effect by name")
    p_effect.add_argument("name")

    sub.add_parser("off")
    sub.add_parser("status")
    sub.add_parser("list")

    p_discover = sub.add_parser(
        "discover",
        help="interactively map each cell index to a physical key, saving to keymap.json",
    )
    p_discover.add_argument("--output", default="keymap.json")
    p_discover.add_argument("--start", type=int, default=0)
    p_discover.add_argument(
        "--indices",
        default=None,
        help="comma-separated list of specific cell indices to (re-)visit, "
             "e.g. 7,8,9,14 -- overrides --start for a targeted pass",
    )

    p_preset_save = sub.add_parser(
        "preset-save", help="save the currently active effect+params as a named preset"
    )
    p_preset_save.add_argument("name")

    p_preset_load = sub.add_parser("preset-load", help="load a saved preset by name")
    p_preset_load.add_argument("name")

    sub.add_parser("preset-list", help="list saved presets")

    p_set_default = sub.add_parser(
        "set-default", help="set which saved preset the daemon loads on startup"
    )
    p_set_default.add_argument("name")

    sub.add_parser("show-default", help="show the current startup default preset")

    args = parser.parse_args()

    if args.command == "static":
        r = requests.post(f"{BASE}/color", json={"hex": args.hex})
    elif args.command == "effect":
        r = requests.post(f"{BASE}/effect", json={"name": args.name, "params": {}})
    elif args.command == "off":
        r = requests.post(f"{BASE}/off")
    elif args.command == "status":
        r = requests.get(f"{BASE}/status")
    elif args.command == "list":
        r = requests.get(f"{BASE}/effects")
    elif args.command == "discover":
        indices = None
        if args.indices:
            indices = [int(x) for x in args.indices.split(",") if x.strip() != ""]
        discover(args.output, args.start, indices)
        return
    elif args.command == "preset-save":
        preset_save(args.name)
        return
    elif args.command == "preset-load":
        preset_load(args.name)
        return
    elif args.command == "preset-list":
        preset_list()
        return
    elif args.command == "set-default":
        set_default(args.name)
        return
    elif args.command == "show-default":
        show_default()
        return

    print(r.json())


def _load_presets() -> dict:
    try:
        with open(PRESETS_PATH) as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def preset_save(name: str):
    status = requests.get(f"{BASE}/status").json()
    presets = _load_presets()
    presets[name] = {"effect": status["current_effect"], "params": status["params"]}
    with open(PRESETS_PATH, "w") as f:
        json.dump(presets, f, indent=2, sort_keys=True)
    print(f"Saved preset {name!r}: effect={status['current_effect']!r} params={status['params']}")


def preset_load(name: str):
    presets = _load_presets()
    if name not in presets:
        print(f"No preset named {name!r}. Known presets: {sorted(presets.keys())}")
        return
    preset = presets[name]
    r = requests.post(f"{BASE}/effect", json={"name": preset["effect"], "params": preset["params"]})
    print(r.json())


def preset_list():
    presets = _load_presets()
    if not presets:
        print("No presets saved yet.")
        return
    for name, preset in sorted(presets.items()):
        print(f"  {name}: effect={preset['effect']!r} params={preset['params']}")


def _load_config() -> dict:
    try:
        with open(CONFIG_PATH) as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def set_default(name: str):
    presets = _load_presets()
    if name not in presets:
        print(f"No preset named {name!r}. Known presets: {sorted(presets.keys())}")
        return
    config = _load_config()
    config["default_preset"] = name
    with open(CONFIG_PATH, "w") as f:
        json.dump(config, f, indent=2, sort_keys=True)
    print(f"Startup default set to {name!r}. Takes effect on the next daemon restart.")


def show_default():
    default = _load_config().get("default_preset")
    if default:
        print(f"Startup default: {default!r}")
    else:
        print("No startup default set (daemon starts off/black).")


def discover(output_path: str, start: int, indices: list[int] | None = None):
    """Light one cell at a time; the user types which physical key lit
    up. Existing entries in `output_path` are preserved and can be
    fast-forwarded through by pressing Enter. If `indices` is given,
    only that specific list of cells is visited (for re-checking gaps)
    instead of the full start..num_cells range."""
    try:
        with open(output_path) as f:
            keymap = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        keymap = {}

    num_cells = requests.get(f"{BASE}/status").json()["num_cells"]
    cell_sequence = indices if indices is not None else list(range(start, num_cells))

    print(f"Discovering keymap -- {len(cell_sequence)} cell(s) to visit.")
    print("For each lit cell, type the key name and press Enter.")
    print("  [Enter]      keep existing mapping (or skip if unmapped)")
    print("  :none        mark this cell as having no key")
    print("  :back        go back one cell in this pass")
    print("  :quit        save progress and quit")
    print("  (commands must start with ':' so a real key -- even Q, B, or -- is")
    print("   never mistaken for one; Ctrl+C also saves and quits safely)")
    print()

    pos = 0
    try:
        while pos < len(cell_sequence):
            i = cell_sequence[pos]
            idx = str(i)
            existing = keymap.get(idx)
            requests.post(
                f"{BASE}/effect",
                json={"name": "probe", "params": {"index": i, "color": PROBE_COLOR}},
            )
            prompt = f"[cell {i:3d}] ({pos + 1}/{len(cell_sequence)})"
            if existing:
                prompt += f" (currently: {existing})"
            try:
                answer = input(prompt + " key> ").strip()
            except KeyboardInterrupt:
                print()
                break

            if answer in (":quit", ":q"):
                break
            elif answer in (":back", ":b"):
                pos = max(0, pos - 1)
                continue
            elif answer in (":none", ":-"):
                keymap.pop(idx, None)
            elif answer:
                keymap[idx] = answer
            # blank with no existing entry: leave unmapped, move on

            with open(output_path, "w") as f:
                json.dump(keymap, f, indent=2, sort_keys=True)
            pos += 1
    finally:
        requests.post(f"{BASE}/off")

    print(f"\nSaved {len(keymap)} mapped cells to {output_path}")


if __name__ == "__main__":
    main()
