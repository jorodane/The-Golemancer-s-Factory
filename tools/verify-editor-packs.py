#!/usr/bin/env python3
"""Exercise real external DLL generations, UI inheritance and scoped authoring. No model or WPF GUI is simulated."""
import argparse
from pathlib import Path
import subprocess
import re

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
for project in ['editor/PackEngine.PackHost/PackEngine.PackHost.csproj', 'editor/Packs/CoreTools/PackEngine.Editor.CoreTools.csproj', 'editor/examples/MobileLab/PackEngine.Editor.MobileLab.csproj', 'tests/PackEngine.EditorPacks.Verification/PackEngine.EditorPacks.Verification.csproj']:
    subprocess.run([args.dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:quiet'], cwd=root, check=True)
result = subprocess.run([args.dotnet, str(root / 'tests/PackEngine.EditorPacks.Verification/bin/Release/net10.0/PackEngine.EditorPacks.Verification.dll'), str(root), args.dotnet], cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
print(result.stdout, flush=True)
result.check_returncode()
completed = re.search(r'^EDITOR_PACK_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
modules = re.search(r'^MODULE_WINDOW_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
data = re.search(r'^PROJECT_DATA_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
mobile = re.search(r'^APP_MODULE_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
live = re.search(r'^LIVE_VIEW_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
elements = re.search(r'^ELEMENT_CHECKS=(\d+)$', result.stdout, re.MULTILINE)
if not completed or int(completed.group(1)) < 170 or not modules or int(modules.group(1)) < 30 or not data or int(data.group(1)) < 30 or not mobile or int(mobile.group(1)) < 20 or not live or int(live.group(1)) < 25 or not elements or int(elements.group(1)) < 40:
    raise RuntimeError('Editor pack verification did not reach its completion marker.')
