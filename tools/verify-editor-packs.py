#!/usr/bin/env python3
"""Exercise real external DLL generations, UI inheritance and scoped authoring. No model or WPF GUI is simulated."""
import argparse
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
for project in ['editor/PackEngine.PackHost/PackEngine.PackHost.csproj', 'editor/Packs/CoreTools/PackEngine.Editor.CoreTools.csproj', 'tests/PackEngine.EditorPacks.Verification/PackEngine.EditorPacks.Verification.csproj']:
    subprocess.run([args.dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:quiet'], cwd=root, check=True)
result = subprocess.run([args.dotnet, str(root / 'tests/PackEngine.EditorPacks.Verification/bin/Release/net10.0/PackEngine.EditorPacks.Verification.dll'), str(root), args.dotnet], cwd=root, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
print(result.stdout, flush=True)
result.check_returncode()
if 'EDITOR_PACK_CHECKS=46' not in result.stdout:
    raise RuntimeError('Editor pack verification did not reach its completion marker.')
