#!/usr/bin/env python3
"""Verify Confectory authoring, worker RPC, window preferences and image HTTP fixtures; no paid inference."""
import argparse
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError('Provide --dotnet /path/to/dotnet')
    for project in ('editor/PackEngine.PackHost/PackEngine.PackHost.csproj',
                    'tests/PackEngine.Authoring.Verification/PackEngine.Authoring.Verification.csproj'):
        subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0',
                        '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:q'],
                       cwd=ROOT, check=True)
    subprocess.run([dotnet, str(ROOT / 'tests/PackEngine.Authoring.Verification/bin/Release/net10.0/PackEngine.Authoring.Verification.dll'),
                    str(ROOT), dotnet], cwd=ROOT, check=True)


if __name__ == '__main__':
    main()
