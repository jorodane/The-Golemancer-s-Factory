#!/usr/bin/env python3
"""Check the actual installed Windows or packaged Android engine without executing its DLLs."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
source = parser.add_mutually_exclusive_group(required=True)
source.add_argument('--directory', type=Path)
source.add_argument('--apk-directory', type=Path)
parser.add_argument('--receipt', type=Path)
args = parser.parse_args()
archive = None
if args.apk_directory:
    apks = sorted(args.apk_directory.rglob('*-Signed.apk'))
    if not apks:
        apks = sorted(args.apk_directory.rglob('*.apk'))
    if not apks:
        raise RuntimeError('Android build did not produce an APK.')
    archive = zipfile.ZipFile(apks[0])
    def read(path):
        return archive.read('assets/Engine/' + path)
else:
    def read(path):
        return (args.directory / path).read_bytes()
sha = lambda data: hashlib.sha256(data).hexdigest()
xml = ET.fromstring(read('distribution.xml'))
assert xml.tag == 'EditorEngineDistribution' and xml.get('contracts') == 'editor-1'
files = {e.get('path'): e.get('hash') for e in xml.findall('File')}
assert len(files) == len(xml.findall('File')) and files
for path, expected in files.items():
    assert sha(read(path)) == expected, path
signatures = [xml.get('id'), xml.get('release'), 'editor-1']
for pack in xml.findall('Pack'):
    prefix = pack.get('path') + '/'
    manifest = ET.fromstring(read(prefix + 'pack.xml'))
    assert manifest.get('id') == pack.get('id')
    runtime = ['pack.xml']
    documents = ['pack.xml']
    for element in manifest:
        if element.tag in ('Ui', 'Data', 'Assembly'):
            path = element.get('path').replace('{framework}', xml.get('framework'))
            runtime.append(path)
            if element.tag == 'Assembly':
                folder = str(Path(path).parent).replace('\\', '/') + '/'
                runtime += [f[len(prefix):] for f in files if f.startswith(prefix + folder)
                            and '/' not in f[len(prefix + folder):] and (f.lower().endswith('.dll') or f.endswith('.deps.json'))]
        if element.tag in ('Ui', 'Data', 'Source'):
            documents.append(element.get('path'))
        if element.tag == 'Source':
            folder = str(Path(element.get('path')).parent).replace('\\', '/')
            source_prefix = '' if folder == '.' else folder + '/'
            documents += [f[len(prefix):] for f in files if f.startswith(prefix + source_prefix) and f.lower().endswith('.cs')]
    fingerprint = '\n'.join(path + ':' + sha(read(prefix + path)) for path in sorted(set(runtime)))
    assert sha(fingerprint.encode()) == pack.get('fingerprint'), pack.get('id')
    for path in sorted(set(documents)):
        text = read(prefix + path).decode('utf-8-sig').replace('\r\n', '\n')
        signatures.append(pack.get('id') + '/' + path + ':' + sha(text.encode()))
assert sha('\n'.join(signatures).encode()) == xml.get('compatibility'), 'Engine compatibility receipt mismatch'
receipt = {'id': xml.get('id'), 'release': xml.get('release'), 'compatibility': xml.get('compatibility'),
           'framework': xml.get('framework'), 'packs': len(xml.findall('Pack')), 'files': len(files)}
if args.receipt:
    args.receipt.write_text(json.dumps(receipt), encoding='utf-8')
print('ENGINE_DEPLOYMENT_CHECKS=' + str(len(files) + 4))
print(json.dumps(receipt))
if archive:
    archive.close()
