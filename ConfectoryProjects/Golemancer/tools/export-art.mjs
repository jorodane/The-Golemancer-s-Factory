// Optional offline authoring: export the original static SVG art as real pack files.
// The Windows game never imports this file or generates its own artwork.
import {mkdir, writeFile, readFile, copyFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {sprites, portrait, itemIcon, tileColors, tileSvg} from './vector-art-source.mjs';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const output = path.resolve(process.argv[2] || path.join(root, 'Artifacts', 'Static-Image-Packs'));
const definitions = await readFile(path.join(root, 'Content/Packs/90.FeastTrail/objects.xml'), 'utf8');
const kinds = Object.fromEntries([...definitions.matchAll(/<Object\s+id="([^"]+)"[^>]+kind="([^"]+)"/g)].map(m => [m[1], m[2]]));
const groups = {golem:'Actors', npc:'Actors', customer:'Actors', monster:'Enemies', boss:'Enemies', boss_part:'Enemies', resource:'Resources', facility:'Facilities', landmark:'World', drop:'World'};
let count = 0;
async function save(pack, relative, content) {
  const file = path.join(output, 'Content/Packs', pack, 'Images', relative);
  await mkdir(path.dirname(file), {recursive:true});
  await writeFile(file, content); count++;
}
for (const [id, source] of Object.entries(sprites)) await save('06.FeastTrailArt', `${groups[kinds[id]] || 'World'}/${id}.svg`, source);
for (const id of Object.keys(tileColors)) await save('05.FeastTrailTiles', `${id}.svg`, tileSvg(id));
const items = await readFile(path.join(root, 'Content/Packs/90.FeastTrail/items.xml'), 'utf8');
for (const m of items.matchAll(/<Item\s+id="([^"]+)"([^>]*)/g)) await save('06.FeastTrailArt', `Icons/${m[1]}.svg`, itemIcon(m[1], m[2].match(/color="([^"]+)"/)?.[1]));
for (const [mood, chalk] of Object.entries({sleepy:'… zZ', tired:'−_−', neutral:'…', happy:'^_^', smile:'♡', worried:'!', surprised:'O_O', curious:'? → !'})) await save('06.FeastTrailArt', `Portraits/enrin_${mood}.svg`, portrait(mood, chalk));
for (const [pack, data] of [['05.FeastTrailTiles','tileset.xml'], ['06.FeastTrailArt','sprites.xml']])
  for (const file of ['pack.xml', data]) await copyFile(path.join(root, 'Content/Packs', pack, file), path.join(output, 'Content/Packs', pack, file));
console.log(`Exported ${count} static SVG images to ${output}. Install the separate 07.FeastTrailAnimations PNG pack too.`);
