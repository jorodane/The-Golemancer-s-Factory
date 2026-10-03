#!/usr/bin/env python3
"""Verify identity isolation, incident authority, checkpoints, resolution and optional real TLS transport."""
import argparse
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--network', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
project = root / 'tests/PackEngine.Studio.Verification/PackEngine.Studio.Verification.csproj'
subprocess.run([args.dotnet, 'build', str(project), '-c', 'Release', '-p:EngineTargetFramework=net10.0',
                '-p:UseSharedCompilation=false', '-m:1', '--nologo', '-v:quiet'], cwd=root, check=True)
command = [args.dotnet, str(project.parent / 'bin/Release/net10.0/PackEngine.Studio.Verification.dll')]
command.extend(['--dotnet', args.dotnet])
if args.network:
    command.append('--network')
subprocess.run(command, cwd=root, check=True)
