"""Validate shipped image packs and render the exact XML frame regions for QA.
Pillow and numpy are authoring-only dependencies. Original images stay unchanged.
"""
from pathlib import Path
import hashlib
import xml.etree.ElementTree as ET
import numpy as np
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
packs = root / 'Content/Packs'
out = root / 'TestResults/art'
out.mkdir(parents=True, exist_ok=True)
images = set()
for folder in sorted(packs.iterdir()):
    for xml in folder.glob('*.xml'):
        for entry in ET.parse(xml).iter():
            if 'image' in entry.attrib:
                path = (xml.parent / entry.get('image')).resolve()
                assert path.is_relative_to(xml.parent.resolve()) and path.is_file(), path
                images.add(path)
                if path.suffix == '.svg':
                    ET.parse(path)

pack = packs / '07.FeastTrailAnimations'
sprites = ET.parse(pack / 'animations.xml').findall('.//Sprite')
states = ('idle', 'move', 'work', 'attack', 'hit', 'death')
frames = [Image.new('RGB', (1008, 80 + len(sprites) * 164), '#d9dbc9') for _ in range(4)]
count = 0
for row, sprite in enumerate(sprites):
    clips = {c.get('state'): c for c in sprite.findall('Animation')}
    assert set(clips) == set(states)
    for col, state in enumerate(states):
        clip = clips[state]
        sheet = Image.open(pack / clip.get('image'))
        assert sheet.mode == 'RGBA' and sheet.size == (1024, 1536)
        alpha = np.asarray(sheet.getchannel('A'))
        assert np.mean(alpha == 0) > .25, 'Sheet must have a transparent background'
        seen = []
        rectangles = clip.findall('Frame')
        assert len(rectangles) == int(clip.get('frames')) == 4
        for index, rect in enumerate(rectangles):
            x, y, w, h = [int(rect.get(k)) for k in ('x', 'y', 'width', 'height')]
            assert 0 <= x < x+w <= sheet.width and 0 <= y < y+h <= sheet.height
            crop = sheet.crop((x, y, x+w, y+h))
            assert np.count_nonzero(np.asarray(crop.getchannel('A')) > 32) > 500
            seen.append(hashlib.sha256(crop.tobytes()).hexdigest())
            # Render using the same scale and pivot as WPF, fitting large bosses in a QA cell.
            size = min(float(clip.get('drawWidth')), 1.65)
            scale = 75 * size / int(clip.get('frameWidth'))
            draw_w, draw_h = round(w*scale), round(h*scale)
            view = crop.resize((draw_w, draw_h), Image.Resampling.LANCZOS)
            px = 84 + col*168 + float(clip.get('offsetX'))*75 - draw_w*float(rect.get('pivotX'))
            py = 196 + row*164 + float(clip.get('offsetY'))*75 - draw_h*float(rect.get('pivotY'))
            frames[index].paste(view, (round(px), round(py)), view)
            draw = ImageDraw.Draw(frames[index])
            if row == 0: draw.text((col*168+60, 44), state, fill='#274936')
            if col == 0: draw.text((8, 66+row*164), sprite.get('id'), fill='#274936')
            count += 1
        assert len(set(seen)) == 4, 'Animation must contain four distinct drawings'
for i, preview in enumerate(frames):
    ImageDraw.Draw(preview).text((12, 12), 'Animation sheet frames - XML crops and pivots (QA preview)', fill='#274936')
    preview.save(out / f'frames-{i}.png')
frames[0].save(out/'Animation-Preview.gif', save_all=True, append_images=frames[1:], duration=220, loop=0)
sheet_count = len({clip.get('image') for sprite in sprites for clip in sprite.findall('Animation')})
print(f'PASS: {len(images)} actual images; {sheet_count} RGBA sheets; {len(sprites)} sprite bindings; {len(sprites)*len(states)} clips; {count} bounded, nonempty, distinct frames.')
dedicated = packs / '91.DeguldolArt'
if dedicated.exists():
    for clip in ET.parse(dedicated / 'sprites.xml').findall('.//Animation'):
        sheet = Image.open(dedicated / clip.get('image')).convert('RGBA')
        rectangles = clip.findall('Frame') or [clip]
        for rect in rectangles:
            x, y = int(rect.get('x', 0)), int(rect.get('y', 0))
            w, h = int(rect.get('width', clip.get('frameWidth'))), int(rect.get('height', clip.get('frameHeight')))
            assert 0 <= x < x+w <= sheet.width and 0 <= y < y+h <= sheet.height
            assert np.count_nonzero(np.asarray(sheet.crop((x, y, x+w, y+h)).getchannel('A')) > 32) > 500
    print('PASS: pulled dedicated Deguldol sheet (24 frames) and stone icon have valid, nonempty PNG crops.')
print(f'QA previews: {out}')
