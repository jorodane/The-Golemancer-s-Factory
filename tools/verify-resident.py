#!/usr/bin/env python3
"""Verify semantic input, editor tools and Codex transport on a copy of the real game; no model is simulated in the product."""
import argparse
import json
import os
import queue
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--codex', help='Optional real native CLI: verify initialize/account and ephemeral thread configuration; no model turn or login')
    parser.add_argument('--skip-build', action='store_true')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError('Provide --dotnet /path/to/dotnet')
    output = ROOT / 'TestResults/resident'
    output.mkdir(parents=True, exist_ok=True)
    for project in ([] if args.skip_build else ['editor/PackEngine.Tool/PackEngine.Tool.csproj', 'editor/PackEngine.Assistant.Codex/PackEngine.Assistant.Codex.csproj',
                    'tests/PackEngine.Workspace.Verification/PackEngine.Workspace.Verification.csproj']):
        subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '-v:q'], cwd=ROOT, check=True)
    tool = ROOT / 'editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll'
    provider = ROOT / 'editor/PackEngine.Assistant.Codex/bin/Release/net10.0/PackEngine.Assistant.Codex.dll'
    harness = ROOT / 'tests/PackEngine.Workspace.Verification/bin/Release/net10.0/PackEngine.Workspace.Verification.dll'
    fixture = ROOT / 'tools/fixtures/codex-peer.py'
    report = {'passed': False, 'checks': [], 'windowsGuiTested': False, 'authenticatedModelTurnTested': False}

    def check(ok, message):
        if not ok:
            raise RuntimeError(message)
        report['checks'].append(message)
        print('PASS: ' + message, flush=True)

    try:
        with tempfile.TemporaryDirectory(prefix='packengine-resident-') as temp:
            folder = Path(temp)
            game = folder / 'Loaded Game'
            shutil.copytree(ROOT / 'Golemancer', game, ignore=lambda _, names: set(names) & {'bin', 'obj', 'Saves', 'SmokeSaves', 'TestResults', '.git', 'Artifacts'})
            project = game / 'Golemancer.packproject'
            ui = game / 'Content/Packs/02.Controls/ui.xml'
            original = ui.read_bytes()
            workspace = subprocess.run([dotnet, str(harness), str(project), str(folder / 'workspace-state'), dotnet], capture_output=True, text=True, timeout=60)
            (output / 'workspace.log').write_text(workspace.stdout + workspace.stderr)
            check(workspace.returncode == 0 and 'RESIDENT_WORKSPACE_PASS' in workspace.stdout, 'workspace snapshots, scopes, conflict detection, real XML apply/build and undo')
            report['workspaceChecks'] = sum(line.startswith('PASS:') for line in workspace.stdout.splitlines())
            state = folder / 'resident-state'
            count = 0

            def command(mode, *options):
                nonlocal count
                count += 1
                log = output / f'{count:02d}-{mode}.jsonl'
                log.unlink(missing_ok=True)
                log.with_suffix('.ready').unlink(missing_ok=True)
                env = dict(os.environ, PACKENGINE_TEST_MODE=mode, PACKENGINE_TEST_LOG=str(log))
                cmd = [dotnet, str(tool), 'codex-chat', '--project', str(project), '--state', str(state), '--dotnet', dotnet,
                       '--provider', str(provider), '--codex', str(fixture), '--prompt', '전송 경계 검사', *map(str, options)]
                return cmd, env, log

            def call(mode, *options, fail=False):
                cmd, env, log = command(mode, *options)
                result = subprocess.run(cmd, env=env, capture_output=True, text=True, timeout=25)
                (output / f'{count:02d}-{mode}.log').write_text(result.stdout + result.stderr)
                check(bool(result.returncode) == fail, mode + ' returns the expected success/failure status')
                return result, [json.loads(line) for line in log.read_text().splitlines()]

            response, messages = call('edit', '--point', 'widget:golemancer.costButton', '--write-pack', 'golemancer.controls')
            check(response.stdout.strip() == 'FIXTURE_PROTOCOL_OK' and 'STALE' not in response.stderr and b'value="15"' in ui.read_bytes(),
                  'resident transport streams the correct turn and performs only authorized real XML operations')
            start_options = next(m['params'] for m in messages if m.get('method') == 'thread/start')
            operations = json.loads((state / 'session.json').read_text())['Operations']
            check(any(o['Tool'] == 'packengine_build' and o['Status'] == 'completed' for o in operations) and any(o['Status'] == 'failed' for o in operations),
                  'transport retains completed and denied operations in the editor history')
            ui.write_bytes(original)
            _, resumed = call('resume')
            check(any(m.get('method') == 'thread/resume' for m in resumed) and not any(m.get('method') == 'thread/start' for m in resumed),
                  'restarting the client resumes the persisted conversation for this project')
            _, reset = call('new', '--new-thread')
            check(any(m.get('method') == 'thread/start' for m in reset), 'explicit new conversation starts a new Codex thread')
            _, api_key = call('api-key', fail=True)
            check(not any(m.get('method') in ('turn/start', 'thread/start', 'thread/resume') for m in api_key), 'API-key authentication never silently replaces ChatGPT login')
            call('failure', fail=True)
            call('disconnect', fail=True)
            cmd, env, log = command('cancel')
            process = subprocess.Popen(cmd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                deadline = time.monotonic() + 10
                while not log.with_suffix('.ready').exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(.05)
                if not log.with_suffix('.ready').exists():
                    raise RuntimeError('Cancellation fixture never started')
                process.send_signal(signal.SIGINT)
                stdout, stderr = process.communicate(timeout=15)
                (output / 'cancel.log').write_text(stdout + stderr)
                messages = [json.loads(line) for line in log.read_text().splitlines()]
                check(process.returncode != 0 and any(m.get('method') == 'turn/interrupt' for m in messages), 'cancellation interrupts the active turn and terminates its transport')
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()
            call('resume')
            def history(command_name, *options, fail=False):
                cmd, env, log = command('history')
                cmd[2] = command_name
                cmd.extend(map(str, options))
                result = subprocess.run(cmd, env=env, capture_output=True, text=True, timeout=25)
                messages = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
                check(bool(result.returncode) == fail, command_name + ' ' + ' '.join(options) + ' has the expected access result')
                if command_name != 'codex-chat':
                    check(not any(m.get('method') in ('turn/start', 'thread/resume', 'thread/start') for m in messages), 'browsing stored conversations never starts inference or resumes a thread')
                return result, messages
            listed, _ = history('codex-threads')
            page = json.loads(listed.stdout)
            check(len(page['Threads']) == 1 and page['Threads'][0]['Title'] == 'Saved question' and page['Cursor'] == 'older-threads', 'thread listing excludes other projects/clients and hides internal context from titles')
            listed, _ = history('codex-threads', '--cursor', page['Cursor'], '--deny-thread', 'fixture-older')
            check(json.loads(listed.stdout)['Threads'][0]['Allowed'] is False, 'pagination retains per-conversation access choices')
            history_result, _ = history('codex-history', '--thread', 'fixture-thread')
            messages = json.loads(history_result.stdout)['Messages']
            check([m['Text'] for m in messages] == ['Older question', 'OLDER_FIXTURE', 'Recent question', 'RECENT_FIXTURE'], 'history renders chronological user/assistant messages without editor envelopes or reasoning')
            older, _ = history('codex-history', '--thread', 'fixture-thread', '--cursor', 'older-messages')
            check(json.loads(older.stdout)['Cursor'] == '', 'history paging terminates at the final cursor')
            for denied in ['foreign', 'other-client', 'active']:
                _, records = history('codex-history', '--thread', denied, fail=True)
                check(not any(m.get('method') == 'thread/turns/list' for m in records), 'foreign or active conversations are rejected before reading message contents')
            _, records = history('codex-history', '--thread', 'fixture-thread', '--deny-thread', 'fixture-thread', fail=True)
            check(not any(m.get('method') == 'thread/read' for m in records), 'blocked conversation access is enforced before RPC')
            _, records = history('codex-threads', '--no-history', fail=True)
            check(not any(m.get('method') == 'thread/list' for m in records), 'history-disabled settings prevent listing')
            _, records = history('codex-status', '--deny-access', fail=True)
            check(not records, 'connection-disabled settings do not start the Codex process')
            _, records = history('codex-chat', '--no-history')
            check(any(m.get('method') == 'thread/start' for m in records) and not any(m.get('method') in ('thread/read', 'thread/resume') for m in records), 'history-disabled chat starts fresh without reading or resuming old context')
            _, records = history('codex-chat', '--thread', 'fixture-older')
            check(any(m.get('method') == 'thread/resume' and m['params']['threadId'] == 'fixture-older' for m in records)
                  and json.loads((state / 'codex-thread.json').read_text())['ThreadId'] == 'fixture-older', 'selecting an older chat resumes that exact thread and persists the selection')
            if args.codex:
                result = subprocess.run([dotnet, str(tool), 'codex-status', '--project', str(project), '--state', str(folder / 'real-state'),
                                         '--provider', str(provider), '--codex', args.codex], capture_output=True, text=True, timeout=60)
                # Retain no authentication payload: only whether the official handshake succeeded.
                check(result.returncode == 0 and 'Display' in json.loads(result.stdout), 'official native Codex initialize/account handshake succeeds without a model call')
                report['officialCliVersion'] = subprocess.check_output([args.codex, '--version'], text=True).strip()
                # Exercise the exact tool/config payload against the official server without invoking a model.
                cwd = folder / 'real-state/codex-workspace'
                (folder / 'isolated-codex-history').mkdir()
                peer = subprocess.Popen([args.codex, 'app-server'], cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.DEVNULL, text=True, env=dict(os.environ, CODEX_HOME=str(folder / 'isolated-codex-history')))
                incoming = queue.Queue()
                def collect():
                    for line in peer.stdout:
                        incoming.put(json.loads(line))
                    incoming.put(None)
                threading.Thread(target=collect, daemon=True).start()
                number = 0
                def rpc(method, params):
                    nonlocal number
                    number += 1
                    peer.stdin.write(json.dumps({'id': number, 'method': method, 'params': params}) + '\n')
                    peer.stdin.flush()
                    while True:
                        value = incoming.get(timeout=30)
                        if value is None:
                            raise RuntimeError('Official Codex exited before replying to ' + method)
                        if value.get('id') == number:
                            if 'error' in value:
                                raise RuntimeError('Official CLI rejected ' + method + ': ' + str(value['error'].get('message')))
                            return value['result']
                try:
                    rpc('initialize', {'clientInfo': {'name': 'packengine_editor', 'version': '0.3.0'}, 'capabilities': {'experimentalApi': True}})
                    peer.stdin.write(json.dumps({'method': 'initialized', 'params': {}}) + '\n'); peer.stdin.flush()
                    config = rpc('config/read', {'includeLayers': False, 'cwd': str(cwd)})
                    start_options['cwd'] = str(cwd); start_options['ephemeral'] = True
                    start_options['config']['mcp_servers'] = {name: {'enabled': False} for name in config.get('config', {}).get('mcp_servers', {})}
                    accepted = rpc('thread/start', start_options)
                    check(bool(accepted.get('thread', {}).get('id')) and accepted['sandbox']['type'] == 'readOnly',
                          'official Codex accepts the exact semantic dynamic tools and restricted thread configuration without inference')
                    report['officialThreadStartAccepted'] = True
                    start_options['ephemeral'] = False
                    stored = rpc('thread/start', start_options)['thread']
                    # Materialize an isolated test history without asking a model to generate anything.
                    rpc('thread/inject_items', {'threadId': stored['id'], 'items': [{'type': 'message', 'role': 'user',
                        'content': [{'type': 'input_text', 'text': 'EXPLICIT PROTOCOL FIXTURE; NO MODEL RESPONSE'}]}]})
                    metadata = rpc('thread/read', {'threadId': stored['id'], 'includeTurns': False})['thread']
                    listed = rpc('thread/list', {'cwd': str(cwd), 'sourceKinds': ['appServer', 'cli', 'vscode', 'unknown'], 'modelProviders': ['openai'],
                        'sortKey': 'updated_at', 'sortDirection': 'desc', 'limit': 30, 'archived': False})
                    turns = rpc('thread/turns/list', {'threadId': stored['id'], 'limit': 10, 'sortDirection': 'desc', 'itemsView': 'full'})
                    check(metadata['cwd'] == str(cwd) and metadata['originator'] == 'packengine_editor' and isinstance(listed['data'], list)
                          and isinstance(turns['data'], list), 'official Codex accepts project-scoped thread listing, ownership metadata and paged history without inference')
                    report['officialHistoryMethodsAccepted'] = True
                finally:
                    peer.kill(); peer.wait(timeout=10)
            check(ui.read_bytes() == original, 'all verification edits remain in a temporary game copy')
            report['passed'] = True
    finally:
        (output / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('RESIDENT_VERIFICATION_PASS', flush=True)


if __name__ == '__main__':
    main()
