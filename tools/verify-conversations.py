#!/usr/bin/env python3
"""Verify local conversation storage and explicit portable snapshots, without model inference."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import queue
import shutil
import signal
import subprocess
import tempfile
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--dotnet', default='dotnet')
    ap.add_argument('--codex')
    ap.add_argument('--skip-build', action='store_true')
    args = ap.parse_args()
    dotnet = shutil.which(args.dotnet)
    assert dotnet, 'Provide --dotnet'
    out = ROOT / 'TestResults/conversations'
    out.mkdir(parents=True, exist_ok=True)
    projects = ['editor/PackEngine.Tool/PackEngine.Tool.csproj', 'editor/PackEngine.Assistant.Codex/PackEngine.Assistant.Codex.csproj',
                'tests/PackEngine.Conversation.Verification/PackEngine.Conversation.Verification.csproj',
                'tests/PackEngine.Launcher.Verification/PackEngine.Launcher.Verification.csproj']
    for project in ([] if args.skip_build else projects):
        subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false',
                        '-m:1', '--disable-build-servers', '--nologo', '-v:q'], cwd=ROOT, check=True)
    tool = ROOT / 'editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll'
    provider = ROOT / 'editor/PackEngine.Assistant.Codex/bin/Release/net10.0/PackEngine.Assistant.Codex.dll'
    fixture = ROOT / 'tools/fixtures/portable-codex-peer.py'
    report = {'passed': False, 'checks': [], 'windowsGuiTested': False, 'authenticatedModelTurnTested': False, 'cloudSyncTested': False}

    def check(ok, title):
        if not ok:
            raise RuntimeError(title)
        report['checks'].append(title)
        print('PASS: ' + title, flush=True)

    def game(folder):
        (folder / 'Packs/Base').mkdir(parents=True)
        (folder / 'Packs/Base/pack.xml').write_text('<ObjectPack id="base" version="1.0.0" contracts="2"/>')
        file = folder / 'Game.packproject'
        file.write_text('<EngineProject version="1" id="portable-test" name="Fixture Game" packs="Packs" defaultTarget="windows"><Target id="windows" platform="windows" framework="net48"/></EngineProject>')
        return file

    def archive(project):
        return project.parent / '.packengine' / project.name / 'conversations'

    def files(directory):
        return {str(p.relative_to(directory)): p.read_bytes() for p in directory.rglob('*') if p.is_file()}

    try:
        with tempfile.TemporaryDirectory(prefix='packengine-conversations-') as tmp:
            folder = Path(tmp)
            for name, cmd in [('storage', [str(folder / 'storage')]), ('launcher', [])]:
                path = ROOT / ('tests/PackEngine.Conversation.Verification/bin/Release/net10.0/PackEngine.Conversation.Verification.dll' if name == 'storage'
                               else 'tests/PackEngine.Launcher.Verification/bin/Release/net10.0/PackEngine.Launcher.Verification.dll')
                run = subprocess.run([dotnet, str(path), *cmd], capture_output=True, text=True, timeout=60)
                (out / (name + '.log')).write_text(run.stdout + run.stderr)
                check(run.returncode == 0, name + ' storage/launcher regression checks pass')
                report[name + 'Checks'] = sum(x.startswith('PASS:') for x in run.stdout.splitlines())
            project = game(folder / 'device-a/Game')
            home = folder / 'codex-a'; home.mkdir()
            (home / 'auth.json').write_text('SYNTHETIC_AUTH_SENTINEL_NOT_A_CREDENTIAL')
            state = folder / 'state-a'
            number = 0

            def command(command, project, state, home, *extra, codex=fixture, mode='normal'):
                nonlocal number
                number += 1
                log = out / f'{number:02d}-{command}.jsonl'; log.unlink(missing_ok=True)
                log.with_suffix('.ready').unlink(missing_ok=True)
                env = dict(os.environ, CODEX_HOME=str(home), PACKENGINE_TEST_LOG=str(log), PACKENGINE_TEST_MODE=mode)
                return [dotnet, str(tool), command, '--project', str(project), '--state', str(state), '--provider', str(provider),
                        '--codex', str(codex), '--project-conversations', *extra], env, log

            def call(command_name, project, state, home, *extra, fail=False, codex=fixture, mode='normal'):
                cmd, env, log = command(command_name, project, state, home, *extra, codex=codex, mode=mode)
                run = subprocess.run(cmd, env=env, capture_output=True, text=True, timeout=45)
                (out / f'{number:02d}-{command_name}.log').write_text(run.stdout + run.stderr)
                check(bool(run.returncode) == fail, command_name + ' has the expected completion status')
                records = [json.loads(x) for x in log.read_text().splitlines()] if log.exists() else []
                return run, records

            failing_state = folder / 'archive-failure-state'
            successful, traffic = call('codex-chat', project, failing_state, home, '--prompt', 'ARCHIVE FAILURE FIXTURE', mode='archive-fail')
            check(successful.stdout.strip() == 'PORTABILITY_FIXTURE_OK' and 'archive-failed' in successful.stderr,
                  'a cache failure warns without converting a successful model fixture response into a failed task')
            failed_id = json.loads((failing_state / 'codex-thread.json').read_text())['ThreadId']
            history, _ = call('codex-history', project, failing_state, home, '--thread', failed_id, mode='archive-fail')
            check('ARCHIVE FAILURE FIXTURE' in history.stdout, 'native history remains readable when its recovery cache cannot be saved')
            original_project = files(project.parent)
            first, _ = call('codex-chat', project, state, home, '--prompt', 'FIRST DEVICE FIXTURE')
            check(first.stdout.strip() == 'PORTABILITY_FIXTURE_OK', 'fixture response is clearly labeled as a protocol fixture')
            local = state / 'conversations'
            id = (local / 'active.txt').read_text().strip()
            descriptor = json.loads((local / (id + '.json')).read_text())
            data = (local / (descriptor['Hash'] + '.jsonl')).read_bytes()
            check(b'FIRST DEVICE FIXTURE' in data and len(data) > 0, 'the device state contains the native transcript outside the project')
            call('codex-threads', project, state, home)
            call('codex-history', project, state, home, '--thread', id)
            check(files(project.parent) == original_project and not archive(project).parent.exists(),
                  'chat, reconnect, listing and history browsing create no project files even with legacy project-conversations enabled')
            call('codex-save', project, state, home, '--thread', id, '--deny-thread', id, fail=True)
            check(files(project.parent) == original_project, 'blocked explicit saves cannot create project files')
            call('codex-save', project, state, home, '--thread', id)
            saved_meta = json.loads((archive(project) / (id + '.json')).read_text())
            check((archive(project) / (saved_meta['Hash'] + '.jsonl')).read_bytes() == data,
                  'explicit save writes the exact selected transcript and portable project identity')
            moved = folder / 'device-b/Renamed Game'
            shutil.copytree(project.parent, moved)
            shutil.rmtree(project.parent); shutil.rmtree(home); shutil.rmtree(state)
            project = moved / 'Game.packproject'; state = folder / 'state-b'; home = folder / 'codex-b'; home.mkdir()
            saved_project = files(project.parent); local = state / 'conversations'
            listed, traffic = call('codex-threads', project, state, home)
            check(json.loads(listed.stdout)['Threads'][0]['Id'] == id and not any(x.get('method') in ('thread/start', 'thread/resume', 'turn/start') for x in traffic),
                  'a moved game lists its conversations without the previous PC or starting inference')
            history, traffic = call('codex-history', project, state, home, '--thread', id)
            check([x['Text'] for x in json.loads(history.stdout)['Messages']] == ['FIRST DEVICE FIXTURE', 'PORTABILITY_FIXTURE_OK'],
                  'a clean second device restores full chat messages from the game folder')
            check(not any(x.get('method') in ('thread/start', 'thread/resume', 'turn/start') for x in traffic), 'history browsing does not resume a thread or start a model turn')
            _, traffic = call('codex-chat', project, state, home, '--prompt', 'SECOND DEVICE FIXTURE')
            check(any(x.get('method') == 'thread/resume' and x['params']['threadId'] == id for x in traffic) and not any(x.get('method') == 'thread/start' for x in traffic),
                  'second-device chat resumes the same native ID with restricted tools and current working directory')
            history, _ = call('codex-history', project, state, home, '--thread', id)
            check(len(json.loads(history.stdout)['Messages']) == 4, 'both devices messages survive round-trip persistence')
            check(files(project.parent) == saved_project, 'restoring and continuing a saved conversation never refreshes the project snapshot automatically')
            call('codex-save', project, state, home, '--thread', id)
            saved_meta = json.loads((archive(project) / (id + '.json')).read_text())
            check(b'SECOND DEVICE FIXTURE' in (archive(project) / (saved_meta['Hash'] + '.jsonl')).read_bytes(),
                  'saving again explicitly includes the newer local messages')
            saved_project = files(project.parent)
            _, traffic = call('codex-history', project, state, home, '--thread', id, '--deny-thread', id, fail=True)
            check(not any(x.get('method') == 'thread/read' for x in traffic), 'blocked history is denied before any native history read')
            _, traffic = call('codex-chat', project, state, home, '--prompt', 'NO HISTORY FIXTURE', '--no-history')
            check(not any(x.get('method') == 'thread/resume' for x in traffic), 'history-disabled mode starts a new conversation')
            _, _ = call('codex-chat', project, state, home, '--prompt', 'DISCONNECT FIXTURE', mode='disconnect', fail=True)
            active = (local / 'active.txt').read_text().strip()
            meta = json.loads((local / (active + '.json')).read_text())
            check(b'DISCONNECT FIXTURE' in (local / (meta['Hash'] + '.jsonl')).read_bytes(), 'a disconnected turn preserves complete native records on this device')
            cmd, env, log = command('codex-chat', project, state, home, '--prompt', 'CANCEL FIXTURE', mode='cancel')
            proc = subprocess.Popen(cmd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            try:
                end = time.monotonic() + 15
                while not log.with_suffix('.ready').exists() and proc.poll() is None and time.monotonic() < end:
                    time.sleep(.05)
                assert log.with_suffix('.ready').exists(), 'fixture did not start'
                proc.send_signal(signal.SIGINT)
                stdout, stderr = proc.communicate(timeout=20)
                (out / 'cancel.log').write_text(stdout + stderr)
                check(proc.returncode != 0 and 'turn/interrupt' in log.read_text(), 'cancellation interrupts the turn and retains a local snapshot')
            finally:
                if proc.poll() is None:
                    proc.kill(); proc.wait()
            meta = json.loads((local / (active + '.json')).read_text())
            check(b'CANCEL FIXTURE' in (local / (meta['Hash'] + '.jsonl')).read_bytes(), 'cancelled native records remain recoverable locally')
            check(files(project.parent) == saved_project, 'new selections, history-disabled turns, disconnects and cancellation leave saved project files unchanged')
            call('codex-save', project, state, home, '--thread', active)
            meta = json.loads((archive(project) / (active + '.json')).read_text())
            native = next((home / 'sessions').rglob('*-' + active + '.jsonl'))
            snapshot = archive(project) / (meta['Hash'] + '.jsonl')
            changed = snapshot.read_bytes() + b'{"type":"fixture-divergent"}\n'
            new_hash = hashlib.sha256(changed).hexdigest()
            (archive(project) / (new_hash + '.jsonl')).write_bytes(changed)
            meta['Hash'] = new_hash; (archive(project) / (active + '.json')).write_text(json.dumps(meta))
            with native.open('ab') as stream:
                stream.write(b'{"type":"fixture-local-branch"}\n')
            original_native = native.read_bytes()
            conflicted_project = files(project.parent)
            _, _ = call('codex-history', project, state, home, '--thread', active)
            check(files(project.parent) == conflicted_project, 'local history remains usable when a project snapshot has diverged')
            _, _ = call('codex-save', project, state, home, '--thread', active, fail=True)
            check(native.read_bytes() == original_native, 'divergent device histories stop safely and preserve both native originals')
            check(files(project.parent) == conflicted_project, 'an explicit conflicting save preserves every existing project file')
            _, traffic = call('codex-chat', project, state, home, '--prompt', 'AFTER CONFLICT FIXTURE', '--new-thread')
            check(any(x.get('method') == 'thread/start' for x in traffic), 'a conflicting old conversation does not prevent explicitly starting a new one')
            new_id = (local / 'active.txt').read_text().strip()
            _, traffic = call('codex-chat', project, state, home, '--prompt', 'LOCAL SELECTION FIXTURE')
            check(any(x.get('method') == 'thread/resume' and x['params']['threadId'] == new_id for x in traffic),
                  'reconnecting honors the local selection instead of the older saved project selection')
            check(files(project.parent) == conflicted_project, 'local continuation after a project conflict does not change Git-tracked snapshots')
            check(not any(p.name in ('auth.json', 'config.toml') or b'SYNTHETIC_AUTH_SENTINEL' in p.read_bytes() for p in moved.rglob('*') if p.is_file()),
                  'moving the game never copied login credentials or global Codex configuration')
            if args.codex:
                verify_official(args.codex, folder, game, archive, call, check, report)
            report['passed'] = True
    finally:
        (out / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('CONVERSATION_VERIFICATION_PASS', flush=True)


class Peer:
    def __init__(self, binary, home, cwd):
        home.mkdir(parents=True, exist_ok=True); cwd.mkdir(parents=True, exist_ok=True)
        self.p = subprocess.Popen([binary, 'app-server'], cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                  text=True, env=dict(os.environ, CODEX_HOME=str(home)))
        self.queue = queue.Queue(); self.number = 0
        def read():
            for line in self.p.stdout:
                self.queue.put(json.loads(line))
        threading.Thread(target=read, daemon=True).start()
        self.rpc('initialize', {'clientInfo': {'name': 'packengine_editor', 'version': '0.4.0'}, 'capabilities': {'experimentalApi': True}})
        self.p.stdin.write('{"method":"initialized","params":{}}\n'); self.p.stdin.flush()

    def rpc(self, method, params):
        self.number += 1
        self.p.stdin.write(json.dumps({'id': self.number, 'method': method, 'params': params}) + '\n'); self.p.stdin.flush()
        while True:
            value = self.queue.get(timeout=30)
            if value.get('id') == self.number:
                if 'error' in value:
                    raise RuntimeError(method + ': ' + str(value['error']))
                return value['result']

    def close(self):
        self.p.kill(); self.p.wait(timeout=10)


def verify_official(binary, folder, game, archive, call, check, report):
    project = game(folder / 'official-a/Game')
    home = folder / 'official-home-a'; cwd = folder / 'official-state-a/codex-workspace'
    peer = Peer(binary, home, cwd)
    config = {'features.shell_tool': False, 'features.unified_exec': False, 'features.hooks': False, 'features.memories': False,
              'features.multi_agent': False, 'features.skip_host_skill_discovery': True, 'web_search': 'disabled', 'project_doc_max_bytes': 0}
    options = {'cwd': str(cwd), 'sandbox': 'read-only', 'approvalPolicy': 'never', 'modelProvider': 'openai', 'config': config, 'environments': []}
    try:
        thread = peer.rpc('thread/start', options)['thread']
        peer.rpc('thread/inject_items', {'threadId': thread['id'], 'items': [{'type': 'message', 'role': 'user',
                 'content': [{'type': 'input_text', 'text': 'OFFICIAL NATIVE HISTORY FIXTURE; NO MODEL INFERENCE'}]}]})
        native = peer.rpc('thread/read', {'threadId': thread['id'], 'includeTurns': False})['thread']
        data = Path(native['path']).read_bytes()
    finally:
        peer.close()
    id = thread['id']; digest = hashlib.sha256(data).hexdigest(); profile = uuid.uuid4().hex
    store = archive(project); store.mkdir(parents=True)
    (store.parent / 'conversation.xml').write_text(f'<ProjectConversation version="1" id="{profile}" mode="local"><Title/><Url/></ProjectConversation>')
    (store / (id + '-' + digest + '.jsonl')).write_bytes(data)
    (store / (id + '.json')).write_text(json.dumps({'Version': 1, 'Project': profile, 'Id': id, 'Title': 'Official portability fixture',
        'NativeFile': Path(native['path']).name, 'Hash': digest, 'Model': '', 'UpdatedAt': 1}))
    (store / 'active.txt').write_text(id)
    moved = folder / 'official-b/Moved Game'; shutil.copytree(project.parent, moved)
    shutil.rmtree(home); shutil.rmtree(project.parent); shutil.rmtree(cwd.parent)
    project = moved / project.name; home = folder / 'official-home-b'; home.mkdir(); state = folder / 'official-state-b'
    _, _ = call('codex-history', project, state, home, '--thread', id, codex=binary)
    restored = next((home / 'sessions').rglob('*-' + id + '.jsonl'))
    check(restored.read_bytes() == data, 'real provider restores exact official Codex bytes with no previous home or absolute project path')
    peer = Peer(binary, home, state / 'codex-workspace')
    try:
        options['cwd'] = str(state / 'codex-workspace'); options['threadId'] = id; options['excludeTurns'] = True
        result = peer.rpc('thread/resume', options)
        check(result['thread']['id'] == id and result['cwd'] == options['cwd'] and result['sandbox']['type'] == 'readOnly',
              'official Codex resumes the imported native conversation in the new device directory with read-only sandbox')
        peer.rpc('thread/inject_items', {'threadId': id, 'items': [{'type': 'message', 'role': 'user',
                 'content': [{'type': 'input_text', 'text': 'SECOND DEVICE OFFICIAL FIXTURE; NO MODEL INFERENCE'}]}]})
        peer.rpc('thread/read', {'threadId': id, 'includeTurns': False})
        check(restored.read_bytes().startswith(data) and b'SECOND DEVICE OFFICIAL FIXTURE' in restored.read_bytes(),
              'official resumed storage appends to the restored history without discarding prior items')
        report['officialNativeRestoreTested'] = True
        report['officialCliVersion'] = subprocess.check_output([binary, '--version'], text=True).strip()
    finally:
        peer.close()


if __name__ == '__main__':
    main()
