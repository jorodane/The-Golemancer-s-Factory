/* Authored vector geometry. Export with tools/export-art.mjs. No bitmap dependencies. */
const svg = (body, w=128, h=128) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${body}</svg>`;
const path = (d, fill, stroke='', width=3) => `<path d="${d}" fill="${fill}"${stroke?` stroke="${stroke}" stroke-width="${width}" stroke-linejoin="round" stroke-linecap="round"`:''}/>`;
const rect = (x,y,w,h,fill,r=0,stroke='') => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}"${stroke?` stroke="${stroke}" stroke-width="3"`:''}/>`;
const ellipse = (x,y,rx,ry,fill) => `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${fill}"/>`;
const shadow = ellipse(64,112,39,10,'#182f3330');
const leaf = (x,y,s,c,rot=0) => `<g transform="translate(${x} ${y}) rotate(${rot}) scale(${s})">${path('M0 0Q-27-24-10-40Q13-34 0 0',c)}${path('M0 0L-9-31','none','#365e49',1.5)}</g>`;
const crystal = (color='#bdeadc') => path('M64 19L87 46 79 87 64 105 45 85 40 48Z',color,'#447477')+path('M64 19L58 55 64 105 79 87 70 53Z','#ffffff65')+path('M40 48L58 55 87 46M58 55L45 85','none','#72a9aa',2);
const core = c => rect(37,33,54,64,'#c99c65',10,'#70513d')+rect(44,40,40,45,'#eed1a2',7)+path('M64 45L80 64 64 83 48 64Z',c,'#755942',2)+path('M64 49L64 76 54 64Z','#ffffff60');

function golem(type) {
  const mini=type==='mini_golem', metal=type==='mining_golem', battle=type==='combat_golem';
  const body=metal?'#aab7b0':'#c59d6b', light=metal?'#d7dfd3':'#e1c297', edge=metal?'#637c7b':'#796148';
  const c={harvest_golem:'#81b880',craft_golem:'#d99461',combat_golem:'#ca7774',mining_golem:'#9cbfe0',mini_golem:'#b2c971'}[type];
  return svg(shadow+`<g transform="${mini?'translate(17 28) scale(.73)':''}">`+
    rect(36,89,20,23,edge,5)+rect(73,89,20,23,edge,5)+rect(31,104,28,11,body,4)+rect(69,104,28,11,body,4)+
    rect(20,64,18,35,body,5,edge)+rect(91,64,18,35,body,5,edge)+rect(34,59,61,41,body,8,edge)+rect(41,62,47,29,light,4)+
    path('M64 65L74 77 64 89 54 77Z',c,'#637961',2)+rect(29,21,71,46,body,10,edge)+rect(34,25,60,30,light,7)+
    rect(44,39,8,10,'#33595a',3)+rect(77,39,8,10,'#33595a',3)+path('M57 53L70 53','none',edge,2)+
    path('M39 29L51 29M83 57L91 57','none','#f5dfb4',2)+
    (type==='harvest_golem'?leaf(69,24,.6,'#668e63',35)+leaf(65,25,.45,'#90b575',-30):'')+
    (type==='craft_golem'?rect(34,19,61,10,'#6b6e55',3)+rect(53,8,24,18,'#b28762',5)+path('M28 72L13 63 8 73 25 81Z','#b4c4b7'):'')+
    (battle?path('M28 20L65 7 101 21 100 34 30 34Z','#768e87', '#50645d')+path('M23 67L6 61 8 102 22 111 35 96 35 68Z','#97aaa0',edge)+rect(97,44,8,52,'#907754',2)+rect(90,76,23,7,'#746344',2):'')+
    (metal?path('M29 24Q62-5 99 24L100 34 28 34Z','#687b75')+ellipse(64,26,10,9,'#ecebc0')+path('M99 45L88 78 103 55 120 57Z','#b0c6bd',edge):'')+'</g>');
}
function herb(id) {
  let color=id==='newflesh_herb_patch'?'#d78076':id==='spark_herb_patch'?'#ae91b8':'#b8cd75';
  return svg(ellipse(64,112,34,7,'#365f4530')+leaf(64,109,1.1,'#55794f',-28)+leaf(61,111,1.4,'#729958',26)+leaf(65,108,1.15,'#98b866',65)+leaf(58,110,.9,'#9abb74',-60)+
    [36,60,87].map((x,i)=>path(`M${x} 92Q${x+9} 68 ${x+2} ${64-i*7}`,'none','#4d7958',3)+ellipse(x+2,65-i*7,9,14,color)+path(`M${x-1} ${60-i*7}L${x+4} ${66-i*7}`,'none','#eff2be',2)).join(''));
}
function tree(fruit=false) {
  return svg(shadow+path('M54 114L57 67 74 65 79 114Z','#886647')+path('M65 106L67 70 73 68 74 111Z','#b28a59')+
    path('M64 9C37 7 35 27 25 34C0 43 12 71 27 77C20 95 41 103 63 91C83 104 112 89 104 71C124 54 107 33 91 32C89 15 78 9 64 9Z','#365e4c','#294e43',2)+
    path('M62 11Q34 16 35 42Q17 45 23 65Q40 79 60 62Q82 79 103 60Q109 43 88 36Q92 16 62 11Z','#57825c')+
    path('M43 29Q53 10 69 19L65 32 79 41 63 47 50 40 32 45Z','#7a9e66')+
    (fruit?[30,63,88].map((x,i)=>ellipse(x,65+(i%2)*15,7,8,'#dfaf67')+path(`M${x} ${59+(i%2)*15}l3-5`,'none','#abc87a',2)).join(''):''));
}
function shelf(fine=false) {
  return svg(shadow+rect(12,33,104,78,fine?'#6c8061':'#886648',5,'#564d3d')+rect(20,40,88,29,'#493f34',2)+rect(20,77,88,25,'#493f34',2)+rect(9,68,110,9,'#d2ad72',2)+rect(8,27,112,12,'#e0c58f',3)+
    [29,49,79,97].map((x,i)=>rect(x,51-i%2*6,12,16+i%2*6,['#a7c77f','#d0958a','#abcbd2','#bd9dc3'][i],3)+rect(x+3,46-i%2*6,6,6,'#d4b882',1)).join('')+
    [27,55,86].map((x,i)=>rect(x,84,20,16,['#bbc68f','#c8b18d','#9caf9d'][i],3)).join('')+(fine?path('M10 31L22 13 43 24 64 9 84 24 106 13 118 31Z','#bca369'):'')+rect(17,110,9,9,'#624832',2)+rect(101,110,9,9,'#624832',2));
}
function enrinSmall() {
  return svg(shadow+path('M43 68L33 111Q63 126 95 110L84 66Z','#3d6b58','#2c5248')+path('M44 75L46 106M82 75L79 111','none','#a8a47c',3)+ellipse(64,48,25,28,'#eed1b4')+
    path('M36 48Q28 14 60 15Q98 11 94 54L78 33 56 37 45 28 41 57Z','#d6bdae')+path('M39 44Q26 68 34 93L46 97 46 51M89 40Q104 75 93 98L82 96 82 49','#c6a894','#a18475',2)+
    path('M47 48L58 48M72 48L81 48M59 61L66 61','none','#5b6358',2)+rect(81,72,15,8,'#bd9e61',3)+path('M86 71L92 72 94 77 87 80 84 75Z','#54b991')+
    rect(47,1,34,20,'#c49e6d',4,'#756346')+rect(54,8,4,5,'#42675a',1)+rect(70,8,4,5,'#42675a',1)+rect(77,4,33,23,'#547268',2,'#b99462')+path('M84 13L89 13M96 13L101 13M88 20L97 20','none','#e3dfc0',1.5));
}
export function portrait(mood='sleepy', chalk='…') {
  const cheerful=mood==='happy'||mood==='smile';
  const eyes=cheerful?path('M113 171Q123 160 135 171M174 171Q184 160 195 170','none','#555c54',4):mood==='surprised'?ellipse(125,172,6,9,'#769078')+ellipse(182,172,6,9,'#769078'):path(mood==='worried'?'M112 169L137 163M172 163L196 169':mood==='curious'?'M112 167L137 167M172 160L196 163':'M112 169L137 169M172 169L196 169','none','#555c54',4)+ellipse(128,173,4,5,'#769078')+ellipse(181,173,4,5,'#769078');
  const mouth=cheerful?'M145 193Q158 204 169 192':mood==='worried'?'M147 199Q158 192 168 198':mood==='surprised'?'M153 196Q158 188 163 196Q158 205 153 196':'M151 198L164 198';
  const safe=chalk.replace(/[&<>"']/g,'');
  let braids=''; for(let i=0;i<6;i++)braids+=ellipse(83-i*.8,199+i*21,17-i*.8,17,'#cbb3a0')+path(`M${72-i*.8} ${193+i*21}q10 16 22 4`,'none','#b29684',2)+ellipse(224+i*.8,199+i*21,17-i*.8,17,'#cbb3a0')+path(`M${213+i*.8} ${193+i*21}q10 16 22 4`,'none','#b29684',2);
  return svg(`<defs><linearGradient id="dress" x2="1" y2="1"><stop stop-color="#477461"/><stop offset="1" stop-color="#263f3b"/></linearGradient></defs>`+
    path('M97 255Q66 265 45 366L42 420 278 420 272 362Q253 273 214 254Z','url(#dress)','#263f3b')+
    path('M98 271Q133 307 162 308Q194 309 216 269L229 354 245 414 77 414 92 353Z','#426c57')+
    path('M99 273Q155 336 216 273M76 412Q155 384 244 412','none','#bba473',4)+
    path('M134 213L133 260 117 277Q156 305 194 277L178 260 177 213Z','#e5bfa1')+
    path('M86 168Q65 62 157 70Q256 62 227 194L195 232 111 228Z','#b99c8c')+
    path('M100 126Q107 92 153 96Q203 89 214 133L209 188Q197 227 158 237Q114 224 101 187Z','#f0d2b8')+
    path('M84 155Q69 85 123 75Q163 51 205 82Q239 93 229 164L212 157 196 113Q174 139 131 142L143 112 111 131 103 166Z','#d9c2b0')+
    path('M103 115Q126 88 154 86M170 86Q201 86 215 121','none','#eddbca',6)+eyes+path(mouth,'none','#946f65',2.5)+ellipse(112,189,12,5,'#e6ab9f70')+ellipse(200,189,12,5,'#e6ab9f70')+braids+
    path('M71 329L91 329 86 344 75 344Z','#d8b977')+path('M223 329L242 329 240 344 226 344Z','#d8b977')+
    path('M62 341Q102 345 160 318L207 309Q224 320 209 334Q127 382 61 372Z','#e9c7aa','#334e42',2)+
    path('M58 336L92 344 87 378 55 370Z','#38624e','#bca273',3)+
    path('M163 318L179 312 190 341 174 349Z','#c8ab67','#937b49',2)+path('M170 317L181 317 187 329 179 339 169 330Z','#3fbd92','#235f50',2)+path('M174 319L180 319 181 328Z','#bef3cb')+
    rect(122,40,56,34,'#a88458',6,'#6b573e')+rect(128,42,44,21,'#d2b384',4)+rect(139,50,5,7,'#345c50',2)+rect(158,50,5,7,'#345c50',2)+path('M146 62L157 62','none','#6e684b',2)+
    rect(117,66,12,15,'#b29262',3)+rect(171,64,22,9,'#b29262',3)+path('M182 67L198 55','none','#d1b37f',7)+
    rect(191,13,112,72,'#9f8056',5,'#674f39')+rect(198,20,98,58,'#3d5b50',2)+path('M203 72L259 72','none','#7b9280',1)+
    `<text data-role="chalk" x="247" y="54" font-size="${safe.length>5?15:23}" text-anchor="middle" fill="#e9e5c9" font-family="sans-serif">${safe}</text>`+path('M275 69L285 69','none','#f4efd9',4),320,420);
}
export const sprites={};
for(const type of ['harvest_golem','craft_golem','combat_golem','mining_golem','mini_golem'])sprites[type]=golem(type);
for(const type of ['common_herb_patch','newflesh_herb_patch','spark_herb_patch'])sprites[type]=herb(type);
sprites.upright_tree=tree();sprites.sweetfruit_tree=tree(true);sprites.enrin=enrinSmall();
sprites.display_shelf=shelf();sprites.fine_shelf=shelf(true);
sprites.storage=svg(shadow+rect(23,48,83,61,'#b28c59',7,'#68583e')+rect(21,43,86,19,'#d9b67e',4,'#68583e')+path('M30 68L98 68M30 84L98 84M38 49L38 106M90 49L90 106','none','#886a47',3)+rect(59,56,12,20,'#7d7f5d',2)+ellipse(65,65,2,3,'#e8d19b'));
sprites.workbench=svg(shadow+rect(20,64,13,50,'#795a41',3)+rect(98,64,13,50,'#795a41',3)+rect(17,42,99,33,'#bc9766',5,'#715941')+path('M19 55L114 55','none','#e2c391',3)+rect(26,32,33,14,'#acb9ad',2)+path('M44 31L72 18 78 25 52 41Z','#c2a87b','#6c644e',2)+rect(84,26,17,17,'#7e8067',3)+rect(89,38,6,16,'#6f5440',2));
sprites.herb_fumigator=svg(shadow+rect(25,94,16,24,'#695b47',3)+rect(90,94,16,24,'#695b47',3)+rect(94,15,15,62,'#9d8970',4)+rect(91,14,22,9,'#ccb287',3)+path('M29 48Q64 25 100 48L105 91Q64 115 25 92Z','#a48158','#675e48')+ellipse(65,49,39,15,'#ddc295')+ellipse(65,49,29,9,'#708a61')+path('M40 59L43 87M54 65L55 91','none','#d3b47e',3)+rect(52,82,28,24,'#67543d',7)+path('M64 103Q49 99 63 85Q63 96 72 93Q79 106 64 103Z','#efa967')+path('M97 10Q86-3 99-10M108 5Q119-5 112-15','none','#dae3bd80',7));
sprites.mana_tower=svg(shadow+ellipse(64,105,42,16,'#748b7b')+ellipse(64,99,37,13,'#b3c3a2')+path('M49 98L52 53 76 53 80 98Z','#adba9f','#5c7f6e')+`<g transform="translate(17 -3) scale(.73)">${crystal('#b5e8dc')}</g>`+ellipse(64,54,27,8,'none')+path('M36 46Q30 63 67 64Q98 63 94 48','none','#d6d9a4',3)+ellipse(64,101,16,5,'#d6e7ad'));
sprites.mana_deposit=svg(shadow+path('M22 112L30 88 92 82 108 113Z','#849c8b')+`<g transform="translate(-5 11) scale(.9)">${crystal()}</g>`+`<g transform="translate(58 56) scale(.45)">${crystal('#d4debe')}</g>`);
sprites.order_board=svg(shadow+rect(58,56,11,58,'#806445',3)+rect(17,30,97,61,'#a58358',4,'#6c553c')+path('M13 33L65 9 118 33Z','#536e56','#41573f')+rect(32,42,31,35,'#e5d6ac',1)+rect(72,46,26,31,'#dbbf8d',1)+path('M38 51L57 51M38 57L54 57M78 56L91 56M78 62L87 62','none','#9e936d',2)+ellipse(47,42,2,2,'#8c6052'));
sprites.signpost=svg(shadow+rect(59,38,10,74,'#856c4a',2)+path('M16 31L91 31 110 47 91 62 16 62Z','#c1a16a','#786643')+path('M30 45L74 45M65 39L77 45 65 52','none','#526d51',3));
sprites.lake_shrine=svg(shadow+path('M35 111L29 48Q36 24 64 21Q92 24 99 48L94 111Z','#98b4a1','#627f73')+path('M64 41Q87 72 64 84Q43 76 64 41Z','#a6dbce','#648e83')+path('M42 98L85 98M39 105L88 105','none','#d1ddc0',3));
sprites.cave_gate=svg(shadow+path('M5 114L16 44 46 15 81 11 110 45 123 114Z','#718577','#4e675c',3)+path('M26 114L33 57 53 38 79 35 101 64 105 114Z','#233d38')+path('M37 109L43 67 63 51 82 64 89 110Z','#172e2e')+path('M15 42L43 54 47 17M82 15L76 37 109 44','none','#a5b394',4)+leaf(24,61,.65,'#5d8a63',35));
sprites.dropped_items=svg(shadow+path('M34 107Q18 79 44 57L40 46 87 46 82 60Q110 80 94 108Z','#c5aa7f','#84714f')+path('M43 62L84 62M49 77Q39 88 46 98','none','#ead6ac',4)+path('M62 62L60 86 70 78','none','#705f44',3));
for(const ice of [false,true])sprites[ice?'icewater_pouch':'springwater_pouch']=svg(shadow+path('M25 100Q8 69 37 58Q37 22 63 21Q84 29 90 54Q116 66 105 99Q72 121 25 100Z',ice?'#b3d4d0':'#72bab1','#447f80')+path('M34 81Q23 67 46 64Q44 38 64 34Q57 55 66 68Z','#d5efda85')+ellipse(48,84,5,5,'#295b61')+ellipse(79,84,5,5,'#295b61')+path('M60 96Q65 92 70 96','none','#295b61',2)+(ice?path('M52 24L63 6 79 28 90 24 94 48Z','#e5efd9','#8dbabb'):'') );
sprites.springwater_king=svg(shadow+path('M14 111Q1 72 32 54Q30 23 63 21Q101 21 99 56Q126 79 116 112Z','#65b4ab','#336e73')+path('M23 89Q19 65 47 63Q38 39 62 35Q54 58 65 74Z','#b1dfcc99')+path('M31 33L21 9 46 19 63 2 82 18 107 10 98 36Z','#c6b77b','#72846b')+path('M39 73L55 78M76 78L92 72','none','#275b60',5)+path('M57 96L73 96','none','#34676a',3)+path('M21 107Q47 95 68 108Q91 114 107 104','none','#b5d6b3',3));
sprites.stone_fist=svg(shadow+path('M18 106L17 59 32 45 34 28 54 23 66 30 84 23 106 34 115 64 109 107Z','#98aaa0','#5e7870',4)+path('M18 67L39 70 42 38M60 31L64 72 87 70 84 30M107 39L103 70M39 70L45 92 87 91 91 70','none','#c2cbb5',4)+path('M20 92L43 94M87 92L109 92','none','#678479',3));
sprites.merchant=svg(shadow+path('M35 107L38 57 87 57 98 110Z','#b3805e','#6e6348')+ellipse(64,47,20,23,'#d7ba92')+path('M38 39Q34 13 63 10Q85 9 94 39L108 46 21 46Z','#748965','#536e56')+path('M45 26L87 26','none','#c1ae6b',5)+rect(51,45,4,5,'#52634e',2)+rect(73,45,4,5,'#52634e',2)+path('M57 61Q66 69 76 61','none','#836b4f',3)+rect(87,66,29,39,'#7d9b6f',8,'#577657')+path('M45 72L80 100','none','#d6b788',6));
sprites.customer=svg(shadow+path('M43 65L30 112 99 112 86 65Z','#b3aa7f','#817e5e')+ellipse(63,45,23,25,'#dab994')+path('M37 40Q39 12 65 16Q92 15 89 48L75 30 51 38Z','#716b56')+rect(50,46,4,6,'#52634e',2)+rect(74,46,4,6,'#52634e',2)+rect(85,81,26,26,'#a17f57',5));
export function itemIcon(id,color='#a8b893') {
  if(id.endsWith('_core'))return svg(core(color));
  if(id.endsWith('_herb'))return herb(id==='newflesh_herb'?'newflesh_herb_patch':id==='spark_herb'?'spark_herb_patch':'common_herb_patch');
  if(id==='wood')return svg(path('M28 91L48 30 83 36 108 63 87 109Z','#bc935f','#785d40')+ellipse(57,87,31,24,'#dfbf88')+ellipse(57,87,20,15,'none')+path('M37 89Q53 69 72 83Q82 102 54 103M71 72L88 42M80 86L103 61','none','#a58054',3));
  if(id==='mana_crystal')return svg(crystal());
  if(id.endsWith('_book'))return svg(rect(29,24,69,86,'#637d66',6,'#475e50')+path('M39 26L39 108M45 34L89 34M45 101L89 101','none','#d2bf89',3)+path('M65 49L82 67 65 85 50 67Z',id==='mana_book'?'#b6dacc':'#a2bb78'));
  if(id==='wooden_sword')return svg(path('M38 93L77 20 92 16 91 32 53 102Z','#d2b379','#795f45')+path('M30 87L63 106M39 97L30 113','none','#665844',8)+path('M54 81L84 29','none','#e9d8a2',3));
  if(id==='wooden_club')return svg(path('M44 107L75 47','none','#9e7a4e',10)+path('M47 45L64 19 103 41 87 67Z','#c5a576','#7b6347')+path('M61 33L91 51','none','#ead3a0',4));
  if(id==='wooden_shield')return svg(path('M31 28L64 16 99 29 96 85 64 112 33 85Z','#b59a6a','#6b6349',5)+path('M64 24L64 99M40 41L87 41','none','#d9c78c',5)+ellipse(64,64,12,12,'#769582'));
  if(id==='springwater_drop')return svg(path('M63 15Q17 67 31 94Q60 129 93 99Q116 76 63 15Z','#8acbc2','#508d8c')+path('M54 53Q32 86 52 97','none','#dcf2d7',7));
  if(id==='sweetfruit')return svg(ellipse(64,77,32,33,'#d9a769')+leaf(66,47,.7,'#83a570',40)+path('M64 49L67 30','none','#7d7651',5));
  if(id==='king_token')return svg(ellipse(64,70,38,38,'#cbb77a')+path('M42 63L39 45 55 51 64 39 75 51 91 44 86 68Z','#688f7b')+path('M44 79L84 79','none','#907f51',4));
  return svg(ellipse(64,105,37,9,'#43665730')+path('M27 94Q22 47 44 35Q67 25 88 39Q106 63 100 96Q67 116 27 94Z',color,'#718977',3)+path('M39 66Q39 43 57 44','none','#ffffff80',7)+ellipse(76,88,8,5,'#ffffff55'));
}
export const tileColors={grass:'#92aa7d',path:'#c9bd91',floor:'#d0bb90',water:'#77b3aa',shore:'#bac7a0',rock:'#697f6a',cave:'#516762',void:'#243c34',wall:'#667b60'};
export function tileSvg(type) {
 const c=tileColors[type]||tileColors.grass;
 return svg(rect(0,0,64,64,c)+(type==='water'?path('M7 18Q17 23 28 18M35 42Q47 47 59 42','none','#d6e7c470',2):type==='floor'?path('M0 16H64M0 32H64M0 48H64M20 0V16M44 16V32M19 32V48M44 48V64','none','#947d552d',1):type==='grass'?path('M13 44l2-5 3 5M43 17l2-4 3 4M49 54l2-3','none','#60885d50',2):path('M9 17h4M42 36h5M27 54h3','none','#365e3f25',2)),64,64);
}
