// Node 20+; no packages required. Images stay outside Git and are delivered separately.
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {sprites,portrait,itemIcon,tileColors,tileSvg} from '../src/Golemancer.Host/wwwroot/art.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const output=path.resolve(process.argv[2]||path.join(root,'Artifacts','Vector-Art-Pack'));
const manifest={version:1,style:'hand-authored vector',sprites:{},tiles:{},items:{},portraits:{}};
const definitions=await readFile(path.join(root,'Content/Packs/90.FeastTrail/objects.xml'),'utf8');
const kinds=Object.fromEntries([...definitions.matchAll(/<Object\s+id="([^"]+)"[^>]+kind="([^"]+)"/g)].map(m=>[m[1],m[2]]));
const groups={golem:'Actors',npc:'Actors',customer:'Actors',monster:'Enemies',boss:'Enemies',boss_part:'Enemies',resource:'Resources',facility:'Facilities',landmark:'World',drop:'World'};
async function save(relative,content){const file=path.join(output,'Assets',relative);await mkdir(path.dirname(file),{recursive:true});await writeFile(file,content);return relative;}
for(const [id,source]of Object.entries(sprites))manifest.sprites[id]=await save(`${groups[kinds[id]]||'World'}/${id}.svg`,source);
for(const id of Object.keys(tileColors))manifest.tiles[id]=await save(`Tiles/${id}.svg`,tileSvg(id));
const items=await readFile(path.join(root,'Content/Packs/90.FeastTrail/items.xml'),'utf8');
for(const m of items.matchAll(/<Item\s+id="([^"]+)"([^>]*)/g)){const color=m[2].match(/color="([^"]+)"/)?.[1];manifest.items[m[1]]=await save(`Icons/${m[1]}.svg`,itemIcon(m[1],color));}
for(const [mood,chalk]of Object.entries({sleepy:'… zZ',tired:'−_−',neutral:'…',happy:'^_^',smile:'♡',worried:'!',surprised:'O_O',curious:'? → !'}))manifest.portraits[mood]=await save(`Portraits/enrin_${mood}.svg`,portrait(mood,chalk));
await save('manifest.json',JSON.stringify(manifest,null,2));
await writeFile(path.join(output,'README.txt'),'The Golemancer’s Factory — Vector Art Pack v1\n\nAssets 폴더를 저장소 루트에 복사해줘. manifest.json을 통해 게임이 자동으로 읽어.\nSVG가 게임용 원본이고 PNG 폴더는 선택용 미리보기야.\nPortraits: 에메랄드 팔찌 / 머리 위 미니골렘 / 감정 칠판이 모두 포함된 엔린 8표정.\nTiles: 64×64, Sprites/Icons: 128×128, Portraits: 320×420. 배경은 투명.\n실제 타일 점유 크기는 Content/Packs/90.FeastTrail/objects.xml에 정의되어 있어.\n게임의 대사 중 칠판 문구는 SVG 원본 위에 동적으로 표시해.\n\n이미지 파일은 코드 커밋에서 제외했어. Git 명령으로 올릴 때: git add -f Assets\n새 이미지로 교체해도 manifest.json 경로를 유지하면 돼.\n모든 그림은 이 프로젝트를 위해 직접 작성한 벡터 도형이야.\n');
const entries=[...Object.entries(manifest.sprites),...Object.entries(manifest.items)];
let sheet=`<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="${100+Math.ceil(entries.length/8)*165}" style="background:#e8e2ca"><text x="28" y="45" font-family="serif" font-size="25" fill="#355344">The Golemancer’s Factory · Vector collection</text>`;
for(let i=0;i<entries.length;i++){const [id,file]=entries[i],source=await readFile(path.join(output,'Assets',file),'utf8'),x=20+i%8*147,y=68+Math.floor(i/8)*165;sheet+=`<g transform="translate(${x} ${y})">${source.replace('<svg ','<svg x="0" y="0" ')}<text x="64" y="145" text-anchor="middle" font-family="sans-serif" font-size="9" fill="#355344">${id}</text></g>`;}
await writeFile(path.join(output,'Contact-Sheet.svg'),sheet+'</svg>');
console.log(`Exported ${Object.values(manifest).filter(v=>typeof v==='object').reduce((n,v)=>n+Object.keys(v).length,0)} SVG assets to ${output}`);
