// career-ui-bot.js /abs/slice.html outDir [maxRaces=30] [speed=4] [persona=packrat] : step 24 feel-check prep. Plays a fresh career
// through the real screens (title, sponsor, off-track events, garage buys, deck builder, qualifying with Space taps, the race,
// rewards, season review, contract offers) like an attentive player, with screenshots and a log of UI issues (text off-screen,
// 'undefined'/'NaN' in visible text, page errors, a screen it can't leave). Slow: a full ladder at 4x is about an hour.
const {chromium}=require('playwright');const fs=require('fs');
const HTML=process.argv[2],OUT=process.argv[3]||'career-ui-run',MAXR=+(process.argv[4]||30),SPEED=+(process.argv[5]||4),ARCH=process.argv[6]||'packrat';
fs.mkdirSync(OUT,{recursive:true});const log=[];const L=(...a)=>{const s=a.join(' ');log.push(s);console.log(s);fs.appendFileSync(OUT+'/log.txt',s+'\n');};
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1600,height:900}});
const errs=[];p.on('pageerror',e=>{errs.push(e.message);L('PAGEERR',e.message);});p.on('console',m=>{if(m.type()==='error')L('CONSOLEERR',m.text());});
await p.goto('file://'+HTML);await p.waitForTimeout(500);await p.evaluate(()=>localStorage.clear());await p.goto('file://'+HTML);await p.waitForTimeout(500);
let shotN=0;const shot=async n=>{shotN++;const f=`${OUT}/${String(shotN).padStart(3,'0')}-${n}.png`;await p.screenshot({path:f});};
const scr=()=>p.evaluate(()=>{const s=[...document.querySelectorAll('.screen')].filter(e=>!e.hidden&&getComputedStyle(e).display!=='none').map(e=>e.id);return s.join(',');});
const text=id=>p.evaluate(id=>{const e=document.getElementById(id);return e?e.innerText.replace(/\s+/g,' '):'';},id);
const btns=id=>p.evaluate(id=>[...document.getElementById(id).querySelectorAll('button')].filter(x=>x.offsetParent&&!x.disabled).map(x=>(x.id?'#'+x.id:'')+JSON.stringify(x.dataset)+' '+x.innerText.replace(/\s+/g,' ').slice(0,50)),id);
const clickJS=sel=>p.evaluate(sel=>{const e=document.querySelector(sel);if(!e||e.disabled||!e.offsetParent)return false;e.click();return true;},sel);
// overflow checks per screen
const overflow=id=>p.evaluate(id=>{const e=document.getElementById(id);const bad=[];if(document.documentElement.scrollWidth>innerWidth+1)bad.push('page-x');
 e&&e.querySelectorAll('*').forEach(x=>{if(x.offsetParent&&x.scrollWidth>x.clientWidth+2&&getComputedStyle(x).overflowX==='visible'&&x.clientWidth>0&&!['svg','canvas'].includes(x.tagName.toLowerCase())){const r=x.getBoundingClientRect();if(r.right>innerWidth+1)bad.push((x.id||x.className||x.tagName)+'');}});
 const und=e?[...e.querySelectorAll('*')].filter(x=>x.children.length===0&&/undefined|NaN|\[object|null\b/.test(x.textContent)).map(x=>x.textContent.trim().slice(0,80)):[];return {bad:bad.slice(0,5),und:und.slice(0,5)};},id);
const chk=async id=>{const o=await overflow(id);if(o.bad.length||o.und.length)L('[UI-ISSUE]',id,JSON.stringify(o));};
// title
await shot('title');await chk('title');
await clickJS('[data-series="kart"]');await clickJS(`[data-arch="${ARCH}"]`);await clickJS('[data-bg="dirt"]');
await p.fill('#title input',"Bot Racer").catch(()=>{});
await clickJS('#t-go');await p.waitForTimeout(500);
let races=0,stuck=0,lastSig='',seenScreens={};
const raceRes=[];
while(races<MAXR){
 const s=await scr();const sig=s+'|'+(await text(s.split(',')[0])).slice(0,300);
 if(sig===lastSig){stuck++;if(stuck>6){L('[STUCK]',s,JSON.stringify(await btns(s.split(',')[0])));await shot('stuck');break;}}else stuck=0;lastSig=sig;
 const id=s.split(',')[0];
 if(!seenScreens[id+(await p.evaluate(()=>window.__gfd.G&&window.__gfd.G.series))]){seenScreens[id+(await p.evaluate(()=>window.__gfd.G&&window.__gfd.G.series))]=1;await shot('first-'+id);await chk(id);}
 if(id==='garage'){
  const G=await p.evaluate(()=>{const G=window.__gfd.G;return {money:G.money,series:G.series,season:G.season||(G.past?G.past.length+1:1),ri:G.raceIdx,sp:!!G.sponsor,offers:!!G.offers};});
  // sponsor: biggest per-race
  if(await p.$('#g-spons [data-sign]')){const t=await text('g-spons');L('[sponsor]',t.slice(0,400));await clickJS('#g-spons [data-sign]');await p.waitForTimeout(150);continue;}
  if(await clickJS('#g-ev-go')){L('[garage] off-track event');await p.waitForTimeout(400);continue;}
  // spend: cheapest buy while money > 4000 cushion
  let bought=[];for(let k=0;k<6;k++){const c=await p.evaluate(()=>{const G=window.__gfd.G;const bs=[...document.querySelectorAll('#garage [data-buy]')].filter(x=>!x.disabled&&x.offsetParent).map(x=>({k:x.dataset.buy,c:+x.dataset.cost})).sort((a,b)=>a.c-b.c);const x=bs[0];if(x&&G.money-x.c>=2500){document.querySelector(`#garage [data-buy="${x.k}"]`).click();return x.k;}return null;});if(!c)break;bought.push(c);await p.waitForTimeout(80);}
  if(bought.length)L('[garage-buy]',bought.join(','));
  // driver level-up points
  L(`[garage] S${G.season} race ${G.ri+1} ${G.series} $${G.money}`);
  await shot(`garage-s${G.season}-${G.series}-r${G.ri+1}`);
  if(!(await clickJS('#g-go'))){L('[garage] go disabled',JSON.stringify(await btns('garage')));await shot('garage-blocked');break;}
  await p.waitForTimeout(400);continue;}
 if(id==='event'){const t=await text('event');L('[event]',t.slice(0,500));
  // choose the visible option with the best printed odds
  const ch=await p.evaluate(()=>{const ev=document.getElementById('event');const opts=[...ev.querySelectorAll('button')].filter(x=>x.offsetParent&&!x.disabled);const n=opts.find(x=>/next|continue|done|back to|garage/i.test(x.innerText)&&!x.dataset.opt);
   const card=x=>{let e=x;for(let i=0;i<4&&e.parentElement;i++){e=e.parentElement;if(/Gets it/.test(e.innerText))return e;}return x;};
   const sc=x=>{const m=card(x).innerText.match(/Gets it\s*<?(\d+)%/);return m?parseInt(m[1]):-1;};const o=opts.filter(x=>x!==n&&x.dataset.opt).sort((a,b)=>sc(b)-sc(a))[0];
   if(o&&sc(o)>=0){const t=o.innerText.replace(/\s+/g,' ').slice(0,80);o.click();return 'pick '+t;} if(n){const t=n.innerText;n.click();return 'next '+t;} if(o){const t=o.innerText.replace(/\s+/g,' ').slice(0,80);o.click();return 'pick? '+t;} return null;});
  L('[event-act]',ch);await p.waitForTimeout(400);if(!ch){await shot('event-stuck');}continue;}
 if(id==='deckb'){await shot('deckb');const t=await text('deckb');L('[deckb]',t.slice(0,300));if(!(await clickJS('#d-done'))){L('[deckb] done disabled',JSON.stringify(await btns('deckb')));break;}await p.waitForTimeout(300);continue;}
 if(id==='crewhq'){if(!(await clickJS('#s-close')))await p.keyboard.press('Escape');await p.waitForTimeout(300);continue;}
 if(id==='quali'){
  const ph=await p.evaluate(()=>window.__gfd.QS&&window.__gfd.QS.phase);
  if(await p.$('#quali .qcard[data-qc]:not(.qnone)')&&!(await p.evaluate(()=>window.__gfd.QS&&window.__gfd.QS.card))){await clickJS('#quali .qcard[data-qc]:not(.qnone)');await p.waitForTimeout(100);}
  if(await clickJS('#q-go')){await p.waitForTimeout(100);
   const r=await p.evaluate(()=>new Promise(res=>{const g=window.__gfd,ev=t=>document.dispatchEvent(new KeyboardEvent(t,{code:'Space',key:' ',bubbles:true}));
    const tick=()=>{const Q=g.QS;if(!Q||Q.phase!=='run'){res(1);return;}const c=Q.pl.checks.find(c=>!c.res&&Q.time<c.s1);
     if(c&&Q.time>=c.s0){const x=(Q.time-c.s0)/(c.s1-c.s0);if(c._aim==null)c._aim=c.c+(Math.random()-0.5)*0.06;if(x>=c._aim-0.01&&!c.res&&!c._t){c._t=1;ev('keydown');ev('keyup');}}
     requestAnimationFrame(tick);};requestAnimationFrame(tick);}));
   await p.waitForTimeout(500);const q=await p.evaluate(()=>{const q=window.__gfd.G.qual;return q?{pos:q.pos,counts:q.counts,card:q.card}:null;});L('[quali]',JSON.stringify(q));await shot('quali-done');await chk('quali');
   await p.keyboard.press('Enter');await p.waitForTimeout(400);continue;}
  // after run: continue
  const done=await p.evaluate(()=>{const b=[...document.querySelectorAll('#quali button')].find(x=>x.offsetParent&&!x.disabled&&/race|go|continue|start/i.test(x.innerText)&&x.id!=='q-skip');if(b){const t=b.innerText;b.click();return t;}return null;});
  if(done){L('[quali-next]',done);await p.waitForTimeout(400);continue;}
  L('[quali?]',JSON.stringify(await btns('quali')));await p.keyboard.press('Enter');await p.waitForTimeout(400);continue;}
 if(id==='race'){races++;const res=await playRace(races);raceRes.push(res);continue;}
 if(id==='reward'){const t=await text('reward');L('[reward]',t.slice(0,1500));await chk('reward');await shot('reward');
  // season offers?
  if(await p.$('#reward [data-pick]')&&await p.$('#o-sign')){
   const pi=await p.evaluate(()=>{const l=window.__gfd.G.offers.list;let i=l.map((o,i)=>[(o.kind==='promote'?100:0)+o.r,i]).sort((a,b)=>b[0]-a[0])[0][1];return i;});
   await clickJS(`[data-neg="${pi}"]`);await p.waitForTimeout(150);await clickJS(`[data-pick="${pi}"]`);await p.waitForTimeout(150);await shot('offers-picked');L('[offers]',(await text('reward')).slice(0,1200));
   await clickJS('#o-sign');await p.waitForTimeout(300);if(await p.isVisible('#o-sign'))await clickJS('#o-sign');await p.waitForTimeout(400);continue;}
  const rel=await p.evaluate(()=>{const R=document.getElementById('reward');const c=R.querySelector('[data-card]:not(.taken),[data-relic]');if(c&&!window.__tk){window.__tk=1;c.click();return c.innerText.replace(/\s+/g,' ').slice(0,80);}return null;});
  if(rel){L('[reward-pick]',rel);await p.waitForTimeout(250);}
  const go=await p.evaluate(()=>{for(const id of ['w-go','e-next','e-new']){const e=document.getElementById(id);if(e&&e.offsetParent&&!e.disabled){const t=e.innerText;e.click();return id+':'+t;}}return null;});
  await p.evaluate(()=>{window.__tk=0;});
  L('[reward-go]',go);if(!go){L('[reward] no go',JSON.stringify(await btns('reward')));await shot('reward-stuck');break;}await p.waitForTimeout(500);continue;}
 if(id==='title'){L('[title] back at title?');await shot('title-again');break;}
 L('[unknown screen]',s,JSON.stringify(id?await btns(id):[]));await shot('unknown');await p.waitForTimeout(500);
}
L('RESULTS',JSON.stringify(raceRes));L('ERRS',JSON.stringify(errs));fs.writeFileSync(OUT+'/results.json',JSON.stringify(raceRes));await b.close();

async function playRace(rn){
 const info=await p.evaluate(S=>{const g=window.__gfd,r=g.race;r.speed=S;const G=g.G;return {trk:r.track.name,laps:r.track.laps,series:G.series,season:G.season||(G.past?G.past.length+1:1),ri:G.raceIdx+1,start:g.idx(g.meCar())+1};},SPEED);
 L(`=== RACE ${rn} ${info.series} S${info.season} R${info.ri} ${info.trk} ${info.laps} laps start P${info.start}`);await shot(`r${rn}-start`);await chk('race');
 let lastPhase='',t0=Date.now(),lastLogN=0,plays=0,fails=0,caut=0,midShot=0,pgShots=0;
 await p.evaluate(()=>{window.__log=[];['commentate','toast'].forEach(k=>{const o=window[k];if(!o||o.__w)return;window[k]=function(...a){try{window.__log.push([(window.__gfd.race?window.__gfd.race.t.toFixed(0):'-'),k,a.slice(0,1).map(x=>typeof x==='string'?x:'').join('')]);}catch(e){}return o.apply(this,a);};window[k].__w=1;});});
 while(true){await p.waitForTimeout(120);
  const st=await p.evaluate(()=>{const g=window.__gfd,r=g.race;if(!r)return {gone:true};const me=g.meCar();
   const o={t:r.t,phase:r.phase,beat:r._beatPaused,pos:g.idx(me)+1,lap:g.lapOf(me),tires:Math.round(me.tires),dmg:Math.round(me.damage),attempt:!!r.attempt,radio:r.radio?{kind:r.radio.kind,chance:r.radio.chance,line:r.radio.line}:null,pg:r.pg2?r.pg2.phase:null,pit:me.pit?me.pit.stage:null,plan:(g.myPlan()||{}).lap,pitReq:!!r.pitReq,dnf:me.dnf};
   const c=g.ctxFor(me);o.hand=r.hand.map(h=>{const cd=g.CARDS[h.id];const bl=g.cardBlock(cd,c);let od=null;try{od=g.computeOdds(h.id,c);}catch(e){}return {uid:h.uid,n:cd.n,block:bl||null,succ:od?Math.round(od.succ*100):null,cf:od&&od.cf!=null?Math.round(od.cf*100):null,wreck:od&&od.wreck!=null?Math.round(od.wreck*100):null};});return o;});
  if(st.gone)break;
  const nl=await p.evaluate(n=>window.__log.slice(n),lastLogN);lastLogN+=nl.length;for(const x of nl)if(/undefined|NaN/.test(x[2]))L('[TEXT-BUG]',x.join(' '));
  if(st.phase!==lastPhase){if(st.phase==='caution'){caut++;if(caut<=1)await shot(`r${rn}-caution`);}lastPhase=st.phase;}
  if(st.phase==='finished'||st.phase==='done'){await p.waitForTimeout(1200);await shot(`r${rn}-finish`);break;}
  if(Date.now()-t0>12*60*1000){L('TIMEOUT race');await shot(`r${rn}-timeout`);break;}
  if(!midShot&&st.lap>=Math.floor(info.laps/2)){midShot=1;await shot(`r${rn}-mid`);await chk('race');}
  if(st.pg==='plan'){if(pgShots++<1)await shot(`r${rn}-pitplan`);await p.evaluate(()=>{const g=window.__gfd,pg=g.race.pg2;try{pgAutoPlan(pg,true);}catch(e){}g.pgGo();});continue;}
  if(st.pg==='work'){const ready=await p.evaluate(()=>{const r=window.__gfd.race,pg=r.pg2;return pg&&pg.phase==='work'&&(r.t-pg.t0)>=pg.T+0.15;});if(ready)await p.evaluate(()=>window.__gfd.pgRelease());continue;}
  if(st.phase==='caution'){await p.evaluate(()=>{const g=window.__gfd,r=g.race,k=r.caution;if(!k||k.done||k._pol)return;k._pol=1;const me=g.meCar();const pick=me.damage>=25||me.tires<45?'four':me.tires<65?'two':'stay';const b=document.querySelector(`#pitbox [data-pit="${pick}"]`);if(b)b.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true}));});}
  if(st.radio&&st.radio.kind!=='advice'){const take=st.radio.kind==='buff'||(st.radio.chance>=55);await p.evaluate(t=>t?window.__gfd.acceptRadio():window.__gfd.ignoreRadio(),take);}
  if(st.phase==='green'&&!st.pit&&st.plan&&st.lap>=st.plan&&!st.pitReq)await p.evaluate(()=>window.__gfd.pitButton());
  if(!st.attempt&&st.hand.length){const pl=st.hand.filter(h=>!h.block&&h.succ!=null);let best=null,bs=-1;for(const h of pl){const sc=h.succ-(h.cf>=12?10:0)-(h.wreck>=5?15:0);if(sc>bs){bs=sc;best=h;}}
   if(best&&bs>=(st.beat?58:66)){plays++;await p.evaluate(u=>window.__gfd.playCard(u),best.uid);}else if(st.beat)await p.evaluate(()=>togglePause());}
  else if(st.beat&&!st.attempt)await p.evaluate(()=>togglePause());
 }
 const fin=await p.evaluate(()=>{const G=window.__gfd.G;const x=G.results[G.results.length-1];return x?{fin:x.finish,exp:x.expected,start:x.start,dnf:x.dnf,rank:window.__gfd.myRank(),money:G.money}:null;});
 L(`=== FINISH race ${rn}`,JSON.stringify(fin),'plays',plays,'cautions',caut);return {...info,...fin,plays,caut};}
})();
