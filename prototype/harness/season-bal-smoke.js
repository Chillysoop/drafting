// season-bal-smoke.js /abs/slice.html : step 23 season balance rules.
// Race pay and sponsor money scale by series (ECON), promotions come from the next series' smaller teams, qualifying adds the same
// driver term for every car, the field races back (TOUGH: the other car's Defense or Racecraft; FIELD: the series), Grit adds odds
// late in the race and Wits keeps crew calls up longer (board Q65), the driver sheet shows both, the garage says driver coaching
// follows you to a new team (Q66), and the kart Crew HQ can't train the pit crew.
const {chromium}=require('playwright');
const html=process.argv[2];
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:900}});const errs=[];p.on('pageerror',e=>errs.push(e.message));
 const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 await p.goto('file://'+html);await p.waitForTimeout(400);await p.evaluate(()=>localStorage.clear());
 const r=await p.evaluate(()=>{const g=window.__gfd,S=window.__gfdSeason,L=window.__gfdLate,out={};
  const T=g.titleSel;Object.assign(T,{sched:'kart',arch:'packrat',diff:'normal',info:'full',stakes:'standard',bg:'hero'});g.newCareer();let G=g.G;
  out.econKart=S.econMul();
  // sponsor offers at the kart scale
  G.sponsorOffers=null;g.showGarage();const sp=g.SPONSORS.find(s=>s.id===G.sponsorOffers[0].id);out.spScaled=G.sponsorOffers[0].per===Math.round(sp.per*S.ECON.kart/100)*100;
  out.coachTxt=[...document.querySelectorAll('#garage .eyebrow')].some(e=>/stays with you/.test(e.textContent));
  g.showCrewHQ();const pb=document.querySelector('#crewhq [data-train="pitCrew"]');out.pitLocked=!!pb&&pb.disabled&&/karts/.test(pb.textContent);
  // promotion offers from a P2 kart season: the next series' rated 5 and 3 teams
  out.offers=L.rollOffers(2).filter(o=>o.kind==='promote').map(o=>o.r);
  // a promote rescales the pending sponsor offers to the late model scale
  G.offers={rk:2,list:L.rollOffers(2),pick:0};const pi=G.offers.list.findIndex(o=>o.kind==='promote');G.sponsorOffers=[{id:'cobalt',per:300,bonus:1800}];g.signOffer(pi);G=g.G;
  out.series=G.series;out.econLate=S.econMul();out.spRescaled=G.sponsorOffers[0].per===600;
  // stock race: card reads carry the series field and the car ahead's Defense
  Object.assign(T,{sched:'mixed',arch:'drafter',bg:'hero'});g.newCareer();G=g.G;G.sponsor={id:'pinecrest',per:400,bonus:2400};G.nextObjectives=g.rollRaceObjectives();
  window.__gfdHeadless=false;g.startRace();window.__gfdHeadless=true;const race=g.race,me=g.meCar();
  for(let k=0;k<600&&race.phase!=='green';k++){if(race.paused){race.paused=false;race._beatPaused=false;}g.update(0.05);}
  let c=g.ctxFor(me);const pass=Object.values(g.CARDS).find(cd=>cd.fam==='pass'&&!cd.kart);
  if(c.ahead){c.ahead.stats.defense=9;}const o1=g.computeOdds(pass.id,c);
  out.fieldTag=o1.tags.some(t=>/Stock cars field/.test(t[1]));out.defTag=!!c.ahead&&o1.tags.some(t=>/defense 9/.test(t[1])&&t[0]===-10);
  // Grit: Short-Track Hero has Grit 15 (+2): +4 late in the race only
  const lapsAt=d=>{me.dist=d;c=g.ctxFor(me);return g.computeOdds(pass.id,c);};
  const early=lapsAt(race.track.laps*0.3),late=lapsAt(race.track.laps*0.8);
  out.gritEarly=early.tags.some(t=>/Grit/.test(t[1]));out.gritLate=late.tags.find(t=>/Grit 15 · late race/.test(t[1]));out.gritLate=out.gritLate&&out.gritLate[0];
  // Wits 12 (+1): a crew call stays up 1 s longer
  G.drv.attr.wits=14;race.radio=null;let tries=0;while(tries++<40){window.spawnRadio&&0;g.spawnRadio();if(race.radio&&race.radio.kind!=='advice')break;race.radio=null;}
  out.wits=race.radio&&race.radio.wits;out.witsDur=race.radio&&race.radio.dur;
  window.__gfdHeadless=false;
  // the driver sheet's race-day panel
  g.showGarage();document.getElementById('g-dsheet').click();out.raceday=(document.getElementById('d-raceday')||{}).innerText||'';
  return out;});
 if(r.econKart!==0.15||!r.spScaled)fail('kart pay scale '+JSON.stringify(r));
 if(!r.coachTxt)fail('garage coaching note');
 if(!r.pitLocked)fail('kart pit crew training not locked');
 if(r.offers.join()!=='5,3')fail('promotion offers '+r.offers);
 if(r.series!=='late'||r.econLate!==0.3||!r.spRescaled)fail('promotion rescale '+JSON.stringify(r));
 if(!r.fieldTag||!r.defTag)fail('field odds tags '+JSON.stringify(r));
 if(r.gritEarly||r.gritLate!==4)fail('grit '+JSON.stringify(r));
 if(r.wits!==2||!(r.witsDur>=8))fail('wits '+JSON.stringify(r));
 if(!/Grit 15/.test(r.raceday)||!/Wits 14/.test(r.raceday))fail('race day panel '+r.raceday);
 console.log(process.exitCode?'season balance smoke FAILED':'season balance smoke ok',JSON.stringify({offers:r.offers,grit:r.gritLate,wits:r.wits}));
 console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;await b.close();})();
