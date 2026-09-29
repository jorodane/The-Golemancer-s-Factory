"""Package the built native Windows app and separate real image packs (stdlib only)."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import json
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
output = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else root/'Artifacts'
output.mkdir(parents=True, exist_ok=True)
bin_dir = root/'src/Golemancer.Host/bin/Release/net48'
assert (bin_dir/'Golemancer.exe').is_file(), 'Build the net48 Release solution first'
packs = root/'Content/Packs'
art_packs = ('05.FeastTrailTiles', '06.FeastTrailArt', '07.FeastTrailAnimations')
for pack in packs.iterdir():
    manifest = pack/'pack.xml'
    if not manifest.is_file(): continue
    for assembly in ET.parse(manifest).findall('Assembly'):
        assert (pack/assembly.get('path').replace('{framework}', 'net48')).is_file(), assembly.attrib
for name in art_packs:
    for xml in (packs/name).glob('*.xml'):
        for node in ET.parse(xml).iter():
            if 'image' in node.attrib: assert (xml.parent/node.get('image')).is_file(), node.attrib

revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
common = '''The Golemancer's Factory — Windows / .NET Framework 4.8

1. Windows-net48 ZIP을 쓰기 가능한 폴더에 풀어줘.
2. Image-Packs-v2 ZIP도 같은 폴더에 풀어 Content 폴더를 합쳐줘.
3. Golemancer.exe를 실행해. .NET Framework 4.8이 필요해.

배포 EXE 실행에는 SDK나 브라우저가 필요 없어.
Saves/manual.json 및 Saves/autosave.json에 진행 상황을 저장해.
Esc로 저장 메뉴, F11로 전체 화면을 사용할 수 있어.

검증: net48 빌드와 Linux net10.0 공유 엔진·세션 검증을 통과했어.
실제 Windows 창 실행은 이 제작 환경에서 확인하지 못했어.
이미지·모드 명세: docs/ART_PACKS.md
'''
app_file = output/'The-Golemancers-Factory-Windows-net48.zip'
with ZipFile(app_file, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
    for file in sorted(bin_dir.iterdir()):
        if file.suffix in ('.exe', '.dll', '.config'): archive.write(file, file.name)
    for file in sorted(packs.rglob('*')):
        if not file.is_file() or 'Images' in file.parts: continue
        if file.suffix == '.xml' or ('net48' in file.parts and file.suffix in ('.dll','.config')):
            archive.write(file, file.relative_to(root).as_posix())
    for name in ('ART_PACKS.md','OBJECT_PACKS.md','VERIFICATION.md','CONTRACTS.md'):
        archive.write(root/'docs'/name, 'docs/'+name)
    archive.writestr('READ-ME.txt', common)
    archive.writestr('build-info.json', json.dumps({'commit':revision,'target':'net48','ui':'WPF','windows_gui_tested':False}, indent=2)+'\n')
art_file = output/'The-Golemancers-Factory-Image-Packs-v2.zip'
with ZipFile(art_file, 'w', ZIP_DEFLATED, compresslevel=9) as archive:
    for name in art_packs:
        for file in sorted((packs/name).rglob('*')):
            if file.is_file() and file.suffix in ('.xml','.svg','.png'):
                archive.write(file, file.relative_to(root).as_posix())
    archive.write(root/'docs/ART_PACKS.md', 'docs/ART_PACKS.md')
    preview = root/'TestResults/art/Animation-Preview.gif'
    if preview.is_file(): archive.write(preview, 'Previews/Animation-Preview.gif')
    archive.writestr('IMAGE-PACKS-README.txt', common+'\n포함: 정적 SVG 67개, 캐릭터 PNG 시트 12장, 72개 애니메이션·288프레임.\n타일셋·정적 그림·캐릭터 시트는 독립 객체팩이야.\n')
for file in (app_file, art_file):
    with ZipFile(file) as z:
        assert z.testzip() is None
        count = len(z.namelist())
    print(json.dumps({'path':str(file),'bytes':file.stat().st_size,'entries':count,'sha256':hashlib.sha256(file.read_bytes()).hexdigest()}))
