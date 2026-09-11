#!/usr/bin/env python3
"""Park/restore the two standard development bundles; never delete user data."""

import argparse
import os
from pathlib import Path
import subprocess
import sys


BUNDLES = ("RhinoLayoutFoundry.rhp", "RhinoLayoutFoundry.AI.rhp")


def require_rhino_closed():
    result = subprocess.run(["/bin/ps", "-axo", "comm="],
                            capture_output=True, text=True, check=True)
    if any("/Contents/MacOS/Rhinoceros" in line or
           Path(line.strip()).name in ("Rhinoceros", "Rhino")
           for line in result.stdout.splitlines()):
        raise RuntimeError("Fully quit all Rhino instances before moving bundles.")


def transfer(action, plugins, backup, apply=False):
    plugins, backup = plugins.expanduser().resolve(), backup.expanduser().resolve()
    if plugins == backup or plugins in backup.parents or backup in plugins.parents:
        raise RuntimeError("Plugin and backup directories must be separate, non-nested paths.")
    source, destination = (plugins, backup) if action == "park" else (backup, plugins)
    moves = []
    for name in BUNDLES:
        src, dst = source / name, destination / name
        if src.is_symlink() or dst.is_symlink():
            raise RuntimeError(f"Refusing symlink bundle: {name}; inspect its load path manually.")
        if not src.exists():
            continue
        if not src.is_dir():
            raise RuntimeError(f"Expected a development bundle directory: {src}")
        if dst.exists():
            raise RuntimeError(f"Refusing to overwrite existing bundle: {dst}")
        moves.append((src, dst))
    for src, dst in moves:
        print(f"{src} -> {dst}")
    if not moves:
        print("No bundles to move.")
        return
    if not apply:
        print("Preview only. Add --apply to move these bundles with Rhino closed.")
        return
    if sys.platform != "darwin":
        raise RuntimeError("This helper supports macOS only.")
    require_rhino_closed()
    destination.mkdir(parents=True, exist_ok=True)
    # Same-volume rename avoids a partially copied installation or backup.
    if any(src.stat().st_dev != destination.stat().st_dev for src, _ in moves):
        raise RuntimeError("Use a backup directory on the same volume as the bundles.")
    completed = []
    try:
        for src, dst in moves:
            os.rename(src, dst)
            completed.append((src, dst))
    except OSError:
        for src, dst in reversed(completed):
            os.rename(dst, src)
        raise
    print("Bundles moved. Fully restart Rhino for the next check.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("status", "park", "restore"))
    parser.add_argument("--apply", action="store_true", help="Execute; otherwise only preview")
    parser.add_argument("--plugins-dir", type=Path, default=Path.home() /
                        "Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns")
    parser.add_argument("--backup-dir", type=Path, default=Path.home() /
                        "Library/Application Support/LayoutFoundry/DevInstallBackup")
    args = parser.parse_args()
    if args.action == "status":
        for root in (args.plugins_dir, args.backup_dir):
            for name in BUNDLES:
                path = root.expanduser() / name
                print(f"{'present' if path.exists() or path.is_symlink() else 'absent'}: {path}")
        print("Only the two named development bundles are checked; inspect PackageManager, "
              "PlugInManager and custom load paths separately.")
    else:
        transfer(args.action, args.plugins_dir, args.backup_dir, args.apply)


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError) as error:
        print(f"Error: {error}", file=sys.stderr)
        sys.exit(1)
