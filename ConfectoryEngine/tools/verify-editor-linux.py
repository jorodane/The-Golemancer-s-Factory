#!/usr/bin/env python3
"""Exercise the actual SDL window, editor controller and external editor DLL."""
import argparse
import os
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--directory')
parser.add_argument('--screenshot')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
app = Path(args.directory).resolve() if args.directory else root / 'editor/Builds/Linux/linux-x64'
env = os.environ.copy()
env['CONFECTORY_DOTNET'] = args.dotnet
if not args.directory:
    subprocess.run([str(root / 'BuildEditorLinux.sh'), 'linux-x64'], env=env, check=True)
if not env.get('DISPLAY') and not env.get('WAYLAND_DISPLAY'):
    env.setdefault('SDL_VIDEODRIVER', 'offscreen')
command = [str(app / 'start.sh'), '--smoke']
if args.screenshot:
    screenshot = Path(args.screenshot).resolve()
    screenshot.parent.mkdir(parents=True, exist_ok=True)
    command += ['--screenshot', str(screenshot)]
result = subprocess.run(command, env=env, text=True, capture_output=True, timeout=90)
print(result.stdout, end='')
print(result.stderr, end='')
if result.returncode or 'LINUX_EDITOR_SMOKE_PASS' not in result.stdout:
    raise SystemExit(result.returncode or 1)
