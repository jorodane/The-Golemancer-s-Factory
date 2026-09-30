#!/usr/bin/env python3
"""Verify the shipped STDIO executable, protocol, live session and IPC when the host permits named pipes."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError('Provide --dotnet /path/to/dotnet')
    output = ROOT / 'TestResults/mcp'
    output.mkdir(parents=True, exist_ok=True)
    for project in ['editor/PackEngine.Mcp/PackEngine.Mcp.csproj', 'tests/PackEngine.Mcp.Verification/PackEngine.Mcp.Verification.csproj']:
        subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '-v:q'], cwd=ROOT, check=True)
    executable = ROOT / 'editor/PackEngine.Mcp/bin/Release/net10.0/PackEngine.Mcp.dll'
    messages = [dict(jsonrpc='2.0', id=1, method='initialize', params=dict(protocolVersion='2025-11-25', capabilities={}, clientInfo=dict(name='verification', version='1'))),
                dict(jsonrpc='2.0', method='notifications/initialized'), dict(jsonrpc='2.0', id=2, method='tools/list'),
                dict(jsonrpc='2.0', id=3, method='ping'), dict(jsonrpc='2.0', id=4, method='unsupported')]
    wire = subprocess.run([dotnet, str(executable), '--project', str(ROOT / 'Golemancer/Golemancer.packproject')],
                          input=''.join(json.dumps(message) + '\n' for message in messages), capture_output=True, text=True, timeout=20)
    responses = {r['id']: r for r in map(json.loads, wire.stdout.splitlines())}
    assert wire.returncode == 0 and not wire.stderr, wire.stderr
    assert responses[1]['result']['protocolVersion'] == '2025-11-25'
    assert len(responses[2]['result']['tools']) == 10 and responses[3]['result'] == {} and responses[4]['error']['code'] == -32601
    (output / 'stdio.jsonl').write_text(wire.stdout, encoding='utf-8')
    with tempfile.TemporaryDirectory(prefix='packengine-mcp-') as temporary:
        folder = Path(temporary)
        game = folder / '게임 with spaces'
        shutil.copytree(ROOT / 'Golemancer', game, ignore=lambda _, names: set(names) & {'bin', 'obj', 'Saves', 'SmokeSaves', 'TestResults', '.git', 'Artifacts', 'Builds'})
        result = subprocess.run([dotnet, str(ROOT / 'tests/PackEngine.Mcp.Verification/bin/Release/net10.0/PackEngine.Mcp.Verification.dll'),
                                 str(game / 'Golemancer.packproject'), str(folder / 'state'), dotnet,
                                 str(executable)], capture_output=True, text=True, timeout=120)
        (output / 'verification.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        print(result.stdout, end='')
        if result.returncode:
            print(result.stderr)
        passed = result.returncode == 0 and 'MCP_VERIFICATION_PASS' in result.stdout
        (output / 'report.json').write_text(json.dumps({'passed': passed, 'checks': [line[6:] for line in result.stdout.splitlines() if line.startswith('PASS: ')],
            'executableProtocolChecks': 4,
            'namedPipeTested': 'SKIP_NAMED_PIPE:' not in result.stdout, 'windowsGuiTested': False, 'chatGptPluginConnected': False, 'secureTunnelTested': False, 'authenticatedModelTurnTested': False}, ensure_ascii=False, indent=2), encoding='utf-8')
        if not passed:
            raise RuntimeError('MCP verification failed; see TestResults/mcp/verification.log')


if __name__ == '__main__':
    main()
