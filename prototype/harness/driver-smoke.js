// driver-smoke.js /abs/slice.html [shotDir] : step 18 driver sheet and d20 checks.
// Title background pick; races give XP and Hype; the garage holds Go racing until tonight's off-track event is done (or skipped);
// every answer in all three events (press scrum, sponsor dinner, contract talk) shows labeled modifiers, the DC, the roll needed and
// five-tier odds that match a brute-force count, and every result shows "what would have helped"; Hype buys advantage or a reroll;
// contract talk sets a salary that is paid in the next race; banked Momentum reaches the green flag; image map unlocks a signature answer;
// attribute points spend on the sheet; Feel hides the odds; saves from before step 18 still load; phone layout has no horizontal scroll.
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const errs=[];
 const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 for(const [W,H] of [[1280,900],[390,844]]){
  const p=await b.newPage({viewport:{width:W,height:H}});p.on('pageerror',e=>errs.push(e.message));
  const shot=async n=>{if(shots)await p.screenshot({path:`${shots}/${n}-${W}.png`,fullPage:false});};
  const load=async()=>{await p.goto('file://'+html);await p.waitForTimeout(350);};
  const click=async sel=>{await p.click(sel);await p.waitForTimeout(80);};
  const noScroll=async id=>p.evaluate(id=>document.documentElement.scrollWidth>innerWidth+1||document.getElementById(id).scrollWidth>document.getElementById(id).clientWidth+1,id);
  // force the next d20s: list of faces (1-20), then back to Math.random
  const force=async faces=>p.evaluate(f=>{const q=[...f],mr=window.__mr||(window.__mr=Math.random);Math.random=()=>{if(q.length){const v=q.shift();if(!q.length)Math.random=mr;return (v-1)/20+0.001;}return mr();};},faces);
  const fin=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let k=0;
   while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}
   window.__gfdHeadless=false;g.saveSeason();return g.G.results[g.G.results.length-1];});
  const goRace=async()=>{await click('#g-go');if(await p.isVisible('#q-skip'))await click('#q-skip');await click('#q-race');};
  // every option on screen: odds match a brute-force count, roll needed matches, attribute line labeled
  const checkOpts=async()=>p.evaluate(()=>{const g=window.__gfd,ev=g.G.event,r=g.evRound(ev),bad=[];
   for(const o of r.opts){if(o.auto)continue;const b=g.chkBuild(o,ev,false),el=document.querySelector(`.evopt[data-o="${o.id}"]`);
    if(!el){bad.push('no card '+o.id);continue;}
    let c={crit:0,succ:0,mixed:0,fail:0,fumble:0};const N=b.mode==='flat'?1:2;
    for(let a=1;a<=20;a++)for(let z=1;z<=(N===2?20:1);z++){const k=N===1?a:b.mode==='adv'?Math.max(a,z):Math.min(a,z);c[g.chkTier(k,b.mod,b.dc)]+=1/(N===1?20:400);}
    for(const k in c)if(Math.abs(c[k]-b.odds[k])>1e-9)bad.push(o.id+' odds '+k);
    const sum=Object.values(b.odds).reduce((s,v)=>s+v,0);if(Math.abs(sum-1)>1e-9)bad.push(o.id+' sum');
    const t=el.innerText;const A=g.DATTR[g.DSKILL[o.skill].a].n;
    if(!t.includes(A))bad.push(o.id+' no attribute label');if(!t.includes('DC '+b.dc))bad.push(o.id+' no DC');
    if(!new RegExp('Roll needed '+(b.need>=20?'20':b.need+'\\+'),'i').test(t))bad.push(o.id+' roll needed');
    if(!el.querySelector('.oddbar'))bad.push(o.id+' no odds bar');if(!/Success/.test(t)||!/Failure/.test(t))bad.push(o.id+' no stakes');}
   return {n:r.opts.length,bad};});
  const pickAndWait=async(id,faces)=>{if(faces)await force(faces);await click(`[data-opt="${id}"]`);await p.waitForTimeout(2300);
   return p.evaluate(()=>{const ev=window.__gfd.G.event,h=document.getElementById('ev-help');return {tier:ev.cur&&ev.cur.R.tier,kept:ev.cur&&ev.cur.R.kept,help:h?h.innerText:'',items:h?h.querySelectorAll('li').length:0,res:(document.querySelector('.evres')||{}).innerText||''};});};

  await load();await p.evaluate(()=>localStorage.clear());await load();
  const bgs=await p.evaluate(()=>document.querySelectorAll('#t-bgs [data-bg]').length);if(bgs!==5)fail(W+': title backgrounds '+bgs);
  await click('#t-bgs [data-bg="hero"]');await click('#t-go');
  const d0=await p.evaluate(()=>{const G=window.__gfd.G;return {bg:G.drv.bg,attr:G.drv.attr,prof:G.drv.prof,lvl:G.drv.lvl,ev:G.event};});
  if(d0.bg!=='hero'||d0.attr.grit!==15||d0.prof.join()!=='media,endure'||d0.ev)fail('new driver '+JSON.stringify(d0));
  await click('#g-spons [data-sign]');
  // race 1 -> reward shows XP; the garage then holds a press scrum
  await goRace();const r1=await fin();
  const w=await p.evaluate(()=>({drv:(document.getElementById('w-drv')||{}).innerText||'',xp:window.__gfd.G.drv.xp}));
  if(!/\+25 XP/.test(w.drv)||w.xp<25)fail('reward XP '+JSON.stringify(w));
  if(!/Off track next: Press scrum/.test(w.drv))fail('reward does not name the next event');
  await click('#w-go');
  const g1=await p.evaluate(()=>({ev:window.__gfd.G.event&&window.__gfd.G.event.id,go:document.getElementById('g-go').disabled,panel:!!document.getElementById('g-ev-go')}));
  if(g1.ev!=='press'||!g1.go||!g1.panel)fail('garage before the press scrum '+JSON.stringify(g1));
  if(await noScroll('garage'))fail(W+': garage scroll');
  await p.evaluate(()=>document.getElementById('g-ev').scrollIntoView());await shot('garage-event');
  await click('#g-ev-go');
  const o1=await checkOpts();if(o1.n<3||o1.bad.length)fail('press options '+JSON.stringify(o1));
  if(await noScroll('event'))fail(W+': event screen scroll');
  await shot('press-options');
  // a forced miss: the result explains what would have helped
  const first=await p.evaluate(()=>window.__gfd.evRound(window.__gfd.G.event).opts[0].id);
  const m1=await pickAndWait(first,[6,6]);
  if(m1.tier==='succ'||m1.tier==='crit'||!/What would have helped/.test(m1.help)||!/Missed by/.test(m1.help)||m1.items<2)fail('miss explanation '+JSON.stringify(m1));
  await shot('press-miss');
  console.log(W,'press:',first,m1.tier,'·',m1.help.replace(/\n/g,' | ').slice(0,220));
  await click('#ev-next');
  const g2=await p.evaluate(()=>{const G=window.__gfd.G;return {done:G.event.done,go:document.getElementById('g-go').disabled,sum:document.getElementById('g-ev').innerText,log:G.drv.log.length};});
  if(!g2.done||g2.go||!g2.log)fail('after the press scrum '+JSON.stringify(g2));
  if(W!==1280){
   // phone: the other two events lay out without horizontal scroll
   for(const id of ['dinner','contract']){await p.evaluate(id=>{const g=window.__gfd;g.G.event=g.makeEvent(id);g.showEvent();},id);await p.waitForTimeout(100);if(await noScroll('event'))fail(W+': '+id+' scroll');await shot(id);}
   await p.close();continue;}

  // all three events: odds before, what would have helped after (forced miss), on every round
  for(const id of ['press','dinner','contract']){
   await p.evaluate(id=>{const g=window.__gfd;g.G.drv.hype=0;g.G.event=g.makeEvent(id);g.showEvent();},id);await p.waitForTimeout(100);
   for(let ri=0;ri<3;ri++){const st=await p.evaluate(()=>{const ev=window.__gfd.G.event;return ev.done?null:ev.ri;});if(st==null)break;
    const c=await checkOpts();if(c.bad.length)fail(id+' round '+st+' '+JSON.stringify(c.bad));
    const o=await p.evaluate(()=>window.__gfd.evRound(window.__gfd.G.event).opts.find(x=>!x.auto).id);
    const m=await pickAndWait(o,[3,3]);if(!/What would have helped/.test(m.help)||m.items<2)fail(id+' round '+st+' help '+JSON.stringify(m));
    await click('#ev-next');}
   const done=await p.evaluate(()=>window.__gfd.G.event.done&&!document.getElementById('garage').hidden);if(!done)fail(id+' did not finish in the garage');
  }
  // a made check reports its margin
  await p.evaluate(()=>{const g=window.__gfd;g.G.event=g.makeEvent('press');g.showEvent();});await p.waitForTimeout(100);
  {const o=await p.evaluate(()=>window.__gfd.evRound(window.__gfd.G.event).opts[0].id);const m=await pickAndWait(o,[19,19]);if(m.tier!=='succ'&&m.tier!=='crit')fail('forced 19 did not pass '+JSON.stringify(m));if(!/Made it by/.test(m.help))fail('made-it note '+m.help);
   await shot('press-made');await click('#ev-next');}
  // Hype: advantage before the roll (two dice) and a reroll after a miss
  await p.evaluate(()=>{const g=window.__gfd;g.G.drv.hype=2;g.G.event=g.makeEvent('dinner');g.showEvent();});await p.waitForTimeout(100);
  await click('#ev-hype');
  const hv=await p.evaluate(()=>{const g=window.__gfd,ev=g.G.event,o=g.evRound(ev).opts[1],b=g.chkBuild(o,ev,true),b0=g.chkBuild(o,ev,false);return {txt:document.querySelector(`.evopt[data-o="${o.id}"]`).innerText,mode:b.mode,up:b.odds.succ+b.odds.crit>b0.odds.succ+b0.odds.crit,id:o.id};});
  if(!/Spent 1 Hype/.test(hv.txt)||hv.mode!=='adv'||!hv.up)fail('hype advantage '+JSON.stringify(hv));
  const hm=await pickAndWait(hv.id,[2,4]);
  const hd=await p.evaluate(()=>({hype:window.__gfd.G.drv.hype,dice:document.querySelectorAll('#ev-dice .die').length,kept:window.__gfd.G.event.cur.R.kept}));
  if(hd.hype!==1||hd.dice!==2||hd.kept!==4)fail('advantage roll '+JSON.stringify(hd));
  await shot('dinner-advantage');
  if(!(await p.isVisible('#ev-reroll')))fail('no reroll button after a miss with Hype left');
  else{await force([18]);await click('#ev-reroll');await p.waitForTimeout(2300);
   const rr=await p.evaluate(()=>({hype:window.__gfd.G.drv.hype,R0:!!window.__gfd.G.event.cur.R0,tier:window.__gfd.G.event.cur.R.tier,txt:document.querySelector('.evroll').innerText}));
   if(rr.hype!==0||!rr.R0||!/First roll/.test(rr.txt))fail('reroll '+JSON.stringify(rr));}
  await click('#ev-next');
  // contract talk: read the room (reveal) -> ask for the ceiling (success) -> shake; salary is paid in the next race
  await p.evaluate(()=>{const g=window.__gfd;g.G.drv.hype=0;g.G.contract={per:0,clauses:[]};g.G.event=g.makeEvent('contract');g.showEvent();});await p.waitForTimeout(100);
  await pickAndWait('room',[17]);await click('#ev-next');
  const q2=await p.evaluate(()=>({q:document.querySelector('.evq').innerText,ceil:window.__gfd.G.event.ceil,rev:window.__gfd.G.event.revealed,adv:/know their number/.test(document.querySelector('.evopt[data-o="ceiling"]').innerText)}));
  if(!q2.rev||!q2.q.includes('$'+q2.ceil.toLocaleString('en-US'))||!q2.adv)fail('contract reveal '+JSON.stringify(q2));
  await pickAndWait('ceiling',[19,19]);await click('#ev-next');await shot('contract-close');
  await click('[data-opt="shake"]');
  const ct=await p.evaluate(()=>({per:window.__gfd.G.contract.per,done:window.__gfd.G.event.done}));
  if(ct.per!==q2.ceil||!ct.done)fail('contract salary '+JSON.stringify(ct)+' ceil '+q2.ceil);
  // banked Momentum reaches the green flag, salary lands in the payout
  await p.evaluate(()=>{window.__gfd.G.drv.next.mom=1;window.__gfd.showGarage();});
  await goRace();
  const mo=await p.evaluate(()=>({m:window.__gfd.race.momentum,left:window.__gfd.G.drv.next.mom,drv:window.__gfd.race.drvMom}));
  if(mo.drv!==1||mo.m<1||mo.left!==0)fail('banked momentum '+JSON.stringify(mo));
  const r2=await fin();if(r2.salary!==ct.per)fail('salary not paid '+r2.salary);
  const pay=await p.evaluate(()=>document.querySelector('#reward').innerText);if(!/Team salary/.test(pay))fail('no salary row');
  // image: a Cocky Rookie sees the signature answer and gets the in-character +1 on Media
  await p.evaluate(()=>{const g=window.__gfd;g.G.drv.img={x:3,y:3};g.G.event=g.makeEvent('press');g.showEvent();});await p.waitForTimeout(100);
  const im=await p.evaluate(()=>{const g=window.__gfd,ev=g.G.event,r=g.evRound(ev),s=r.opts.find(o=>o.sig==='rookie'),b=g.chkBuild(s,ev,false);return {img:g.imageOf(),sig:!!document.querySelector('.evopt.sig'),line:b.L.some(l=>l.k==='image')};});
  if(im.img!=='rookie'||!im.sig||!im.line)fail('image '+JSON.stringify(im));
  await shot('press-signature');
  await click('[data-opt="trash"]');await p.waitForTimeout(2300);
  const im2=await p.evaluate(()=>window.__gfd.G.drv.img);if(im2.x!==4||im2.y!==4)fail('tone did not move the image '+JSON.stringify(im2));
  await click('#ev-next');
  // driver sheet: 6 attributes, 11 skills, spend a point with a preview
  await p.evaluate(()=>{window.__gfd.G.drv.pts=1;window.__gfd.showGarage();});await click('#g-dsheet');
  const sh=await p.evaluate(()=>({a:document.querySelectorAll('#mbox .attr').length,s:document.querySelectorAll('#mbox .skrow').length,map:!!document.querySelector('#mbox .imgmap'),prev:(document.querySelector('#mbox .attr .prev')||{}).innerText||'',pres:window.__gfd.G.drv.attr.presence}));
  if(sh.a!==6||sh.s!==11||!sh.map||!/\+1 →/.test(sh.prev))fail('sheet '+JSON.stringify(sh));
  await shot('driver-sheet');
  await click('#mbox [data-attr-up="presence"]');
  const sp=await p.evaluate(()=>({pres:window.__gfd.G.drv.attr.presence,pts:window.__gfd.G.drv.pts}));if(sp.pres!==sh.pres+1||sp.pts!==0)fail('spend point '+JSON.stringify(sp));
  await p.keyboard.press('Escape');
  // Feel hides the odds but keeps the modifiers
  await p.evaluate(()=>{const g=window.__gfd;g.G.info='feel';g.G.event=g.makeEvent('dinner');g.showEvent();});await p.waitForTimeout(100);
  const fe=await p.evaluate(()=>({bars:document.querySelectorAll('.evopt .oddbar').length,math:document.querySelectorAll('.evopt .evmath').length}));
  if(fe.bars||!fe.math)fail('feel mode '+JSON.stringify(fe));
  // skip: walk away costs what it says
  const f0=await p.evaluate(()=>{const g=window.__gfd;g.G.info='full';g.G.event=g.makeEvent('press');g.showGarage();return g.G.drv.fans;});
  await click('#g-ev-walk');const f1=await p.evaluate(()=>({fans:window.__gfd.G.drv.fans,done:window.__gfd.G.event.done,go:document.getElementById('g-go').disabled}));
  if(f1.fans!==Math.max(0,f0-3)||!f1.done||f1.go)fail('walk '+JSON.stringify(f1));
  // old save from before step 18: no driver, no contract, no event
  await p.evaluate(()=>{const g=window.__gfd;g.saveSeason();const s=g.readSave(),G=s.G;delete G.drv;delete G.contract;delete G.event;G.reward=null;G.inRace=null;G.snap=null;localStorage.setItem('gfd-season-v1',JSON.stringify(s));});
  await load();await click('#t-cont');
  const old=await p.evaluate(()=>({g:!document.getElementById('garage').hidden,drv:window.__gfd.G.drv&&window.__gfd.G.drv.bg,go:document.getElementById('g-go').disabled}));
  if(!old.g||old.drv!=='sim'||old.go)fail('old save '+JSON.stringify(old));
  await p.close();
 }
 console.log(process.exitCode?'driver smoke FAILED':'driver smoke ok');console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;
 await b.close();})();
