#!/usr/bin/env python3
"""Verify web runtime lifecycle and desktop MCP registration using disposable fixtures/settings."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--codex', help='Official native CLI; no account or inference is used')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError('Provide --dotnet /path/to/dotnet')
    project = 'tests/PackEngine.ChatGptSetup.Verification/PackEngine.ChatGptSetup.Verification.csproj'
    subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0',
                    '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '--nologo', '-v:q'], cwd=ROOT, check=True)
    output = ROOT / 'TestResults/chatgpt-setup'
    output.mkdir(parents=True, exist_ok=True)
    assembly = ROOT / 'tests/PackEngine.ChatGptSetup.Verification/bin/Release/net10.0/PackEngine.ChatGptSetup.Verification.dll'
    run = subprocess.run([dotnet, str(assembly), *([str(Path(args.codex).resolve())] if args.codex else [])], cwd=ROOT,
                         capture_output=True, text=True, timeout=120)
    (output / 'verification.log').write_text(run.stdout + run.stderr)
    print(run.stdout, end='')
    if run.returncode:
        print(run.stderr, end='')
    report = dict(passed=run.returncode == 0, checks=sum(line.startswith('PASS:') for line in run.stdout.splitlines()),
                  officialCliRegistrationTested=bool(args.codex), windowsGuiTested=False, windowsDownloadTested=False,
                  authenticatedChatGptTested=False, authenticatedTunnelTested=False,
                  webRuntimeFixtureTested='real fixture process and loopback health' in run.stdout,
                  windowsJobAndDpapiTested=False)
    (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
    run.check_returncode()


if __name__ == '__main__':
    main()
