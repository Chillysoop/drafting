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
 // step 16: Go racing opens qualifying; skip it (after a reload it shows the grid you already set) and start the race
 const goRace=async()=>{await click('#g-go');if(await p.$('#q-skip'))await click('#q-skip');await click('#q-race');};
 await load();await p.evaluate(()=>localStorage.clear());await load();
 if(await p.$('#t-cont'))fail('continue shown with no save');
 await click('#t-go');
 // step 17: Go racing waits for a sponsor contract
 if(!(await p.$eval('#g-go',e=>e.disabled)))fail('go racing allowed before signing a sponsor');
 await click('#g-spons [data-sign]');
 if(!(await p.evaluate(()=>!!window.__gfd.readSave())))fail('no save on the garage screen');
 let pre=null;
 for(let i=0;i<10;i++){
  await goRace();
  if(i===1){ // close the page mid-race (standard stakes): continue restarts the race from the garage
   await p.evaluate(()=>{const g=window.__gfd;for(let k=0;k<400;k++)g.update(0.05);});
   await load();await shot('title-continue');await click('#t-cont');
   const st=await p.evaluate(()=>({scr:!document.getElementById('garage').hidden,idx:window.__gfd.G.raceIdx,n:window.__gfd.G.results.length,notice:!!window.__gfd.G.notice}));
   if(!st.scr||st.idx!==1||st.n!==1||!st.notice)fail('mid-race continue (standard) '+JSON.stringify(st));
   await goRace();
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
  if(i===0){const o=await p.evaluate(()=>{const G=window.__gfd.G,res=G.results[0];return {box:!!document.getElementById('w-objs'),n:res.objs.length,ask:res.objs.filter(o=>o.ask).length,ret:res.retainer,tot:res.total===res.purse+res.bonus+res.retainer+res.objs.filter(o=>o.ok).reduce((s,o)=>s+o.pay,0)};});
   if(!o.box||o.n!==3||o.ask!==1||!o.ret||!o.tot)fail('reward objectives/sponsor '+JSON.stringify(o));}
  if(i===9)pre=await p.evaluate(()=>({pts:window.__gfd.standings().reduce((s,x)=>s+x.pts,0),money:window.__gfd.G.money,sp:window.__gfd.G.sponsor.id}));
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
 const end=await p.evaluate(()=>{const G=window.__gfd.G,s=window.__gfd.readSave();return {txt:document.getElementById('reward').innerText.slice(0,400),review:(document.getElementById('e-spons')||{}).innerText||'',save:!!s&&s.G.season===2,n:G.results.length,season:G.season,past:G.past,offers:G.sponsorOffers,money:G.money};});
 await shot('season-end');
 if(!/Season complete/i.test(end.txt))fail('no season end screen');
 // step 17: the career carries on into season 2, saved; the sponsor renews (goal met: bonus paid, renewal offer) or walks (not offered again)
 if(!end.save||end.season!==2||end.n!==0)fail('season 2 not set up '+JSON.stringify({save:end.save,season:end.season,n:end.n}));
 const met=end.past[0].met;
 if(!/renews|walks/i.test(end.review))fail('no sponsor review');
 if(met&&(!end.offers[0].renew||end.offers[0].id!==pre.sp||end.money<=pre.money))fail('renewal '+JSON.stringify(end.offers));
 if(!met&&(end.offers.some(o=>o.id===pre.sp)||end.money!==pre.money))fail('walk '+JSON.stringify(end.offers));
 if(end.past[0].rank<1)fail('past season');
 // every race hands out the same points total (40+35+34+...+17)
 const per=40+Array.from({length:19},(_,i)=>35-i).reduce((a,b)=>a+b,0);if(pre.pts!==per*10)fail('points total '+pre.pts+' != '+per*10);
 console.log('season ok:',end.txt.split('\n').slice(0,4).join(' | '),'| sponsor',met?'renews':'walks');
 await click('#e-next');
 const s2=await p.evaluate(()=>({g:!document.getElementById('garage').hidden,lbl:/season 2/i.test(document.getElementById('garage').innerText),offers:document.querySelectorAll('#g-spons [data-sign]').length,go:document.getElementById('g-go').disabled}));
 if(!s2.g||!s2.lbl||s2.offers<2||!s2.go)fail('season 2 garage '+JSON.stringify(s2));
 await shot('season2-offers');
 await click('#g-spons [data-sign]');if(await p.$eval('#g-go',e=>e.disabled))fail('season 2 sign');
 await load();if(!(await p.$('#t-cont')))fail('no continue for season 2');
 // Ironman: closing mid-race starts the race over too (board Q48: no), with no DNF and no penalty
 await p.evaluate(()=>{window.__gfd.titleSel.stakes='ironman';window.__gfd.titleSel.diff='normal';});
 await click('#t-go');await click('#t-go');await click('#g-spons [data-sign]');const m0=await p.evaluate(()=>window.__gfd.G.money);
 await goRace();await p.evaluate(()=>{const g=window.__gfd;for(let k=0;k<200;k++)g.update(0.05);});
 await load();await click('#t-cont');
 const ab=await p.evaluate(()=>{const G=window.__gfd.G;return {n:G.results.length,idx:G.raceIdx,money:G.money,inRace:G.inRace,garage:!document.getElementById('garage').hidden,notice:!!G.notice};});
 if(ab.n!==0||ab.idx!==0||ab.money!==m0||ab.inRace||!ab.garage||!ab.notice)fail('ironman mid-race continue '+JSON.stringify(ab));
 await shot('ironman-restart');
 console.log('ironman restart ok:',JSON.stringify(ab));
 // Delete save inside a sandboxed frame like the artifact viewer, where window.confirm() is blocked: two taps must delete it
 {const fs=require('fs'),path=require('path'),os=require('os');const wrap=path.join(os.tmpdir(),'gfd-sandbox-wrap.html');
  fs.writeFileSync(wrap,`<iframe sandbox="allow-scripts allow-same-origin" src="file://${html}" style="width:1200px;height:900px"></iframe>`);
  await p.evaluate(()=>window.__gfd.saveSeason());await p.goto('file://'+wrap);await p.waitForTimeout(800);
  const F=p.frames().find(f=>f!==p.mainFrame());
  if(!(await F.$('#t-del')))fail('no Delete save button in the sandboxed frame');
  else{await F.click('#t-del');await F.click('#t-del');await F.waitForTimeout(200);
   const left=await F.evaluate(()=>!!localStorage.getItem('gfd-season-v1')||!!document.getElementById('t-cont'));
   if(left)fail('Delete save did nothing in a sandboxed frame');else console.log('sandboxed delete ok');}}
 console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;await b.close();})();
