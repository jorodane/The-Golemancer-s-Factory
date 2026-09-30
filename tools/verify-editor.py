#!/usr/bin/env python3
"""Exercise the real Golemancer project through the generic editor workspace and CLI."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--skip-build', action='store_true')
    args = parser.parse_args()
    dotnet = shutil.which(args.dotnet)
    if not dotnet:
        raise RuntimeError('Provide --dotnet /path/to/dotnet.')
    output = ROOT / 'TestResults/editor'
    output.mkdir(parents=True, exist_ok=True)
    if not args.skip_build:
        for project in ['editor/PackEngine.Tool/PackEngine.Tool.csproj', 'editor/PackEngine.Assistant.Command/PackEngine.Assistant.Command.csproj']:
            subprocess.run([dotnet, 'build', project, '-c', 'Release', '-p:EngineTargetFramework=net10.0', '-p:UseSharedCompilation=false', '-m:1', '--disable-build-servers', '-v:minimal'], cwd=ROOT, check=True)
    runner = ROOT / 'editor/PackEngine.Tool/bin/Release/net10.0/PackEngine.Tool.dll'
    provider = ROOT / 'editor/PackEngine.Assistant.Command/bin/Release/net10.0/PackEngine.Assistant.Command.dll'
    report = {'passed': False, 'checks': [], 'windowsGuiTested': False}

    def check(ok, message):
        if not ok:
            raise RuntimeError(message)
        report['checks'].append(message)
        print('PASS: ' + message, flush=True)

    def digest(path):
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def ignore(folder, names):
        return set(names) & {'bin', 'obj', 'Saves', 'SmokeSaves', 'TestResults', '.git', 'Artifacts'}

    try:
        with tempfile.TemporaryDirectory(prefix='packengine-editor-') as folder:
            game = Path(folder) / 'Loaded Game'
            shutil.copytree(ROOT / 'Golemancer', game, ignore=ignore)
            state = Path(folder) / 'Editor State'
            sequence = 0

            def call(command, *options, fail=False, env=None):
                nonlocal sequence
                sequence += 1
                result = subprocess.run([dotnet, str(runner), command, '--project', str(game / 'Golemancer.packproject'), '--state', str(state), '--dotnet', dotnet, *map(str, options)],
                                        cwd=game, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
                (output / f'{sequence:02d}-{command}.log').write_text(result.stdout + result.stderr, encoding='utf-8')
                if bool(result.returncode) != fail:
                    raise RuntimeError(f'{command} returned {result.returncode}:\n{result.stderr[-6000:]}')
                return result

            locked = json.loads((game / 'SDK/engine-lock.json').read_text())['files']
            frozen = {p: digest(game / p) for p in locked}
            project = json.loads(call('inspect').stdout)
            check(project['Id'] == 'golemancer' and len(project['Packs']) == 18 and not project['Diagnostics'], 'load Golemancer as a declared project without engine or game source coupling')
            graph = json.loads(call('graph').stdout)
            check(any(n['Key'] == 'action:buy' for n in graph['Nodes']) and any(l['From'] == 'widget:golemancer.costButton' and l['To'] == 'widget:golemancer.button' for l in graph['Links']), 'project-owned game symbols and inherited UI share the navigable relationship index')
            check(any(n['Key'] == 'implementation:commerce.buy' and n['Status'] == 'runtime-unknown' for n in graph['Nodes']), 'unexecuted DLL implementations remain explicitly unknown')
            ui = 'Content/Packs/02.Controls/ui.xml'
            commerce = 'Content/Packs/40.Commerce/actions.xml'
            context = json.loads(call('context', '--point', 'view:golemancer.purchase', '--open', ui, '--prompt', '구매 버튼의 글자 크기를 바꿔줘.', '--budget', '24000').stdout)['Request']
            check(len(context['Context']) == 1 and context['Context'][0]['Path'] == ui and '<View id="golemancer.purchase"' in context['Context'][0]['Content'] and len(context['Documents']) == 1 and context['Input']['Mode'] == 'single', 'pointing exports only the chosen XML definition and open-document metadata; parents and other documents remain demand-read')
            ordinary = json.loads(call('context', '--select', 'widget:golemancer.button', '--prompt', '그냥 대화 중이야').stdout)['Request']
            check(not ordinary['Context'] and not ordinary['Input']['Targets'] and ordinary['Input']['Mode'] == 'none', 'ordinary chat and navigation never attach a stale object or its contents')
            current = json.loads((state / 'session.json').read_text())
            check(not current['Reads'] and sum(len(c['Content']) for c in context['Context']) <= 24000, 'context export respects its character budget and never pretends that AI has read it')
            read = json.loads(call('read', '--request', context['Id'], '--file', commerce).stdout)
            current = json.loads((state / 'session.json').read_text())
            check(read['Content'] == (game / commerce).read_text() and len(current['Reads']) == 1 and commerce not in current['OpenFiles'], 'explicit assistant reads are recorded separately from user-open documents')
            call('read', '--request', context['Id'], '--file', '../outside.txt', fail=True)
            call('read', '--request', context['Id'], '--file', 'README.md', fail=True)
            check(True, 'assistant reads reject escaping and undeclared document paths')

            # Reopen a saved native-editor buffer and distinguish it from a newer disk version.
            buffer_text = (game / ui).read_text()
            buffer_bytes = (game / ui).read_bytes()
            current['Drafts'] = [{'Path': ui, 'Baseline': digest(game / ui), 'Original': buffer_text,
                                  'Text': buffer_text.replace('property="fontSize" value="13"', 'property="fontSize" value="15"')}]
            (state / 'session.json').write_text(json.dumps(current))
            restored = json.loads(call('context', '--point', 'widget:golemancer.costButton', '--prompt', '저장한 초안을 이어서 확인해줘').stdout)['Request']
            check(any(c['Path'] == ui and c['Draft'] and 'value="15"' in c['Content'] for c in restored['Context']) and (game / ui).read_bytes() == buffer_bytes,
                  'reopening restores unsaved editor buffers without applying them to project files')
            (game / ui).write_bytes(buffer_bytes + b'\n')
            changed = json.loads(call('context', '--point', 'widget:golemancer.costButton', '--prompt', '외부 변경 여부 확인').stdout)['Request']
            check(any(c['Path'] == ui and c['DiskChanged'] for c in changed['Context']), 'shared context marks a newer disk version separately from the visible editor draft')
            (game / ui).write_bytes(buffer_bytes)
            current = json.loads((state / 'session.json').read_text()); current['Drafts'] = []
            (state / 'session.json').write_text(json.dumps(current))

            original = (game / ui).read_bytes()
            proposal = Path(folder) / 'proposed.xml'
            proposal.write_text((game / ui).read_text().replace('property="fontSize" value="13"', 'property="fontSize" value="15"'))
            draft = json.loads(call('preview', '--file', ui, '--text-file', proposal, '--intent', '가격 버튼 문구 가독성 확인').stdout)
            check(any('fontSize' in c['Member'] and c['After'] == '15' for c in draft['Changes']) and 'view:golemancer.purchase' in draft['Impact'] and (game / ui).read_bytes() == original, 'preview reports semantic XML changes and downstream views without changing project files')
            call('apply', '--change', draft['Id'])
            inspected = json.loads(call('inspect', '--node', 'widget:golemancer.costButton').stdout)
            check(next(p for p in inspected['Definition']['Properties'] if p['Name'] == 'fontSize')['Default'] == '15', 'applying a reviewed XML change updates the inherited contract')
            applied = (game / ui).read_bytes()
            (game / ui).write_bytes(applied + b'\n<!-- external edit -->\n')
            call('undo', '--change', draft['Id'], fail=True)
            check((game / ui).read_bytes().endswith(b'<!-- external edit -->\n'), 'undo refuses to overwrite a newer external edit')
            (game / ui).write_bytes(applied)
            call('undo', '--change', draft['Id'])
            check((game / ui).read_bytes() == original, 'undo restores the exact prior bytes when the version still matches')

            # This process is only a transport fixture, not a simulated AI in the product.
            client = Path(folder) / 'protocol-client.py'
            client.write_text('''import json,sys
request=json.loads(sys.stdin.readline())
assert request['type']=='request' and request['protocol']==1
print(json.dumps({'type':'read','path':'Content/Packs/40.Commerce/actions.xml'}),flush=True)
read=json.loads(sys.stdin.readline())
assert read['type']=='read-result' and 'commerce.buy' in read['item']['Content']
print(json.dumps({'type':'inspect','key':'view:golemancer.purchase'}),flush=True)
inspection=json.loads(sys.stdin.readline())
assert inspection['type']=='inspect-result' and 'engine.button.view' in inspection['content']
print(json.dumps({'type':'reply','text':'PROVIDER_PROTOCOL_OK'}),flush=True)
''')
            config = Path(folder) / 'assistant.json'
            config.write_text(json.dumps({'Executable': shutil.which('python3'), 'Arguments': [str(client)], 'TimeoutSeconds': 15}))
            response = call('assist', '--provider', provider, '--select', 'view:golemancer.purchase', '--prompt', '읽기 프로토콜 확인', env=dict(os.environ, PACKENGINE_ASSISTANT_CONFIG=str(config)))
            check('PROVIDER_PROTOCOL_OK' in response.stdout and len(json.loads((state / 'session.json').read_text())['Reads']) == 3, 'an independently loaded assistant provider exchanges real read and inspection requests over JSON lines')

            source = next((game / 'modules/Golemancer.Commerce').glob('*.cs'))
            source_bytes = source.read_bytes()
            all_modules = {str(p.relative_to(game)): digest(p) for p in (game / 'Content/Packs').glob('*/Bin/net10.0/*.dll')}
            source.write_bytes(source_bytes + b'\n// editor workflow verification\n')
            call('build-pack', '--pack', 'commerce', '--target', 'linux')
            others = {p: digest(game / p) for p in all_modules if '/40.Commerce/' not in p}
            check(all(all_modules[p] == value for p, value in others.items()) and frozen == {p: digest(game / p) for p in locked}, 'building one real implementation pack preserves other pack DLLs and the frozen engine SDK')
            published = game / 'Content/Packs/40.Commerce/Bin/net10.0/Golemancer.Commerce.dll'
            stable_dll = digest(published)
            source.write_bytes(source_bytes + b'\nthis is deliberately invalid C#;\n')
            call('build-pack', '--pack', 'commerce', '--target', 'linux', fail=True)
            check(digest(published) == stable_dll, 'a failed implementation build retains the previously published pack DLL')
            source.write_bytes(source_bytes)
            campaign = call('verify', '--target', 'linux')
            combined = campaign.stdout + campaign.stderr
            check('PASS: FULL CAMPAIGN:' in combined, 'project-declared verification executes the real complete Golemancer campaign')
            report['campaignChecks'] = sum(line.startswith('PASS:') for line in combined.splitlines())
            smoke = call('smoke', '--target', 'linux')
            native = smoke.stdout + smoke.stderr
            check('LINUX_SMOKE_PASS: 30 native frames' in native, 'project-declared launch smoke executes real external DLLs and 30 native Linux frames')
            report['linuxChecks'] = sum(line.startswith('PASS:') for line in native.splitlines())
            check(frozen == {p: digest(game / p) for p in locked}, 'editor workflow leaves all pinned SDK and Windows engine files unchanged')
            report['passed'] = True
    finally:
        (output / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('EDITOR_WORKFLOW_PASS', flush=True)


if __name__ == '__main__':
    main()
