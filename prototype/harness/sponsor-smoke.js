// sponsor-smoke.js /abs/slice.html [shotDir] : step 17 sponsors and race objectives.
// Signing gates Go racing; meeting every objective moves rewards up a tier (and a DNF never does); season goals report
// met/missed; at season end a sponsor that met its goal pays the bonus and renews with a raise, one that missed walks;
// saves from before step 17 (objective ids as strings, no sponsor) still load; phone layout has no horizontal scroll.
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const errs=[];
 const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 for(const [W,H] of [[1280,900],[390,844]]){
  const p=await b.newPage({viewport:{width:W,height:H}});p.on('pageerror',e=>errs.push(e.message));
  const shot=async n=>{if(shots)await p.screenshot({path:`${shots}/${n}-${W}.png`,fullPage:false});};
  const load=async()=>{await p.goto('file://'+html);await p.waitForTimeout(350);};
  const click=async sel=>{await p.click(sel);await p.waitForTimeout(80);};
  await load();await p.evaluate(()=>localStorage.clear());await load();
  await click('#t-go');
  const g0=await p.evaluate(()=>({go:document.getElementById('g-go').disabled,offers:document.querySelectorAll('#g-spons [data-sign]').length,objs:window.__gfd.G.nextObjectives.length,ov:document.documentElement.scrollWidth>innerWidth+1||document.getElementById('garage').scrollWidth>document.getElementById('garage').clientWidth+1}));
  if(!g0.go||g0.offers!==3||g0.objs!==2)fail(W+': garage before signing '+JSON.stringify(g0));
  if(g0.ov)fail(W+': horizontal scroll in garage');
  await p.evaluate(()=>{const e=document.getElementById('g-spons');e.scrollIntoView();});await shot('offers');
  if(W!==1280){await p.close();continue;}
  // season-1 offers never include the sponsors that need a good season
  const gated=await p.evaluate(()=>{const g=window.__gfd;let bad=0;for(let i=0;i<200;i++){g.G.sponsorOffers=null;g.G.past=[];g.showGarage();if(g.G.sponsorOffers.some(o=>g.SPONSORS.find(s=>s.id===o.id).need<99))bad++;}return bad;});
  if(gated)fail('gated sponsor offered in season 1 x'+gated);
  await click('#g-spons [data-sign]');
  const g1=await p.evaluate(()=>{const G=window.__gfd.G;return {sp:G.sponsor&&G.sponsor.id,objs:G.nextObjectives,go:document.getElementById('g-go').disabled,strip:!!document.querySelector('#garage .spstrip')};});
  if(!g1.sp||g1.objs.length!==3||!g1.objs[2].ask||g1.go||!g1.strip)fail('after signing '+JSON.stringify(g1));
  await p.evaluate(()=>document.querySelector('#garage .spstrip').scrollIntoView());await shot('signed');
  // play race 1 with every objective forced to pass: rewards move up a tier
  await click('#g-go');if(await p.$('#q-skip'))await click('#q-skip');await click('#q-race');
  const fin=async force=>p.evaluate(force=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let k=0;
   if(force==='pass')for(const o of g.OBJECTIVES)o._t=o._t||o.test,o.test=()=>true;
   while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);if(force==='dnf'&&r.phase==='green'&&!g.meCar().dnf&&r.t>20){const c=g.meCar();c.dnf=true;c.dnfWhy='test';}}
   for(const o of g.OBJECTIVES)if(o._t){o.test=o._t;delete o._t;}
   window.__gfdHeadless=false;g.saveSeason();const res=g.G.results[g.G.results.length-1];return {tier:res.tier,from:res.tierFrom||null,met:res.objMet,n:res.objs.length,dnf:res.dnf,box:document.getElementById('w-objs').innerText};},force);
  const a=await fin('pass');
  if(a.met!==3||a.n!==3)fail('forced objectives '+JSON.stringify(a));
  if(a.tier!=='victory'&&!a.from)fail('no tier bump with every objective met '+JSON.stringify(a));
  if(a.from&&!/moved up/i.test(a.box))fail('bump not explained');
  await p.evaluate(()=>document.getElementById('w-objs').scrollIntoView());await shot('reward-objectives');
  console.log('race 1:',a.from||'(already victory)','->',a.tier);
  // goal states
  const gs=await p.evaluate(()=>{const g=window.__gfd,R=n=>Array.from({length:n},()=>({start:20,finish:9,expected:18,dnf:false}));
   return [g.goalState('top10x4',R(4),5,6).st,g.goalState('top10x4',R(2),5,0).st,g.goalState('dnf2',[{dnf:true},{dnf:true},{dnf:true}],5,4).st,g.goalState('dnf2',[],5,0).st,g.goalState('pts10',R(1),11,5).st,g.goalState('pts10',R(10),10,0).st,g.goalState('gain50',R(5),5,5).st,g.goalState('win1',R(10),3,0).st];});
  if(gs.join()!=='met,missed,missed,met,behind,met,on,missed')fail('goal states '+gs);
  // season end: met -> renewal with a raise, bonus paid; missed -> walks and is not offered again
  const wrap=async met=>p.evaluate(met=>{const g=window.__gfd,G=g.G;G.sponsor={id:'hitide',per:2200,bonus:12000,since:1};
   G.results=Array.from({length:10},(_,i)=>({start:18,finish:met&&i<4?8:15,expected:18,dnf:false,tier:'solid',track:'x',objs:[]}));G.raceIdx=9;const m0=G.money;g.showEnd();
   return {season:G.season,offers:G.sponsorOffers,paid:G.money-m0,txt:document.getElementById('e-spons').innerText,notice:G.notice,saved:!!g.readSave()};},met);
  const w1=await wrap(true);
  if(w1.paid!==12000||!w1.offers[0].renew||w1.offers[0].id!=='hitide'||w1.offers[0].per!==2600||!/renews/i.test(w1.txt)||!w1.saved)fail('renew '+JSON.stringify(w1));
  await shot('season-end-renew');
  const w2=await wrap(false);
  if(w2.paid!==0||w2.offers.some(o=>o.id==='hitide')||!/walks/i.test(w2.txt)||!/walked/.test(w2.notice))fail('walk '+JSON.stringify(w2));
  await shot('season-end-walk');
  // a DNF never moves rewards up, even with every objective met
  await p.evaluate(()=>{const g=window.__gfd;g.G.sponsor={id:'pinecrest',per:1000,bonus:6000};g.G.sponsorOffers=null;g.G.nextObjectives=g.rollRaceObjectives();g.showGarage();});
  await click('#g-go');if(await p.$('#q-skip'))await click('#q-skip');await click('#q-race');
  const d=await fin('pass');
  console.log('season 3 race 1:',JSON.stringify({tier:d.tier,from:d.from}));
  await click('#w-go');
  await click('#g-go');if(await p.$('#q-skip'))await click('#q-skip');await click('#q-race');
  const dn=await fin('dnf');
  if(!dn.dnf||dn.from)fail('dnf bumped '+JSON.stringify(dn));
  // old save: objective ids as strings, no sponsor, no season
  await load();await p.evaluate(()=>{const g=window.__gfd,s=g.readSave();const G=s.G;G.reward=null;G.inRace=null;G.snap=null;delete G.sponsor;delete G.sponsorOffers;delete G.season;delete G.past;G.nextObjectives=['top15','rival'];for(const c of G.cars)delete c.career.swins;localStorage.setItem('gfd-season-v1',JSON.stringify(s));});
  await click('#t-cont'); // the title reads the save when Continue is pressed
  const old=await p.evaluate(()=>({g:!document.getElementById('garage').hidden,offers:document.querySelectorAll('#g-spons [data-sign]').length,lines:document.querySelectorAll('#garage .objl').length}));
  if(!old.g||old.offers!==3||old.lines!==2)fail('old save '+JSON.stringify(old));
  await click('#g-spons [data-sign]');await click('#g-go');if(await p.$('#q-skip'))await click('#q-skip');await click('#q-race');
  const o2=await fin('');if(o2.n!==3)fail('old save race '+JSON.stringify(o2));
  await p.close();
 }
 console.log(process.exitCode?'sponsor smoke FAILED':'sponsor smoke ok');console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;
 await b.close();})();
