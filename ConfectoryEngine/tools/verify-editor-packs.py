#!/usr/bin/env python3
"""Exercise real external DLL generations, UI inheritance and scoped authoring. No model or WPF GUI is simulated."""
import argparse
import os
from pathlib import Path
import subprocess
import re

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--project', required=True, help='Consumer manifest for explicitly selected integration cases')
args = parser.parse_args()
os.environ["CONFECTORY_TEST_PROJECT"] = str(Path(args.project).resolve())
for project in ['editor/Confectory.PackHost/Confectory.PackHost.csproj', 'editor/Packs/CoreTools/Confectory.Editor.CoreTools.csproj', 'editor/examples/MobileLab/Confectory.Editor.MobileLab.csproj', 'tests/Confectory.EditorPacks.Verification/Confectory.EditorPacks.Verification.csproj']:
    subprocess.run([args.dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:quiet'], cwd=root, check=True)
result = subprocess.run([args.dotnet, str(root / 'tests/Confectory.EditorPacks.Verification/bin/Release/net10.0/Confectory.EditorPacks.Verification.dll'), str(root), args.dotnet], cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
print(result.stdout, flush=True)
result.check_returncode()
completed = re.search(r'^EDITOR_PACK_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
modules = re.search(r'^MODULE_WINDOW_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
data = re.search(r'^PROJECT_DATA_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
mobile = re.search(r'^APP_MODULE_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
live = re.search(r'^LIVE_VIEW_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
execution = re.search(r'^PROJECT_EXECUTION_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
elements = re.search(r'^ELEMENT_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
if not execution or int(execution.group(1)) < 28 or not completed or int(completed.group(1)) < 170 or not modules or int(modules.group(1)) < 30 or not data or int(data.group(1)) < 30 or not mobile or int(mobile.group(1)) < 20 or not live or int(live.group(1)) < 25 or not elements or int(elements.group(1)) < 40:
    raise RuntimeError('Editor pack verification did not reach its completion marker.')
