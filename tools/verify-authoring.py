#!/usr/bin/env python3
"""Verify reviewed authoring, real pack compilation, catalog, placement and image protocol fixtures."""
import argparse
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')
project = 'tests/PackEngine.Authoring.Verification/PackEngine.Authoring.Verification.csproj'
subprocess.run([args.dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0',
                '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:q'],
               cwd=root, env=environment, check=True)
subprocess.run([args.dotnet, str(root / 'tests/PackEngine.Authoring.Verification/bin/Release/net10.0/PackEngine.Authoring.Verification.dll'),
                str(root), args.dotnet], cwd=root, env=environment, check=True)
