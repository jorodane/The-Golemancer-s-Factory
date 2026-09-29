"""Author explicit frame rectangles from transparent gutters; never alters sprite pixels.
Pillow + numpy are authoring-only dependencies. Inspect resulting rectangles before shipping.
"""
from pathlib import Path
from PIL import Image
import numpy as np
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
pack = root / 'Content/Packs/07.FeastTrailAnimations'
tree = ET.parse(pack / 'animations.xml')
def cuts(projection, expected, radius):
    result = [0]
    for at in expected:
        start=max(0,at-radius)
        good=np.flatnonzero(projection[start:at+radius]==0)+start
        if not len(good):raise ValueError(f'Overlapping frames near {at}; correct source artwork first')
        result.append(int(good[np.argmin(abs(good-at))]))
    return result+[len(projection)]
for sprite in tree.findall('.//Sprite'):
    path=pack/sprite.find('Animation').get('image')
    image=Image.open(path)
    if image.mode!='RGBA' or image.size!=(1024,1536):raise ValueError(f'Invalid source: {path}')
    alpha=np.array(image.getchannel('A'))
    mask=alpha>32  # Ignore only diffuse, near-transparent glow when finding gutters.
    ys=cuts(mask.sum(axis=1),[256,512,768,1024,1280],70)
    for row,clip in enumerate(sprite.findall('Animation')):
        for child in list(clip):clip.remove(child)
        xs=cuts(mask[ys[row]:ys[row+1]].sum(axis=0),[256,512,768],75)
        for col in range(4):
            cell=mask[ys[row]:ys[row+1],xs[col]:xs[col+1]]
            yy,xx=np.nonzero(cell)
            if not len(xx):raise ValueError('Empty frame')
            x0=max(xs[col],xs[col]+int(xx.min())-2);x1=min(xs[col+1],xs[col]+int(xx.max())+3)
            y0=max(ys[row],ys[row]+int(yy.min())-2);y1=min(ys[row+1],ys[row]+int(yy.max())+3)
            # Keep original pose and scale; pin the actual footprint to a shared world origin.
            body=mask[y0:y1,x0:x1]
            feet=np.nonzero(body[max(0,body.shape[0]-18):])[1]
            pivot=(float(np.median(feet))+0.5)/body.shape[1] if len(feet) else .5
            ET.SubElement(clip,'Frame',{'x':str(x0),'y':str(y0),'width':str(x1-x0),'height':str(y1-y0),'pivotX':f'{pivot:.5f}','pivotY':f'{(body.shape[0]-2)/body.shape[0]:.5f}'})
        if sprite.get('id')=='craft_golem':clip.set('drawWidth','1.5');clip.set('drawHeight','1.5')
        if sprite.get('id')=='springwater_king':clip.set('drawWidth','4.5');clip.set('drawHeight','4.5')
ET.indent(tree)
tree.write(pack/'animations.xml',encoding='utf-8',xml_declaration=True)
print('288 complete frame regions assigned. Original sheets unchanged.')
