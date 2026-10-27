// season-smoke.js /abs/slice.html [shotDir] : plays a 10-race season through the real screens (races fast-forwarded),
// reloads the page mid-season and mid-race to check save/continue, and checks that Ironman also restarts a race cut off mid-way (Q48).
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:900}});const errs=[];p.on('pageerror',e=>errs.push(e.message));
 const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 const shot=async n=>{if(shots)await p.screenshot({path:`${shots}/${n}.png`});};
 const load=async()=>{await p.goto('file://'+html);await p.waitForTimeout(400);};
 // fast-forward the current race (the green flag save already happened through the real button) with a simple card bot until the reward screen shows
 const runRace=()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;let k=0;window.__gfdPitPolicy='human';window.__gfdHeadless=true; // headless only while ticking: the pit stop plays itself as a bot
  while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);if(g.race!==r)break;const c=g.meCar();if(r.phase==='green'&&!c.pit&&!r.pitReq){const pl=g.myPlan();if(pl&&g.lapOf(c)>=pl.lap)g.pitButton();}if(r.caution&&!r.caution.done)r.caution.mine=r.caution.mine||'two';if(!r.attempt&&r.phase==='green'){const x=g.ctxFor(c),h=r.hand.find(h=>!g.cardBlock(g.CARDS[h.id],x)&&(o=>o.succ>=0.65&&o.wreck<0.05)(g.computeOdds(h.id,x)));if(h)g.playCard(h.uid);}}
  window.__gfdHeadless=false;g.saveSeason();
  return !g.race||{phase:r.phase,lap:g.lapOf?g.lapOf(g.meCar()):null,paused:r.paused,k};});
 const click=async sel=>{await p.click(sel);await p.waitForTimeout(80);};
 await load();await p.evaluate(()=>localStorage.clear());await load();
 if(await p.$('#t-cont'))fail('continue shown with no save');
 await click('#t-go');
 if(!(await p.evaluate(()=>!!window.__gfd.readSave())))fail('no save on the garage screen');
 for(let i=0;i<10;i++){
  await click('#g-go');
  if(i===1){ // close the page mid-race (standard stakes): continue restarts the race from the garage
   await p.evaluate(()=>{const g=window.__gfd;for(let k=0;k<400;k++)g.update(0.05);});
   await load();await shot('title-continue');await click('#t-cont');
   const st=await p.evaluate(()=>({scr:!document.getElementById('garage').hidden,idx:window.__gfd.G.raceIdx,n:window.__gfd.G.results.length,notice:!!window.__gfd.G.notice}));
   if(!st.scr||st.idx!==1||st.n!==1||!st.notice)fail('mid-race continue (standard) '+JSON.stringify(st));
   await click('#g-go');
  }
  {const rr=await runRace();if(rr!==true){fail('race '+(i+1)+' did not finish '+JSON.stringify(rr));break;}}
  if(i===0)await shot('reward-standings');
  const pick=await p.$('#reward .minicard.pickable');if(pick)await pick.click();
  if(i===4){ // close on the reward screen after picking: continue lands back on the same reward screen
   const before=await p.evaluate(()=>JSON.stringify({d:window.__gfd.G.deck,m:window.__gfd.G.money,pts:window.__gfd.G.pts}));
   await load();await click('#t-cont');
   const after=await p.evaluate(()=>JSON.stringify({d:window.__gfd.G.deck,m:window.__gfd.G.money,pts:window.__gfd.G.pts}));
   if(before!==after)fail('reward-screen continue changed state');
   if(await p.evaluate(()=>document.getElementById('reward').hidden))fail('continue did not reopen the reward screen');
   if(await p.evaluate(()=>window.__gfd.me&&0))0;
  }
  await click('#w-go');
  if(i===6){ // garage upgrade, reload, check your car still uses the bought stat
   await p.evaluate(()=>{window.__gfd.G.money+=5000;});await p.evaluate(()=>window.__gfd.showGarage());
   const b4=await p.evaluate(()=>window.__gfd.G.stats.spotter);await click('[data-buy="spotter"]');
   await load();await click('#t-cont');
   const st=await p.evaluate(()=>{const G=window.__gfd.G;return {s:G.stats.spotter,linked:G.cars.find(c=>c.isMe).stats===G.stats};});
   if(st.s!==b4+1||!st.linked)fail('upgrade after reload '+JSON.stringify(st));
  }
  if(i===3){await p.evaluate(()=>{const e=document.getElementById('garage');e.scrollTop=e.scrollHeight;});await shot('garage-calendar');}
 }
 const end=await p.evaluate(()=>({txt:document.getElementById('reward').innerText.slice(0,400),save:!!window.__gfd.readSave(),n:window.__gfd.G.results.length,pts:window.__gfd.standings().reduce((s,x)=>s+x.pts,0)}));
 await shot('season-end');
 if(!/Season complete/i.test(end.txt))fail('no season end screen');
 if(end.save)fail('save not cleared at season end');
 if(end.n!==10)fail('results '+end.n);
 // every race hands out the same points total (40+35+34+...+17)
 const per=40+Array.from({length:19},(_,i)=>35-i).reduce((a,b)=>a+b,0);if(end.pts!==per*10)fail('points total '+end.pts+' != '+per*10);
 console.log('season ok:',end.txt.split('\n').slice(0,4).join(' | '));
 // Ironman: closing mid-race starts the race over too (board Q48: no), with no DNF and no penalty
 await click('#e-new');await p.evaluate(()=>{window.__gfd.titleSel.stakes='ironman';window.__gfd.titleSel.diff='normal';});
 await click('#t-go');const m0=await p.evaluate(()=>window.__gfd.G.money);
 await click('#g-go');await p.evaluate(()=>{const g=window.__gfd;for(let k=0;k<200;k++)g.update(0.05);});
 await load();await click('#t-cont');
 const ab=await p.evaluate(()=>{const G=window.__gfd.G;return {n:G.results.length,idx:G.raceIdx,money:G.money,inRace:G.inRace,garage:!document.getElementById('garage').hidden,notice:!!G.notice};});
 if(ab.n!==0||ab.idx!==0||ab.money!==m0||ab.inRace||!ab.garage||!ab.notice)fail('ironman mid-race continue '+JSON.stringify(ab));
 await shot('ironman-restart');
 console.log('ironman restart ok:',JSON.stringify(ab));
 console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;await b.close();})();
