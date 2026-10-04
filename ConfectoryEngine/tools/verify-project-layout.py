"""Verify explicit project selection and an output-free project copied outside the repository."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

engine = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--target', default='portable')
args = parser.parse_args()
manifest = args.project.resolve()
if manifest.is_dir():
    files = list(manifest.glob('*.packproject'))
    if len(files) != 1:
        raise RuntimeError('Select one project manifest.')
    manifest = files[0]
output = engine / 'TestResults/project-layout'
output.mkdir(parents=True, exist_ok=True)

def run(label, command, cwd=engine):
    result = subprocess.run(command, cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (output / (label + '.log')).write_text(result.stdout)
    if result.returncode:
        raise RuntimeError(result.stdout[-6000:])
    print('PASS: ' + label, flush=True)
    return result.stdout

# No-argument build does not guess a project or start a compilation.
result = subprocess.run(['bash', str(engine / 'build.sh')], capture_output=True, text=True)
assert result.returncode == 2 and 'Usage:' in result.stderr
assert 'Golemancer' not in result.stdout + result.stderr
print('PASS: no project argument only displays usage', flush=True)
for area in ('src', 'editor', 'packs'):
    for project in (engine / area).rglob('*.csproj'):
        if {'obj', 'bin'} & set(project.parts):
            continue
        for item in ET.parse(project).iter('ProjectReference'):
            for reference in item.get('Include', '').split(';'):
                if '$(' not in reference:
                    resolved = (project.parent / reference).resolve()
                    assert resolved.is_relative_to(engine) and resolved.is_file(), str(resolved)
print('PASS: engine project references stay inside the independent engine', flush=True)
with tempfile.TemporaryDirectory(prefix='confectory-external-') as folder:
    consumer = Path(folder) / 'Renamed project with spaces'
    shutil.copytree(manifest.parent, consumer, ignore=shutil.ignore_patterns('bin', 'Bin', 'obj', 'Builds', 'builds', 'Saves', 'SmokeSaves', 'TestResults', 'Artifacts', '.git'))
    assert not (consumer / 'Builds').exists() and not (consumer / 'SDK').exists()
    assert not list(consumer.glob('*.bat'))
    command = [args.dotnet, str(engine / 'editor/Confectory.Tool/bin/Release/net10.0/Confectory.Tool.dll')]
    options = ['--project', str(consumer), '--target', args.target, '--engine-root', str(engine), '--dotnet', args.dotnet]
    run('external-clean-build', command + ['build-project', *options])
    verification = run('external-verification', command + ['verify', *options])
    assert 'PASS:' in verification
    assert not (consumer / 'SDK').exists()
    # Every copied core DLL comes from the engine's prepared SDK, not the retired project output.
    sdk = engine / 'Builds/SDK/net10.0'
    for name in ('Confectory.Contracts.dll', 'Confectory.Runtime.dll'):
        expected = hashlib.sha256((sdk / name).read_bytes()).digest()
        assemblies = list((consumer / 'tests').rglob(name))
        assert assemblies and all(hashlib.sha256(p.read_bytes()).digest() == expected for p in assemblies)
    print('PASS: copied project references the selected engine SDK and needs no previous output', flush=True)
