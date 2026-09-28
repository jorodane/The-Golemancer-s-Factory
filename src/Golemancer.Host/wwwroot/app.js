import {sprites, portrait, itemIcon, tileColors, tileSvg} from './art.js';
const $=id=>document.getElementById(id), esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const uri=s=>'data:image/svg+xml;charset=utf-8,'+encodeURIComponent(s), val=(o,k,d=0)=>o?.values?.[k]??d;
let meta, state, started=false, selected='', modalType='', building='', follow=true, toastUntil=0, lastMessage='', dialogueId='', panelAt=0, lastCrew='', lastInv='', moveAt=0;
let camera={x:10,y:27}, zoom=48, mouse={x:0,y:0,tx:0,ty:0}, form={item:'',quantity:1,mode:'exact',recipeQuantity:1,recipeMode:'craft_single',failure:'',tab:'inventory'};
const images={}, tiles={}, itemImages={}, spriteSources={},portraitSources={};let targetMenu=[];
const actor=()=>state?.objects[state.controlledId], def=o=>meta.objects[o?.definitionId]||{}, kind=o=>def(o).kind, alive=o=>o&&!val(o,'dead');
const crew=()=>Object.values(state.objects).filter(o=>alive(o)&&kind(o)==='golem');
const inv=o=>Object.entries(o?.inventory||{}).filter(([,n])=>n>0);
const itemName=id=>meta?.items[id]?.name||id;
const icon=id=>itemImages[id]?.src||uri(itemIcon(id,meta?.items[id]?.color));
const sprite=id=>spriteSources[id]||uri(sprites[id]||sprites.storage);
const img=(id,cls='')=>`<img class="${cls}" src="${esc(icon(id))}" alt="">`;
const invRows=o=>inv(o).map(([id,n])=>`<div class="inventory-row">${img(id)}<span>${esc(itemName(id))}</span><b>${n}</b></div>`).join('')||'<p class="hint">보관함이 비어 있어.</p>';
const costText=c=>Object.entries(c||{}).map(([id,n])=>`${itemName(id)} ${n}`).join(' · ');
function imageOf(source,fallback){const i=new Image();if(fallback)i.onerror=()=>{i.onerror=null;i.src=fallback;};i.src=source;return i;}
function portraitMarkup(mood,chalk){let source=portraitSources[mood];if(source){const doc=new DOMParser().parseFromString(source,'image/svg+xml');const text=doc.querySelector('[data-role="chalk"]');if(text){text.textContent=chalk;text.setAttribute('font-size',chalk.length>5?'15':'23');}source=new XMLSerializer().serializeToString(doc);}else source=portrait(mood,chalk);return `<img src="${uri(source)}" alt="에메랄드 팔찌를 찬 엔린과 칠판에 감정을 그리는 머리 위 미니골렘">`;}
async function api(path,body){const response=await fetch('/api/'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json'},...(body===undefined?{}:{body:JSON.stringify(body)})});if(!response.ok){let m;try{m=(await response.json()).message}catch{}throw Error(m||`서버 응답 ${response.status}`)}return response.headers.get('content-type')?.includes('json')?response.json():null;}
function toast(text,warning=false){if(!text)return;$('toast').textContent=text;$('toast').className='show'+(warning?' warning':'');toastUntil=performance.now()+4500;}
async function command(action,options={}){if(!started||!state)return;if(state.dialogues.length){toast('엔린의 이야기를 먼저 들어줘.');return;}try{const result=await api('command',{actorId:state.controlledId,action,failure:form.failure,...options});if(result.message)toast(result.message,!result.ok);await poll();panelAt=0;return result;}catch(e){toast(e.message,true);}}
function select(id){selected=id;form.item='';form.tab='inventory';targetMenu=[];renderPanel();api('menu/'+encodeURIComponent(id)).then(m=>{if(selected===id){targetMenu=m;renderPanel();}}).catch(()=>{});}
function closePanel(){selected='';$('targetPanel').hidden=true;}
function currentQuest(){return Object.values(meta.quests).find(q=>!state.completedQuests.includes(q.id)&&(!q.requires||state.completedQuests.includes(q.requires)));}
function goalHtml(q){return q.goals.map(g=>{const n=val(state,g.key),done=n>=g.amount;return `<div class="goal ${done?'done':''}"><span class="check">${done?'✓':'◇'}</span>${esc(g.label)}<span class="count">${Math.min(g.amount,Math.floor(n))} / ${g.amount}</span></div>`}).join('');}
function updateHUD(){
 const a=actor();if(!a)return;
 $('gold').textContent=Math.floor(val(state,'gold')).toLocaleString();$('reputation').textContent=val(state,'reputation').toFixed(1);
 const day=Math.floor(val(state,'calendarSeconds')/180), phases=['봄의 낮','봄의 밤','여름의 낮','여름의 밤','가을의 낮','가을의 밤','겨울의 낮','겨울의 밤'];
 $('calendar').textContent=`${phases[Math.floor(day/15)%8]} · ${day%15+1}일`;
 const q=currentQuest();$('questNumber').textContent=`${state.completedQuests.length} / 12`;
 $('returnButton').hidden=state.mapId!=='cave_entrance';
 $('questName').textContent=q?.name||'다음 이야기의 문턱';$('questDesc').textContent=q?.description||'만찬의 오솔길을 마쳤어. 공방으로 돌아가 자동화와 생산을 계속할 수 있어.';$('questGoals').innerHTML=q?goalHtml(q):'<div class="goal done">✓ 깊은 돌의 입구 개방</div>';
 $('placeName').textContent=state.mapId==='cave_entrance'?'깊은 돌의 입구':a.x<=15&&a.y>=20?'엔린의 공방':a.x>=39&&a.y<=21?'샘물의 왕의 호수':a.x>=25&&a.y<=22?'샘물 숲':'만찬의 오솔길';
 const crewKey=crew().map(o=>[o.id,o.definitionId,o.recording?.steps.length,o.playback?.status,Math.round(val(o,'mana')),o.id===a.id].join('/')).join('|');
 if(crewKey!==lastCrew){lastCrew=crewKey;$('crewList').innerHTML=crew().map(o=>`<button class="crew-card ${o.id===a.id?'active':''}" data-crew="${o.id}"><img src="${sprite(o.definitionId)}" alt=""><div><strong>${esc(o.name)}</strong><small>${o.recording?'행동 녹화 중':o.playback?esc(o.playback.status):val(o,'mana')<=0?'수동 · 효율 50%':'직접 조종 가능'}</small></div><span class="crew-state">${o.playback?'↻':o.id===a.id?'◇':''}</span></button>`).join('');}
 $('actorName').textContent=a.name;$('healthText').textContent=`${Math.max(0,Math.ceil(val(a,'health')))}/${val(a,'maxHealth')}`;$('manaText').textContent=`${Math.floor(val(a,'mana'))}/${val(a,'maxMana',100)}`;
 $('healthFill').style.width=`${Math.max(0,val(a,'health')/val(a,'maxHealth',100)*100)}%`;$('manaFill').style.width=`${val(a,'mana')/val(a,'maxMana',100)*100}%`;
 $('actorStatus').textContent=a.recording?`● 녹화 중 · ${a.recording.steps.length}단계`:a.playback?a.playback.status:a.work?`${meta.actions[a.work.request.action]?.name||'작업'} · ${Math.round(a.work.done/a.work.total*100)}%`:a.path.length?'이동 중':val(a,'mana')<=0?'마력 없음 · 수동 효율 50%':'마력 가동 · 효율 100%';
 $('recordButton').classList.toggle('recording',!!a.recording);$('recordButton').querySelector('span').textContent=a.recording?'종료':'녹화';$('playButton').classList.toggle('playing',!!a.playback);$('playButton').querySelector('span').textContent=a.playback?'정지':'반복';
 const inventoryKey=a.id+JSON.stringify(a.inventory)+a.data.weapon+a.data.shield+val(a,'slots');
 if(inventoryKey!==lastInv){lastInv=inventoryKey;$('actorBadge').innerHTML=`<img src="${sprite(a.definitionId)}" alt="${esc(a.name)}">`;
 const slots=[];for(const [id,n]of inv(a)){let left=n;const max=meta.items[id]?.stack||50;while(left>0&&slots.length<30){const amount=Math.min(left,max);slots.push(`<button class="slot" data-item="${id}" title="${esc(itemName(id))} ${amount}개${a.data.weapon===id||a.data.shield===id?' · 장착 중':''}">${img(id)}<span class="qty">${amount}</span></button>`);left-=amount;}}
 for(let i=slots.length;i<Math.min(12,val(a,'slots',def(a).slots));i++)slots.push(`<span class="slot empty"><span class="slot-index">${i+1}</span></span>`);$('inventory').innerHTML=slots.join('');}
 $('tilePosition').textContent=`${a.x}, ${a.y}`;
 const boss=state.objects['springwater-king'];$('bossbar').hidden=!state.flags.includes('boss_engaged');$('location').hidden=!$('bossbar').hidden;if(boss)$('bossHealth').style.width=`${Math.max(0,val(boss,'health')/val(boss,'maxHealth')*100)}%`;
 const d=state.dialogues[0];$('dialogue').hidden=!started||!d;if(d&&d.id!==dialogueId){dialogueId=d.id;$('portrait').innerHTML=portraitMarkup(d.mood,d.chalk);$('speaker').textContent=d.speaker;$('dialogueText').textContent=d.text;}
 if(!d)dialogueId='';
 const message=state.messages.at(-1);if(message&&message.time+message.text!==lastMessage){lastMessage=message.time+message.text;toast(message.text,message.kind==='warning');}
 if(selected&&performance.now()>panelAt&&!$('targetPanel').contains(document.activeElement))renderPanel();
}
function actionButton(action,label,options={},extra=''){return `<button class="panel-action ${extra}" data-action="${action}" data-options="${esc(JSON.stringify(options))}">${esc(label)}<span>→</span></button>`;}
function fields(){return `<div class="form-row"><label>수량</label><input id="transferQuantity" type="number" min="0" max="9999" value="${form.quantity}" aria-label="운반 수량"><select id="transferMode" aria-label="운반 방식"><option value="exact" ${form.mode==='exact'?'selected':''}>정해진 수량</option><option value="fill" ${form.mode==='fill'?'selected':''}>목표 수량까지</option><option value="all" ${form.mode==='all'?'selected':''}>가능한 전부</option></select></div>`;}
function transferUI(t){const ids=[...new Set([...inv(actor()).map(x=>x[0]),...inv(t).map(x=>x[0])])];if(!ids.includes(form.item))form.item=ids[0]||'';return `<div class="section-title">물건 옮기기</div><div class="form-row"><select id="transferItem" aria-label="옮길 물건">${ids.map(id=>`<option value="${id}" ${form.item===id?'selected':''}>${esc(itemName(id))} · 나 ${actor().inventory[id]||0} / 대상 ${t.inventory[id]||0}</option>`).join('')}</select></div>${fields()}<div class="form-row"><button data-transfer="give" ${!ids.length?'disabled':''}>내보내기 →</button><button data-transfer="take" ${!ids.length?'disabled':''}>← 가져오기</button></div><p class="hint">‘목표 수량까지’는 받는 쪽의 재고를 채워. 한 칸에는 같은 물건만 쌓을 수 있어.</p>`;}
function recipesUI(t){return `<div class="section-title">제작 예약</div><p class="hint">${t.definitionId==='workbench'?'제작 골렘이 가진 재료로 만들어.':'재료와 목재 연료를 시설에 넣어줘. 예약 후에는 다른 일을 해도 돼. 목재 1개 = 열 100.'}</p><div class="form-row"><input id="recipeQuantity" type="number" min="1" max="99" value="${form.recipeQuantity}" aria-label="제작 수량"><select id="recipeMode" aria-label="제작 방식">${[['craft_single','한 개 만들기'],['craft_count','수량만큼 만들기'],['craft_until','목표 재고까지']].map(([v,n])=>`<option value="${v}" ${form.recipeMode===v?'selected':''}>${n}</option>`).join('')}</select></div>${Object.values(meta.recipes).filter(r=>r.facility===t.definitionId).map(r=>{let locked=r.unlock&&!state.flags.includes(r.unlock);return `<div class="recipe-card"><h3>${esc(r.name)}</h3><small>${esc(costText(r.inputs))}${t.definitionId==='herb_fumigator'?` · 열 ${r.work}`:''}</small><button data-craft="${r.id}" ${locked?'disabled':''}>${locked?'레시피북 필요':'제작 시작'}</button></div>`}).join('')}`;}
function renderPanel(){
 const t=state?.objects[selected],a=actor(),panel=$('targetPanel');if(!t||!alive(t)){closePanel();return;}panelAt=performance.now()+1200;panel.hidden=false;
 let html=`<button class="close" data-close-panel aria-label="대상 닫기">×</button><div class="panel-head"><img src="${sprite(t.definitionId)}" alt=""><div><h2>${esc(t.name)}</h2><small>타일 ${t.x}, ${t.y} · ${def(t).width} × ${def(t).height}</small></div></div>`;
 const actions=def(t).actions||[];
 if(kind(t)==='resource'){
 html+=`<p class="hint">${val(t,'depleted')?'다시 자랄 때까지 기다리는 중이야.':`${itemName(t.data.yield)}을 얻을 수 있어.`}</p>`;
 for(const id of actions)html+=actionButton(id,meta.actions[id]?.name||id,{targetId:t.id});
 }else if(['monster','boss','boss_part'].includes(kind(t))){html+=`<p class="hint">내구도 ${Math.max(0,Math.ceil(val(t,'health')))} / ${val(t,'maxHealth')}<br>약점: ${val(t,'weakSlash',1)>val(t,'weakCrush',1)?'베기 · 목제 검':'타격 · 목제 망치'}</p>`+actionButton('attack','공격하기',{targetId:t.id})+actionButton('roll','구르기',{x:val(a,'facingX',1),y:val(a,'facingY')});if(state.flags.includes('boss_engaged'))html+=actionButton('retreat','호수에서 후퇴',{},'danger');
 }else if(t.definitionId==='merchant'){
 html+='<p class="hint">핵은 공방에서 함께 보관해. 책은 구매하는 즉시 배워.</p>';
 for(const [id,price]of Object.entries({harvest_core:20,craft_core:35,combat_core:75,jelly_book:12,mana_book:65,healing_jelly:22,wood:3}))html+=`<div class="inventory-row">${img(id)}<span>${esc(itemName(id))}</span><button data-buy="${id}">${price} G</button></div>`;
 }else if(t.definitionId==='enrin'){html+=`<p class="hint">“움직이는 건 골렘이면 충분하잖아…”</p>`+actionButton('talk','엔린에게 진행 힌트 듣기',{targetId:t.id})+'<button class="panel-action" data-open="assembly">골렘 조립 <span>＋</span></button>';
 }else if(kind(t)==='golem'){
 html+=`<p class="hint">내구도 ${Math.ceil(val(t,'health'))} · 마력 ${Math.floor(val(t,'mana'))}<br>${t.playback?esc(t.playback.status):'직접 조종 중'}</p>`;
 if(t.id!==a.id)html+=actionButton('select','이 골렘 조종하기',{targetId:t.id});
 html+=invRows(t);if(t.id!==a.id)html+=transferUI(t);else html+='<button class="panel-action" data-open="equipment">장비 · 강화 <span>↗</span></button>';
 }else if(t.definitionId==='order_board'){html+='<button class="panel-action" data-open="orders">주문 확인 <span>↗</span></button>'+actionButton('expand_shop',`상점 확장 · ${val(state,'shopTier',1)*50}G`)+`<p class="hint">현재 규모 ${val(state,'shopTier',1)} / 4<br>일반 판매로 평판을 얻으면 대량 주문이 찾아와.</p>`;
 }else if(t.definitionId==='cave_gate'){html+=`<p class="hint">${state.flags.includes('chapter2_unlocked')?'돌 틈 사이에서 차가운 마력이 흘러나와. 다음 챕터의 입구가 열렸어.':'공방의 자동화와 미니 골렘 운반을 마치면 입구가 열려.'}</p>`+actionButton(state.mapId==='cave_entrance'?'return_cave':'enter_cave',state.mapId==='cave_entrance'?'공방 쪽으로 돌아가기':'깊은 돌의 입구로',{targetId:t.id,option:state.mapId==='cave_entrance'?'return':''});
 }else if(t.definitionId==='lake_shrine'){html+=`<p class="hint">첫 주문을 마치면 도전할 수 있어.<br>목제 망치, 목제 검, 방패와 회복 젤리를 준비해. 주먹이 재생되기 전에 물 몸통을 공격해야 해.</p>`+actionButton('challenge','샘물의 왕에게 도전',{targetId:t.id});
 }else if(kind(t)==='drop'){html+=invRows(t)+actionButton('pickup','가능한 물건 모두 줍기',{targetId:t.id})+transferUI(t);
 }else if(kind(t)==='facility'){
 if(t.definitionId==='mana_tower')html+=`<p class="hint">저장 마력 ${Math.floor(val(t,'reserve'))}<br>무색 마나 수정 1개 = 마력 1,000</p>`+actionButton('fuel_tower','마나 수정 1개 넣기',{targetId:t.id})+fields()+`<button class="panel-action" data-charge>마력 충전 <span>◇</span></button>`;
 else{
 if(t.definitionId==='herb_fumigator')html+=`<p class="hint">${esc(t.data.status||'대기 중')} · 남은 열 ${Math.floor(val(t,'heat'))}<br>생산 예약 ${t.production.length}개${t.production.length?` · ${itemName(meta.recipes[t.production[0].recipeId]?.output)}`:''}</p>`;
 if(t.definitionId==='workbench')html+=recipesUI(t);else{html+='<div class="tabs"><button data-tab="inventory" class="'+(form.tab==='inventory'?'selected':'')+'">보관 · 운반</button>'+(t.definitionId==='herb_fumigator'?'<button data-tab="recipes" class="'+(form.tab==='recipes'?'selected':'')+'">제작</button>':'')+'</div>';html+=form.tab==='recipes'?recipesUI(t):invRows(t)+transferUI(t);}
 }
 if(actions.includes('dismantle'))html+=`<div class="divider"></div>`+actionButton('dismantle','시설 해체',{targetId:t.id},'danger');
 }else for(const id of actions)html+=actionButton(id,meta.actions[id]?.name||id,{targetId:t.id});
 function menuTree(entries){return entries.map(e=>e.actionId?`<button class="panel-action" data-menu-action="${esc(e.actionId)}">${esc(e.label)}<span>→</span></button>`:`<details><summary>${esc(e.label)}</summary>${menuTree(e.children)}</details>`).join('');}
 if(targetMenu.length)html+=`<details class="object-menu"><summary>행동 목록</summary>${menuTree(targetMenu)}</details>`;
 panel.innerHTML=html;
}
async function openModal(type){if(!state)return;modalType=type;if(type==='menu')await api('pause/true',{});$('modal').hidden=false;renderModal();}
async function closeModal(){const was=modalType;modalType='';$('modal').hidden=true;if(was==='menu'&&started)await api('pause/false',{});}
function renderModal(){
 const a=actor();let h='';
 if(modalType==='menu')h=`<div class="eyebrow">THE GOLEMANCER’S FACTORY</div><h2>잠깐, 쉬어가자.</h2><p>공방의 시간도 잠시 멈췄어.</p><div class="menu-buttons"><button data-resume>계속하기</button><button data-save>진행 상황 저장</button><button data-load="manual">직접 저장한 게임 불러오기</button><button data-load="autosave">자동 저장 불러오기</button><button data-open="help">조작과 공방 안내</button></div>`;
 if(modalType==='build')h=`<div class="eyebrow">BUILD YOUR WORKSHOP</div><h2>타일에 공방을 그려봐.</h2><p>제작 골렘을 선택하고 시설을 골라줘. 설치 위치는 상점의 나무 바닥 위야. 현재 시설 한도 ${4+val(state,'shopTier',1)*4}개.</p>${Object.values(meta.objects).filter(d=>d.data.buildable==='true').map(d=>`<button class="build-card" data-build="${d.id}" ${d.data.unlock&&!state.flags.includes(d.data.unlock)?'disabled':''}><img src="${sprite(d.id)}" alt=""><div><strong>${esc(d.name)}</strong><small>${esc(costText(d.cost))}</small></div><span>${d.width}×${d.height}</span></button>`).join('')}<div class="divider"></div>${actionButton('expand_shop',`상점 확장 · ${val(state,'shopTier',1)*50}G`)}`;
 if(modalType==='assembly')h=`<div class="eyebrow">ENRIN’S LITTLE HELPERS</div><h2>골렘을 깨울 시간.</h2><p>일반 핵은 행상인이나 모험에서 얻어. 골렘이 부서져도 핵은 즉시 회수돼. 미니 핵은 직접 만들 수 있어.</p>${Object.values(meta.objects).filter(d=>d.kind==='golem').map(d=>{const n=(state.treasury[d.data.core]||0)+(a.inventory[d.data.core]||0);return `<button class="build-card" data-assemble="${d.id}" ${!n?'disabled':''}><img src="${sprite(d.id)}" alt=""><div><strong>${esc(d.name)}</strong><small>${d.slots}칸 보관함 · 핵 ${n}개 보유</small></div><span>조립 →</span></button>`}).join('')}`;
 if(modalType==='orders')h=`<div class="eyebrow">THE ORDER BOARD</div><h2>공방에 도착한 편지.</h2>${state.orders.filter(o=>!o.delivered).map(o=>`<div class="routine"><h3>${esc(o.name)}</h3><p>${esc(costText(o.requirements))}</p><p>보상 ${o.reward}G · 평판 +${o.reputation}</p>${actionButton('order',o.accepted?'주문 납품하기':'주문 수락하기',{targetId:'board',item:o.id,option:o.accepted?'deliver':'accept'})}</div>`).join('')||'<p>상점 규모 2, 평판 2 이상이면 첫 주문이 도착해. 진열대에 물건을 올려보자.</p>'}<p class="hint">납품은 조종 중인 골렘과 상점의 나무 상자에 보관한 물건을 사용해.</p>`;
 if(modalType==='journal')h=`<div class="eyebrow">CHAPTER 01 · ${state.completedQuests.length}/12</div><h2>만찬의 오솔길</h2>${Object.values(meta.quests).map(q=>`<div class="journal-entry ${state.completedQuests.includes(q.id)?'complete':''}"><h3>${state.completedQuests.includes(q.id)?'✓ ':''}${esc(q.name)}</h3><p>${esc(q.description)}</p>${goalHtml(q)}</div>`).join('')}`;
 if(modalType==='equipment')h=`<div class="eyebrow">${esc(a.name)}</div><h2>작은 준비, 더 큰 모험.</h2><p>장비는 보관함에 둔 채로 장착해. 무기를 바꾸면 약점에 맞춰 공격할 수 있어.</p>${inv(a).filter(([id])=>id.startsWith('wooden_')).map(([id])=>actionButton('equip',`${itemName(id)} ${a.data.weapon===id||a.data.shield===id?'· 장착 중':''}`,{item:id})).join('')||'<p class="hint">목공 작업대에서 제작 골렘으로 장비를 만들어줘.</p>'}<h3>골렘 강화 · 각각 40G</h3>${[['battery','마력 용량 +50'],['storage','보관함 +2칸'],['armor','방어력 +2 · 내구도 +20']].map(([id,name])=>actionButton('upgrade_golem',`${name} (${val(a,'upgrade.'+id)}/3)`,{option:id})).join('')}<h3>전투 행동</h3>${actionButton('mode',`모드 전환 · 현재 ${a.data.mode==='combat'?'전투':'일상'}`)}${actionButton('guard','주변 경호 · 30초',{quantity:30})}`;
 if(modalType==='routines')h=`<div class="eyebrow">TEACH ONCE. DO IT AGAIN.</div><h2>골렘의 행동 기록</h2><p>R로 녹화를 시작하고 충전 → 채집 → 운반을 해봐. 다시 R을 누르면 저장돼. T로 원점에서 반복해. 전투 모드에서는 행동 간격도 기억해.</p><div class="form-row"><label>새 행동의 실패 처리</label><select id="failureMode">${[['','액션의 기본값'],['stop','중단'],['skip','건너뛰기'],['retry','재시도']].map(([v,n])=>`<option value="${v}" ${form.failure===v?'selected':''}>${n}</option>`).join('')}</select></div>${Object.values(state.recordings).map(r=>`<div class="routine"><h3>${esc(r.name)}</h3><p class="hint">원점 ${r.origin.x}, ${r.origin.y} · ${r.combat?'전투 타이밍 기록':'일상 작업 순서'}</p><ol>${r.steps.slice(0,30).map(s=>`<li>${esc(meta.actions[s.request.action]?.name||s.request.action)}${s.request.item?' · '+esc(itemName(s.request.item)):''}${s.request.quantity>1?' ×'+s.request.quantity:''}</li>`).join('')}</ol>${actionButton('play','선택한 골렘으로 반복',{item:r.id})}</div>`).join('')||'<p class="hint">아직 저장한 녹화가 없어.</p>'}${actionButton('wait','녹화에 5초 기다리기 추가',{quantity:5})}`;
 if(modalType==='help')h=`<div class="eyebrow">A GUIDE TO A LAZIER LIFE</div><h2>공방 사용 설명서</h2><div class="help-grid"><kbd>클릭 / WASD</kbd><span>빈 타일로 이동. 약초·나무·적을 누르면 접근해서 행동해.</span><kbd>E</kbd><span>가까운 대상과 상호작용. 시설을 누르면 보관·제작 창이 열려.</span><kbd>Tab</kbd><span>다른 골렘을 직접 조종해. 조종할 골렘의 반복은 멈춰.</span><kbd>R / T</kbd><span>행동 녹화 시작·종료 / 반복 시작·정지. 충전을 기록에 넣어줘.</span><kbd>B / I</kbd><span>타일 건설 / 장비와 강화. 건설·제작은 제작 골렘의 일이야.</span><kbd>Space / 1 / 2</kbd><span>구르기 / 생명력 젤리 / 마나 젤리.</span><kbd>F / 휠</kbd><span>골렘을 따라가기 / 확대·축소. 미니맵을 눌러 주변을 살펴봐.</span><kbd>Esc</kbd><span>작업 창 닫기 / 일시정지와 저장. 작업 취소는 X.</span></div><h3>처음이라면</h3><p>공방 동쪽 널린초를 수확해 진열대에 넣어. 손님이 사가면 핵을 구입하고 엔린에게 조립을 맡길 수 있어. 왼쪽 일지가 다음 할 일을 알려줄 거야.</p><h3>연료와 물류</h3><p>마력이 없는 골렘도 직접 조종하면 절반 속도로 일해. 자동 반복은 마력이 없으면 정지해. 훈증기는 시설 안의 재료와 목재를 쓰고, 목공 작업대는 제작 골렘의 재료를 써.</p><h3>샘물의 왕</h3><p>먼저 돌 주먹 두 개를 망치로 부숴. 빠진 물 아래로 들어가 검으로 몸통을 공격해. 바닥의 붉은 예고를 보고 피하고, 회복 젤리와 방패를 준비해. 실패하면 핵으로 다시 조립할 수 있어.</p><h3>저장</h3><p>자동 저장은 게임 시간 30초마다, 직접 저장은 메뉴에서 할 수 있어. 대사와 일시정지 중에는 세계의 시간이 멈춰.</p>`;
 $('modalContent').innerHTML=h;
}

const canvas=$('world'),ctx=canvas.getContext('2d'),mini=$('minimap').getContext('2d');let W=innerWidth,H=innerHeight,dpr=devicePixelRatio||1;
function resize(){W=innerWidth;H=innerHeight;dpr=Math.min(2,devicePixelRatio||1);canvas.width=W*dpr;canvas.height=H*dpr;ctx.setTransform(dpr,0,0,dpr,0,0);}addEventListener('resize',resize);resize();
function screen(x,y){return {x:(x-camera.x)*zoom+W/2,y:(y-camera.y)*zoom+(H+35)/2};}
function world(x,y){return {x:(x-W/2)/zoom+camera.x,y:(y-(H+35)/2)/zoom+camera.y};}
const positions={};
function drawSprite(id,x,y,width,height,alpha=1){const im=images[id];if(!im?.complete||!im.naturalWidth)return;ctx.globalAlpha=alpha;ctx.drawImage(im,x,y,width,height);ctx.globalAlpha=1;}
function drawWorld(now){
 requestAnimationFrame(drawWorld);if(!state||!meta)return;
 const a=actor();if(follow&&a){camera.x+=(a.x+.5-camera.x)*.10;camera.y+=(a.y+.2-camera.y)*.10;}
 ctx.clearRect(0,0,W,H);ctx.fillStyle='#78946b';ctx.fillRect(0,0,W,H);
 const bounds={x:Math.max(0,Math.floor(camera.x-W/zoom/2)-2),y:Math.max(0,Math.floor(camera.y-H/zoom/2)-3),x2:Math.min(state.map.width,Math.ceil(camera.x+W/zoom/2)+2),y2:Math.min(state.map.height,Math.ceil(camera.y+H/zoom/2)+3)};
 for(let y=bounds.y;y<bounds.y2;y++)for(let x=bounds.x;x<bounds.x2;x++){
   const type=state.map.tiles[y*state.map.width+x],p=screen(x,y),seed=(x*73+y*137)%19,im=tiles[type];
   if(im?.complete&&im.naturalWidth)ctx.drawImage(im,Math.floor(p.x),Math.floor(p.y),Math.ceil(zoom)+1,Math.ceil(zoom)+1);else{ctx.fillStyle=tileColors[type]||'#92aa7d';ctx.fillRect(p.x,p.y,zoom+1,zoom+1);}
   if(type==='grass'){if(seed<5){ctx.globalAlpha=.045;ctx.fillStyle=seed%2?'#19392d':'#fff4c6';ctx.fillRect(p.x,p.y,zoom+1,zoom+1);ctx.globalAlpha=1;}if(seed===6){ctx.fillStyle='#d6d9a0';ctx.beginPath();ctx.ellipse(p.x+zoom*.73,p.y+zoom*.68,2.1,1.4,0,0,7);ctx.fill();ctx.fillStyle='#dce4b4';ctx.beginPath();ctx.ellipse(p.x+zoom*.62,p.y+zoom*.57,1.6,1.6,0,0,7);ctx.fill();}}
   if(type==='water'){ctx.strokeStyle='#e1f0d45a';ctx.lineWidth=1;ctx.beginPath();const v=Math.sin(now/1500+x+y)*3;ctx.moveTo(p.x+7+v,p.y+zoom*.73);ctx.quadraticCurveTo(p.x+16+v,p.y+zoom*.78,p.x+26+v,p.y+zoom*.72);ctx.stroke();}
   if(type==='rock'){ctx.fillStyle='#81977b';ctx.beginPath();ctx.moveTo(p.x+4,p.y+zoom);ctx.lineTo(p.x+zoom*.2,p.y+zoom*.2);ctx.lineTo(p.x+zoom*.7,p.y+zoom*.1);ctx.lineTo(p.x+zoom,p.y+zoom*.75);ctx.closePath();ctx.fill();ctx.strokeStyle='#a1b08b';ctx.beginPath();ctx.moveTo(p.x+zoom*.2,p.y+zoom*.25);ctx.lineTo(p.x+zoom*.65,p.y+zoom*.16);ctx.stroke();}
 }
 // Shop footprint is a saved tile region; the arch and rug are visual dressing.
 const shop=screen(3,21);ctx.strokeStyle='#788c6666';ctx.lineWidth=3;ctx.strokeRect(shop.x-2,shop.y-2,zoom*12+4,zoom*12+4);
 const rug=screen(7,29);ctx.fillStyle='#85977265';ctx.fillRect(rug.x,rug.y,zoom*3,zoom*2);ctx.strokeStyle='#e3c89477';ctx.lineWidth=1.5;ctx.strokeRect(rug.x+5,rug.y+5,zoom*3-10,zoom*2-10);
 for(const o of Object.values(state.objects).filter(alive).sort((a,b)=>(a.y+def(a).height)-(b.y+def(b).height)||a.x-b.x)){
   if(o.x<bounds.x-3||o.x>bounds.x2+2||o.y<bounds.y-3||o.y>bounds.y2+2)continue;
   const d=def(o),g=kind(o)==='golem';let pos=positions[o.id]||(positions[o.id]={x:o.x,y:o.y});pos.x+=(o.x-pos.x)*.28;pos.y+=(o.y-pos.y)*.28;
   if(Math.abs(pos.x-o.x)>3||Math.abs(pos.y-o.y)>3){pos.x=o.x;pos.y=o.y;}
   const p=screen(pos.x,pos.y),dw=(d.width||1)*zoom,dh=(d.height||1)*zoom;
   if(o.id===state.controlledId&&started){ctx.strokeStyle='#f2e1ac';ctx.lineWidth=2;ctx.beginPath();ctx.ellipse(p.x+zoom/2,p.y+zoom*.79,zoom*.45,zoom*.22,0,0,Math.PI*2);ctx.stroke();ctx.strokeStyle='#456e5366';ctx.strokeRect(p.x+1,p.y+1,zoom-2,zoom-2);}
   if(selected===o.id){ctx.fillStyle='#eff3bc33';ctx.fillRect(p.x,p.y,dw,dh);ctx.strokeStyle='#e4e4ad';ctx.lineWidth=1.5;ctx.strokeRect(p.x,p.y,dw,dh);}
   if(val(o,'depleted')){ctx.fillStyle='#64865388';ctx.beginPath();ctx.ellipse(p.x+zoom/2,p.y+zoom*.65,zoom*.2,zoom*.07,0,0,7);ctx.fill();continue;}
   const bounce=g&&o.path.length?Math.sin(now/95)*1.4:kind(o)==='monster'?Math.sin(now/500+o.x)*1.5:0;
   const largeTree=o.definitionId.endsWith('_tree');const width=largeTree?zoom*1.8:dw*1.16,height=largeTree?zoom*2.3:Math.max(dh*1.12,width*1.08);
   drawSprite(o.definitionId,p.x+(dw-width)/2,p.y+dh-height+bounce,width,height,val(o,'invulnerableUntil')>state.time ? .65 : 1);
   if(g&&o.recording){ctx.fillStyle='#cc6f60';ctx.beginPath();ctx.arc(p.x+zoom*.84,p.y-zoom*.18,4,0,7);ctx.fill();}
   if(g&&o.playback){ctx.fillStyle='#eef0bc';ctx.font='18px sans-serif';ctx.textAlign='center';ctx.fillText('↻',p.x+zoom*.82,p.y-zoom*.12);}
   if(g&&o.data.weapon){ctx.strokeStyle=o.data.weapon==='wooden_club'?'#98724b':'#dcc291';ctx.lineWidth=4;ctx.beginPath();ctx.moveTo(p.x+zoom*.86,p.y+zoom*.67);ctx.lineTo(p.x+zoom*1.0,p.y+zoom*.23);ctx.stroke();}
   if(o.work){ctx.fillStyle='#355441';ctx.fillRect(p.x,p.y-zoom*.35,zoom,4);ctx.fillStyle='#e3d094';ctx.fillRect(p.x,p.y-zoom*.35,zoom*o.work.done/o.work.total,4);}
   if(val(o,'health')<val(o,'maxHealth')&&val(o,'maxHealth')>0){ctx.fillStyle='#394f3dcc';ctx.fillRect(p.x,p.y-zoom*.27,dw,4);ctx.fillStyle=g?'#d6c191':'#b9786d';ctx.fillRect(p.x,p.y-zoom*.27,dw*Math.max(0,val(o,'health')/val(o,'maxHealth')),4);}
   if(o.production.length){const r=meta.recipes[o.production[0].recipeId],fraction=o.production[0].progress/(r?.work||1);ctx.fillStyle='#4c694d';ctx.fillRect(p.x,p.y+dh-4,dw,4);ctx.fillStyle='#e1be7f';ctx.fillRect(p.x,p.y+dh-4,dw*fraction,4);}
 }
 if(a?.path.length){ctx.strokeStyle='#edf1c975';ctx.setLineDash([2,7]);ctx.lineWidth=2;ctx.beginPath();const p=screen(a.x+.5,a.y+.5);ctx.moveTo(p.x,p.y);for(const t of a.path){const s=screen(t.x+.5,t.y+.5);ctx.lineTo(s.x,s.y);}ctx.stroke();ctx.setLineDash([]);}
 for(const e of state.effects){const p=screen(e.x+.5,e.y+.5),remaining=e.until-state.time;
 if(e.kind.startsWith('boss_warn')||e.kind==='telegraph'){
   ctx.fillStyle=`rgba(177,74,58,${.24+Math.sin(now/90)*.08})`;ctx.strokeStyle='#f2c191';ctx.lineWidth=2;
   if(e.kind==='boss_warn_2'){ctx.beginPath();ctx.moveTo(p.x,p.y-zoom*7);ctx.lineTo(p.x+zoom*7,p.y);ctx.lineTo(p.x,p.y+zoom*7);ctx.lineTo(p.x-zoom*7,p.y);ctx.closePath();ctx.fill();ctx.stroke();}
   else{const w=e.kind==='boss_warn_1'?11:3;ctx.fillRect(p.x-zoom*w/2,p.y-zoom*1.5,zoom*w,zoom*3);ctx.strokeRect(p.x-zoom*w/2,p.y-zoom*1.5,zoom*w,zoom*3);}
   ctx.font='bold 11px sans-serif';ctx.textAlign='center';ctx.fillStyle='#fff1ce';ctx.fillText(e.text,p.x,p.y-zoom*1.5-8);
 }else if(e.text){ctx.globalAlpha=Math.min(1,remaining*2);ctx.font='bold 13px sans-serif';ctx.textAlign='center';ctx.lineWidth=3;ctx.strokeStyle='#365c42';const y=p.y-zoom*.75-(1-Math.min(1,remaining))*18;ctx.strokeText(e.text,p.x,y);ctx.fillStyle=e.kind==='damage'?'#f5c0a3':e.kind==='mana'?'#c4eee3':'#f1e1b1';ctx.fillText(e.text,p.x,y);ctx.globalAlpha=1;}}
 if(building){const d=meta.objects[building],p=screen(mouse.tx,mouse.ty),valid=buildValid(mouse.tx,mouse.ty,d);ctx.fillStyle=valid?'#c9e49b55':'#d1817455';ctx.fillRect(p.x,p.y,zoom*d.width,zoom*d.height);ctx.strokeStyle=valid?'#edf0b2':'#f3ad99';ctx.lineWidth=2;ctx.strokeRect(p.x,p.y,zoom*d.width,zoom*d.height);drawSprite(building,p.x,p.y-zoom*.2,zoom*d.width,zoom*d.height+zoom*.2,.65);}
 else if(started&&!modalType&&$('dialogue').hidden){const p=screen(mouse.tx,mouse.ty);ctx.strokeStyle='#e6e2b688';ctx.lineWidth=1;ctx.strokeRect(p.x+2,p.y+2,zoom-4,zoom-4);}
 const phase=Math.floor(val(state,'calendarSeconds')/2700)%8;if(phase%2){ctx.fillStyle='#183c6845';ctx.fillRect(0,0,W,H);}else if(phase===6){ctx.fillStyle='#e6ebd026';ctx.fillRect(0,0,W,H);}
 if(performance.now()>toastUntil)$('toast').classList.remove('show');drawMinimap();
}
function drawMinimap(){mini.clearRect(0,0,192,120);for(let y=0;y<40;y++)for(let x=0;x<64;x++){mini.fillStyle=tileColors[state.map.tiles[y*64+x]]||'#385640';mini.fillRect(x*3,y*3,3,3);}for(const o of Object.values(state.objects)){if(!alive(o))continue;mini.fillStyle=kind(o)==='golem'?'#fff2bc':kind(o)==='boss'?'#bc7666':kind(o)==='facility'?'#647452':'transparent';mini.fillRect(o.x*3,o.y*3,3*(def(o).width||1),3*(def(o).height||1));}const a=actor();if(a){mini.strokeStyle='#fff7c8';mini.lineWidth=1.5;mini.strokeRect(a.x*3-2,a.y*3-2,7,7);}mini.strokeStyle='#ecedb577';mini.lineWidth=.7;mini.strokeRect((camera.x-W/zoom/2)*3,(camera.y-H/zoom/2)*3,W/zoom*3,H/zoom*3);}
function buildValid(x,y,d){if(x<3||y<21||x+d.width>15||y+d.height>33)return false;for(let yy=y;yy<y+d.height;yy++)for(let xx=x;xx<x+d.width;xx++){if(state.map.tiles[yy*64+xx]!=='floor')return false;for(const o of Object.values(state.objects)){if(!alive(o)||val(o,'depleted'))continue;const od=def(o);if((od.solid||od.kind==='golem')&&xx>=o.x&&xx<o.x+od.width&&yy>=o.y&&yy<o.y+od.height)return false;}}return true;}
function targetAt(x,y){return Object.values(state.objects).filter(o=>alive(o)&&kind(o)!=='customer'&&x>=o.x&&x<o.x+(def(o).width||1)&&y>=o.y&&y<o.y+(def(o).height||1)).sort((a,b)=>(kind(a)==='golem'?-1:0)-(kind(b)==='golem'?-1:0))[0];}
function interact(t,onlySelect=false){if(!t)return;select(t.id);if(onlySelect)return;
 if(kind(t)==='resource')command(t.definitionId.includes('tree')?'fell':t.definitionId==='mana_deposit'?'mine':'harvest',{targetId:t.id});
 else if(['monster','boss','boss_part'].includes(kind(t)))command('attack',{targetId:t.id});else if(kind(t)==='drop')command('pickup',{targetId:t.id});else if(kind(t)==='golem'&&t.id!==state.controlledId)command('select',{targetId:t.id}).then(()=>{follow=true;});}
canvas.addEventListener('pointermove',e=>{const p=world(e.clientX,e.clientY);mouse={x:e.clientX,y:e.clientY,tx:Math.floor(p.x),ty:Math.floor(p.y)};});
canvas.addEventListener('click',e=>{if(!started||modalType||state.dialogues.length)return;const p=world(e.clientX,e.clientY),x=Math.floor(p.x),y=Math.floor(p.y);if(building){command('build',{item:building,x,y}).then(r=>{if(r?.ok){building='';$('buildHint').hidden=true;}});return;}const t=targetAt(x,y);if(t)interact(t);else{closePanel();command('move',{x,y});}});
canvas.addEventListener('contextmenu',e=>{e.preventDefault();if(building){building='';$('buildHint').hidden=true;return;}const p=world(e.clientX,e.clientY);interact(targetAt(Math.floor(p.x),Math.floor(p.y)),true);});
canvas.addEventListener('wheel',e=>{e.preventDefault();zoom=Math.max(28,Math.min(80,zoom-e.deltaY*.025));},{passive:false});
$('minimap').addEventListener('click',e=>{const r=e.target.getBoundingClientRect();camera={x:(e.clientX-r.left)/r.width*64,y:(e.clientY-r.top)/r.height*40};follow=false;});
$('followButton').onclick=()=>{follow=true;};
document.addEventListener('input',e=>{const map={transferQuantity:'quantity',transferMode:'mode',transferItem:'item',recipeQuantity:'recipeQuantity',recipeMode:'recipeMode',failureMode:'failure'};if(map[e.target.id])form[map[e.target.id]]=e.target.type==='number'?Math.max(0,Math.min(9999,Number(e.target.value))):e.target.value;});
document.addEventListener('click',async e=>{
 const b=e.target.closest('button');if(!b)return;
 if(b.hasAttribute('data-close-panel'))closePanel();
 if(b.dataset.crew){await command('select',{targetId:b.dataset.crew});follow=true;closePanel();}
 if(b.dataset.action){const r=await command(b.dataset.action,JSON.parse(b.dataset.options||'{}'));if(modalType&&r?.ok)renderModal();if(selected)renderPanel();}
 if(b.dataset.open){if(modalType==='menu')await api('pause/false',{});openModal(b.dataset.open);}
 if(b.dataset.transfer){await command('transfer',{targetId:selected,item:form.item,quantity:form.quantity,mode:form.mode,option:b.dataset.transfer==='take'?'take':''});renderPanel();}
 if(b.hasAttribute('data-charge')){await command('charge',{targetId:selected,quantity:form.quantity,mode:form.mode});renderPanel();}
 if(b.dataset.craft){await command(form.recipeMode,{targetId:selected,item:b.dataset.craft,quantity:form.recipeQuantity});renderPanel();}
 if(b.dataset.buy){await command('buy',{targetId:selected,item:b.dataset.buy});renderPanel();}
 if(b.dataset.tab){form.tab=b.dataset.tab;renderPanel();}
 if(b.dataset.menuAction){const id=b.dataset.menuAction;if(id.startsWith('craft_')){form.tab='recipes';form.recipeMode=id;renderPanel();}else if(id==='assemble')openModal('assembly');else if(id==='order')openModal('orders');else if(id==='upgrade_golem')openModal('equipment');else if(['buy','transfer','charge'].includes(id)){form.tab='inventory';renderPanel();$('targetPanel').scrollTop=0;}else command(id,{targetId:selected});}
 if(b.dataset.build){building=b.dataset.build;await closeModal();follow=false;camera={x:9,y:27};$('buildHint').hidden=false;$('buildHint').textContent=`${meta.objects[building].name} · 바닥 타일 클릭으로 설치 · Esc 취소`;}
 if(b.dataset.assemble){await closeModal();await command('assemble',{item:b.dataset.assemble});}
 if(b.dataset.item){const id=b.dataset.item;if(['healing_jelly','mana_jelly','sweetfruit'].includes(id))command('consume',{item:id});else if(id.startsWith('wooden_'))command('equip',{item:id});else toast(`${itemName(id)} · ${meta.items[id]?.description||'시설이나 골렘을 선택해 물건을 옮길 수 있어.'}`);}
 if(b.hasAttribute('data-resume'))closeModal();
 if(b.hasAttribute('data-save')){await api('save',{});toast('진행 상황을 저장했어.');}
 if(b.dataset.load){try{await api('load/'+b.dataset.load,{});await closeModal();startView();await poll();}catch(ex){toast(ex.message,true);}}
});
async function startView(){started=true;$('titleScreen').hidden=true;$('hud').hidden=false;follow=true;lastCrew='';lastInv='';closePanel();}
$('newButton').onclick=async()=>{await api('new',{});await startView();await poll();};
$('continueButton').onclick=async()=>{try{await api('load/'+(meta.saves.includes('autosave')?'autosave':'manual'),{});await startView();await poll();}catch(e){toast(e.message,true);}};
$('dialogueNext').onclick=async()=>{await api('dialogue',{});await poll();};
$('pauseButton').onclick=()=>started?openModal('menu'):null;$('home').onclick=e=>{e.preventDefault();if(started)openModal('menu');};
for(const [id,type]of [['journalButton','journal'],['assemblyButton','assembly'],['buildButton','build'],['routinesButton','routines'],['helpButton','help']])$(id).onclick=()=>openModal(type);
 $('recordButton').onclick=()=>command('record');$('playButton').onclick=()=>command('play');$('actorBadge').onclick=()=>openModal('equipment');$('returnButton').onclick=()=>command('return_cave');
$('modalClose').onclick=closeModal;$('modal').querySelector('.modal-backdrop').onclick=closeModal;
addEventListener('keydown',async e=>{
 if(['INPUT','SELECT','TEXTAREA'].includes(document.activeElement.tagName))return;
 if(!started)return;
 if(e.key==='Escape'){if(building){building='';$('buildHint').hidden=true;}else if(modalType)await closeModal();else if(selected)closePanel();else openModal('menu');return;}
 if(state.dialogues.length){if(['Enter',' '].includes(e.key)){e.preventDefault();$('dialogueNext').click();}return;}
 if(modalType)return;
 const key=e.key.toLowerCase(),a=actor(),dirs={w:[0,-1],s:[0,1],a:[-1,0],d:[1,0],arrowup:[0,-1],arrowdown:[0,1],arrowleft:[-1,0],arrowright:[1,0]};
 if(dirs[key]){e.preventDefault();if(performance.now()-moveAt<180)return;moveAt=performance.now();follow=true;const [dx,dy]=dirs[key];command('move',{x:a.x+dx,y:a.y+dy});}
 else if(e.key===' '){e.preventDefault();command('roll',{x:val(a,'facingX',1),y:val(a,'facingY')});}
 else if(key==='tab'){e.preventDefault();const gs=crew();const next=gs[(gs.findIndex(g=>g.id===a.id)+1)%gs.length];if(next){await command('select',{targetId:next.id});follow=true;}}
 else if(key==='r'&&!e.repeat)command('record');else if(key==='t'&&!e.repeat)command('play');else if(key==='b')openModal('build');else if(key==='i')openModal('equipment');else if(key==='f')follow=true;
 else if(key==='1')command('consume',{item:'healing_jelly'});else if(key==='2')command('consume',{item:'mana_jelly'});else if(key==='x')command('cancel');else if(key==='?')openModal('help');
 else if(key==='e'){const t=Object.values(state.objects).filter(o=>alive(o)&&o.id!==a.id&&kind(o)!=='customer').sort((o,p)=>(Math.abs(o.x-a.x)+Math.abs(o.y-a.y))-(Math.abs(p.x-a.x)+Math.abs(p.y-a.y)))[0];interact(t);}
});
let polling=false;
async function poll(){if(polling)return;polling=true;try{state=await api('state');if(started)updateHUD();}catch(e){if(started)toast('공방 연결을 기다리고 있어. 실행 창이 열려 있는지 확인해줘.',true);}finally{polling=false;}}
async function boot(){try{meta=await api('meta');
 const assets=meta.assetManifest||{};
 for(const id of new Set([...Object.keys(sprites),...Object.keys(meta.objects)])){const fallback=uri(sprites[id]||sprites.storage),art=meta.objects[id]?.sprite;const source=art?.startsWith('/pack-art/')?art:assets.sprites?.[id]?'/assets/'+assets.sprites[id]:uri(sprites[art]||sprites[id]||sprites.storage);spriteSources[id]=source;images[id]=imageOf(source,fallback);}
 for(const [id,d]of Object.entries(meta.items)){const fallback=uri(itemIcon(id,d.color));itemImages[id]=imageOf(assets.items?.[id]?'/assets/'+assets.items[id]:fallback,fallback);}
 for(const id of Object.keys(tileColors)){const fallback=uri(tileSvg(id));tiles[id]=imageOf(assets.tiles?.[id]?'/assets/'+assets.tiles[id]:fallback,fallback);}
 await Promise.allSettled(Object.entries(assets.portraits||{}).map(async([mood,path])=>{const r=await fetch('/assets/'+path);if(r.ok)portraitSources[mood]=await r.text();}));
 $('titlePortrait').innerHTML=portraitMarkup('sleepy','… zZ');$('continueButton').hidden=!meta.saves.length;$('loading').textContent=`${meta.packs.length}개 객체팩 준비 완료 · 로컬 싱글플레이`;
 await poll();requestAnimationFrame(drawWorld);setInterval(poll,200);
 }catch(e){$('loading').textContent='게임 엔진에 연결하지 못했어. Start.bat 또는 start.sh로 실행해줘.';$('newButton').disabled=true;console.error(e);}}
boot();
