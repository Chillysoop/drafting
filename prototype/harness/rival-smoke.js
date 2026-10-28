// rival-smoke.js /abs/slice.html [shotDir] : step 20 rivals.
// Six named rivals take fixed seats with their traits and a Grudge of 0; the garage lists them with an inbox; the rolled objectives
// carry one rival objective naming this week's rival, and the race uses the same driver. Wrecking that rival raises the Grudge,
// leaves a message in the inbox and a row on the reward screen, and the Grudge is still Warm at the next race, where it changes
// how they race you: passing odds on them drop, they block more and lean on you, the rival objective gets harder, and past Hot an
// attack from behind turns into payback (spin, damage, Grudge settles). Replies are d20 checks that move the Grudge.
// Saves from before step 20 get the rivals; phone layout has no horizontal scroll.
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
  const fin=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let k=0;
   while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}
   window.__gfdHeadless=false;g.saveSeason();return g.G.results[g.G.results.length-1];});
  const toGreen=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0;while(r.phase!=='green'&&k++<400){r.paused=false;g.update(0.05);}window.__gfdHeadless=false;return r.phase;});
  const goRace=async()=>{if(await p.isVisible('#g-ev-walk'))await click('#g-ev-walk');await click('#g-go');if(await p.isVisible('#q-skip'))await click('#q-skip');await click('#q-race');};

  await load();await p.evaluate(()=>localStorage.clear());await load();
  await click('#t-go');
  const r0=await p.evaluate(()=>{const g=window.__gfd,G=g.G;const rv=G.cars.filter(c=>c.rival);
   return {n:rv.length,names:rv.map(c=>c.name),seats:rv.map(c=>c.id),traits:rv.map(c=>c.trait===g.RIVALS[c.rival].trait),gr:Object.values(G.riv).map(s=>s.g),
    rows:document.querySelectorAll('#g-riv .rvrow').length,inbox:G.inbox.length,objs:G.nextObjectives,wk:G.rivWeek,wkName:G.rivWeek!=null&&G.cars[G.rivWeek].name,
    tag:[...document.querySelectorAll('#garage .objl')].some(e=>/rival/i.test(e.innerText)&&e.innerText.includes(G.cars[G.rivWeek].name))};});
  if(r0.n!==6||r0.seats.join()!=='1,4,8,10,14,16'||r0.traits.includes(false)||r0.gr.some(v=>v!==0)||r0.rows!==6||r0.inbox)fail('rivals at start '+JSON.stringify(r0));
  if(!r0.objs.some(o=>o.rv&&o.id==='rival')||!r0.tag)fail('rival objective '+JSON.stringify(r0));
  if(await noScroll('garage'))fail(W+': garage scroll');
  await p.evaluate(()=>document.getElementById('g-riv').scrollIntoView());await shot('garage-rivals');
  // the rival sheet explains the personality and what the Grudge does
  await click('#g-riv .rvrow');
  const sh=await p.evaluate(()=>document.getElementById('mbox').innerText);
  if(!/What their Grudge does right now/i.test(sh)||!/Races you like anyone else/.test(sh))fail('rival sheet '+sh.slice(0,200));
  await p.keyboard.press('Escape');
  await click('#g-spons [data-sign]');
  const o1=await p.evaluate(()=>window.__gfd.G.nextObjectives);
  if(o1.length!==4||!o1[3].rv)fail('objectives after signing '+JSON.stringify(o1));
  await goRace();await toGreen();
  // race 1: this week's rival is the garage's; wreck them
  const w=await p.evaluate(()=>{const g=window.__gfd,G=g.G,r=g.race,m=g.meCar(),rv=r.rival;const id=rv.id;
   const py=[...document.querySelectorAll('#pylon .pr')].some(e=>/RIVAL/.test(e.innerText));
   wreck(m,rv,'turns the rival around');return {same:id===G.rivWeek,id,g:g.grudge(id),key:rv.ref.rival,inc:r.rinc.map(x=>x.k),py};});
  const mul=await p.evaluate(k=>window.__gfd.RIVALS[k].mul,w.key);
  if(!w.same||Math.abs(w.g-Math.round(4*mul*4)/4)>1e-9||w.inc.join()!=='wreck')fail('wreck adds Grudge '+JSON.stringify(w));
  await shot('race-wreck');
  const res=await fin();
  const a1=await p.evaluate(id=>{const G=window.__gfd.G;return {g:G.riv[id].g,inbox:G.inbox.map(m=>[m.kind,m.rid]),panel:!!document.getElementById('w-riv'),txt:(document.getElementById('w-riv')||{}).innerText||''};},w.id);
  if(a1.g<3||!a1.inbox.some(([k,id])=>k==='wrecked'&&id===w.id)||!a1.panel||!/you wrecked them/.test(a1.txt))fail('after race 1 '+JSON.stringify({a1,rvWreck:res.rvWreck}));
  if(!res.rvWreck)fail('res.rvWreck not set');
  await p.evaluate(()=>document.getElementById('w-riv').scrollIntoView());await shot('reward-rivals');
  await click('#w-go');
  // race 2 objectives: the Warm rival is this week's rival, with the harder objective
  const o2=await p.evaluate(()=>{const G=window.__gfd.G;return {wk:G.rivWeek,objs:G.nextObjectives};});
  if(o2.wk!==w.id||!o2.objs.some(o=>o.rv&&o.id==='rclean'))fail('race 2 rival objective '+JSON.stringify(o2));
  // reply to the message: walk tonight's event first, then a d20 check (forced natural 20) moves the Grudge
  if(await p.isVisible('#g-ev-walk'))await click('#g-ev-walk');
  await p.evaluate(()=>document.getElementById('g-riv').scrollIntoView());await shot('inbox');
  const gBefore=await p.evaluate(id=>window.__gfd.grudge(id),w.id);
  await click(`#g-riv [data-rvreply]`);
  const evs=await p.evaluate(()=>({on:!document.getElementById('event').hidden,opts:[...document.querySelectorAll('#event [data-opt]')].map(b=>b.dataset.opt)}));
  if(!evs.on||!['sorry','racing','fire','leave'].every(o=>evs.opts.includes(o)))fail('reply screen '+JSON.stringify(evs));
  await shot('reply');
  await p.evaluate(()=>{window.__mr=Math.random;Math.random=()=>0.99;});
  await click('#event [data-opt="sorry"]');
  await p.evaluate(()=>{Math.random=window.__mr;});
  await p.waitForTimeout(1200);await shot('reply-roll');await click('#ev-next');
  const rp=await p.evaluate(id=>{const G=window.__gfd.G;const m=G.inbox.find(x=>x.rid===id&&x.kind==='wrecked');return {g:G.riv[id].g,reply:m&&m.reply,garage:!document.getElementById('garage').hidden};},w.id);
  if(!rp.garage||!rp.reply||rp.reply.tier!=='crit'||!(rp.g<gBefore))fail('reply '+JSON.stringify({rp,gBefore}));
  // put it back to Warm and race again: the Grudge changes how that driver races you
  await p.evaluate(id=>{window.__gfd.G.riv[id].g=5;window.__gfd.showGarage();},w.id);
  await goRace();await toGreen();
  const d=await p.evaluate(id=>{const g=window.__gfd,G=g.G,r=g.race,m=g.meCar(),rv=r.cars.find(c=>c.id===id),st=G.riv[id];
   const c=g.ctxFor(m);c.ahead=rv;c.behind=rv;const odds=v=>{st.g=v;return g.computeOdds('dive_bomb',c).read;};
   const def=v=>{st.g=v;return g.computeOdds('block',c).read;};
   const out={pass0:odds(0),pass5:odds(5),pass8:odds(8),def0:def(0),def8:def(8),block0:(st.g=0,g.rivalBlock(rv)),block5:(st.g=5,g.rivalBlock(rv)),lean0:(st.g=0,g.rivalLean(rv)),lean5:(st.g=5,g.rivalLean(rv)),pc5:(st.g=5,g.paybackChance(rv)),pc8:(st.g=8,g.paybackChance(rv)),resp:(st.g=-3,odds(-3))};
   // payback through the real AI move: the rival right behind you at Grudge 8, dice forced
   const mi=r.order.indexOf(m);r.order.splice(r.order.indexOf(rv),1);r.order.splice(r.order.indexOf(m)+1,0,rv);rv.dist=m.dist-0.002;rv.pit=null;m.pit=null;
   const mr=Math.random;Math.random=()=>0.001;st.g=0;r.rpaid={};const n0=r.rinc.length;g.autoMove(rv);const pay0=r.rinc.slice(n0).filter(x=>x.k==='payback').length;
   st.g=8;g.autoMove(rv);Math.random=mr;const pay8=r.rinc.filter(x=>x.k==='payback'&&x.id===id).length;
   return {...out,pay0,pay8,spin:m.spinning>0,gAfter:st.g,settle:g.RIVALS[rv.ref.rival].settle};},w.id);
  if(!(d.pass5<d.pass0&&d.pass8<d.pass5&&d.resp>d.pass0&&d.def8<d.def0))fail('odds vs a rival '+JSON.stringify(d));
  if(!(d.block5>d.block0&&d.lean5>d.lean0&&d.pc5===0&&d.pc8>0))fail('rival behaviour '+JSON.stringify(d));
  if(d.pay0!==0||d.pay8!==1||!d.spin||Math.abs(d.gAfter-(8-d.settle))>1e-9)fail('payback '+JSON.stringify(d));
  await shot('payback');
  const r2=await fin();
  const pm=await p.evaluate(id=>window.__gfd.G.inbox.filter(m=>m.rid===id).map(m=>m.kind),w.id);
  if(!pm.includes('payback'))fail('payback message '+JSON.stringify(pm));
  if(r2.rvPaid!==true)fail('rvPaid '+JSON.stringify({rvPaid:r2.rvPaid}));
  // old save: no rivals, no inbox
  await click('#w-go');
  await load();await p.evaluate(()=>{const g=window.__gfd,s=g.readSave();const G=s.G;delete G.riv;delete G.inbox;delete G.rivWeek;for(const c of G.cars){delete c.rival;}localStorage.setItem('gfd-season-v1',JSON.stringify(s));});
  await click('#t-cont');
  const old=await p.evaluate(()=>{const G=window.__gfd.G;return {riv:G.riv&&Object.keys(G.riv).length,rows:document.querySelectorAll('#g-riv .rvrow').length,g:!document.getElementById('garage').hidden};});
  if(old.riv!==6||old.rows!==6||!old.g)fail('old save '+JSON.stringify(old));
  if(await noScroll('garage'))fail(W+': garage scroll after races');
  await p.close();
 }
 console.log(process.exitCode?'rival smoke FAILED':'rival smoke ok');
 console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;
 await b.close();
})();
