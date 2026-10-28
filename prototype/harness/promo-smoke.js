// promo-smoke.js /abs/slice.html [shotDir] : step 22 late models and promotion.
// New careers start in karts (Q77). One save climbs the ladder: a kart season ends with contract offers (stay, a better
// kart team, late model teams by points position), one d20 Negotiate per offer, and signing a late model offer switches
// series and team: the car and crew are the new team's, the driver, money, collection and parts stay, kart cards turn into
// their stock car cousins, the persona grows up and the rivals move up with half their Grudge. The save keeps it. A late
// model race on Crossroads Figure-8 runs with pit stops, the crossover hazard and no page errors. A late season in the top
// 14 brings a stock car offer, and signing it puts the save in stock cars. A low finish brings no promotion offer.
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
  // fake a finished season with you at points position rk, then open the season review
  const endSeason=async rk=>p.evaluate(rk=>{const g=window.__gfd,G=g.G;G.results=Array.from({length:10},()=>({start:12,finish:rk,expected:12,dnf:false,tier:'solid',track:'x',objs:[]}));G.raceIdx=9;
   const st=g.standings();G.pts={};st.forEach((s,i)=>{G.pts[s.c.id]=400-i*10;});const mi=G.cars.find(c=>c.isMe).id,o=st[rk-1].c.id;G.pts[o]=G.pts[mi];G.pts[mi]=400-(rk-1)*10;if(o!==mi)G.pts[o]=400-st.findIndex(s=>s.c.isMe)*10;
   g.showEnd();return {rk:G.offers&&G.offers.rk,offers:G.offers&&G.offers.list.map(o=>o.kind+':'+o.series+':'+o.r)};},rk);

  await load();await p.evaluate(()=>localStorage.clear());await load();
  const t0=await p.evaluate(()=>({arch:[...document.querySelectorAll('#title .personas [data-arch]')].map(b=>b.dataset.arch).join(),chips:[...document.querySelectorAll('#title [data-series]')].map(b=>b.dataset.series+(b.classList.contains('on')?'*':'')).join(),
   note:!!document.querySelector('#title .ladder-note'),sched:[...document.querySelectorAll('#title [data-sched]')].filter(b=>!b.hidden).map(b=>b.dataset.sched).join()}));
  if(t0.arch!=='brawler,linepainter,packrat,latebraker'||t0.chips!=='kart*,late,mixed'||!t0.note||t0.sched!=='kart')fail('title '+JSON.stringify(t0));
  await click('[data-series="late"]');
  const t1=await p.evaluate(()=>({arch:[...document.querySelectorAll('#title .personas [data-arch]')].map(b=>b.dataset.arch).join(),intro:!!document.querySelector('#title .late-intro'),cal:[...document.querySelectorAll('#title .g2 .row')].map(r=>r.innerText).join('|')}));
  if(t1.arch!=='drafter,bully,whisperer,restart,gambler'||!t1.intro||!/Crossroads/.test(t1.cal)||!/Cedar Bowl/.test(t1.cal))fail('late title '+JSON.stringify(t1));
  if(await noScroll('title'))fail(W+': title scroll');
  await p.evaluate(()=>document.querySelector('#title .series-pick').scrollIntoView());await shot('ladder-title');
  await click('[data-series="kart"]');await click('[data-arch="packrat"]');await click('#t-go');
  const k0=await p.evaluate(()=>{const g=window.__gfd,G=g.G;return {series:G.series,team:G.team,tn:g.TEAMS[G.team].name,ride:(document.getElementById('g-ride')||{}).innerText||''};});
  if(k0.series!=='kart'||k0.team!==9||k0.tn!=='Garage 77 Karting'||!/Rung 1/i.test(k0.ride))fail('kart career '+JSON.stringify(k0));
  // a grudge to carry up
  await p.evaluate(()=>{const G=window.__gfd.G;const id=Object.keys(G.riv)[0];G.riv[id].g=6;G.money=20000;});
  // kart season ends P2: stay, a better kart team, and three late model teams
  const e1=await endSeason(2);
  if(e1.rk!==2||e1.offers.filter(o=>o.startsWith('promote:late')).length!==3||!e1.offers[0].startsWith('stay:kart')||!e1.offers.some(o=>o.startsWith('move:kart')))fail('kart offers '+JSON.stringify(e1));
  const nx=await p.evaluate(()=>document.getElementById('e-next').innerText);if(!/contract offers/i.test(nx))fail('season review button '+nx);
  await click('#e-next');
  if(await noScroll('reward'))fail(W+': offers scroll');
  const pi=await p.evaluate(()=>window.__gfd.G.offers.list.findIndex(o=>o.kind==='promote'));
  await click(`[data-neg="${pi}"]`);
  const ng=await p.evaluate(pi=>{const o=window.__gfd.G.offers.list[pi];return {neg:o.neg,txt:document.querySelector(`[data-offer="${pi}"] .neg`).innerText,btn:!!document.querySelector(`[data-neg="${pi}"]`)};},pi);
  if(!ng.neg||!/vs DC/.test(ng.txt)||ng.btn)fail('negotiate '+JSON.stringify(ng));
  await click(`[data-pick="${pi}"]`);await shot('offers');
  const before=await p.evaluate(()=>{const G=window.__gfd.G;return {money:G.money,drv:JSON.stringify(G.drv.attr),racecraft:G.stats.racecraft,coll:G.coll.length,o:G.offers.list[G.offers.pick]};});
  await click('#o-sign');if(await p.isVisible('#o-sign'))await click('#o-sign');
  const l0=await p.evaluate(()=>{const g=window.__gfd,G=g.G,L=window.__gfdLate,o=G.contract;const rv=Object.keys(G.riv).map(id=>G.riv[id].g);
   return {series:G.series,ct:G.carType,sched:G.sched,arch:G.arch,kartCards:G.coll.filter(id=>g.CARDS[id].kart).length,coll:G.coll.length,deck:G.deck.length,size:g.deckSize(),kartRelics:G.relics.filter(k=>RELICS[k].kart).length,
    power:G.stats.power,spotter:G.stats.spotter,racecraft:G.stats.racecraft,money:G.money,drv:JSON.stringify(G.drv.attr),per:o.per,team:G.team,tn:g.TEAMS[G.team].name,mine:G.cars.find(c=>c.isMe).team,
    rv:Math.max(...rv),rivals:Object.keys(G.riv).length,next:g.TRACKS[g.arcTi(G.raceIdx)].id,screen:!document.getElementById('garage').hidden,ride:(document.getElementById('g-ride')||{}).innerText||'',ladder:G.ladder.join()};});
  const o=before.o;
  if(l0.series!=='late'||l0.ct!=='late'||l0.sched!=='late'||l0.arch!=='drafter'||l0.kartCards||l0.coll!==before.coll||l0.kartRelics||l0.deck<11||l0.deck>l0.size.max||l0.size.min!==11)fail('promotion '+JSON.stringify(l0));
  if(l0.power!==o.r||l0.spotter!==window_crew(o.r)||l0.racecraft!==before.racecraft||l0.money!==before.money+o.sign||l0.drv!==before.drv||l0.per!==o.per||l0.team!==o.team||l0.mine!==o.team||l0.tn!==o.name)fail('team switch '+JSON.stringify({l0,o}));
  if(l0.rv!==3||l0.rivals<5||l0.next!=='cedar'||!l0.screen||!/Late models/i.test(l0.ride)||l0.ladder!=='kart,late')fail('after promotion '+JSON.stringify(l0));
  if(await noScroll('garage'))fail(W+': late garage scroll');
  await shot('late-garage');
  // the save keeps the series and the team names
  await load();
  const sv=await p.evaluate(()=>{const tc=document.getElementById('t-cont');const eb=tc.closest('.panel').querySelector('.eyebrow').innerText;tc.click();const g=window.__gfd,G=g.G;return {eb,series:G.series,tn:g.TEAMS[G.team].name,t0:g.TEAMS[0].name};});
  if(sv.series!=='late'||sv.tn!==o.name||sv.t0!=='Delmar Late Models'||!/Late models/i.test(sv.eb))fail('late save '+JSON.stringify(sv));
  // a late model race on the figure-8, through the real garage and qualifying
  await p.evaluate(()=>{const g=window.__gfd;g.G.raceIdx=3;g.G.deckFor=null;g.showGarage();});
  await click('#g-spons [data-sign]');if(await p.isVisible('#g-ev-walk'))await click('#g-ev-walk');
  await click('#g-go');if(await p.isVisible('#q-skip'))await click('#q-skip');await click('#q-race');
  const r0=await p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0;while(r.phase!=='green'&&k++<400){r.paused=false;g.update(0.05);}for(let i=0;i<200;i++){r.paused=false;g.update(0.05);}window.__gfdHeadless=false;renderPitMod(true);
   return {id:r.track.id,fig8:!!r.track.fig8,phase:r.phase,mod:document.getElementById('pitmod').innerText,kart:document.body.classList.contains('kart-race')};});
  if(r0.id!=='cross'||!r0.fig8||r0.phase!=='green'||r0.kart||!/FUEL|PIT/i.test(r0.mod))fail('figure-8 race '+JSON.stringify(r0));
  await shot('figure8');
  // the crossover: a car on the other leg arriving with you puts an Evade card in your hand
  const x0=await p.evaluate(()=>{const g=window.__gfd,r=g.race,L=window.__gfdLate,t=r.track,m=g.meCar();const n=L.f8Next(t,m);return {s:n.s,leg:n.leg,X:L.f8X(t)};});
  if(!(x0.s>=0&&x0.s<=t_lap(x0))||x0.X.length!==2)fail('crossover geometry '+JSON.stringify(x0));
  const res=await p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0,hz=0;
   while(g.race===r&&k++<40000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);if(r.hazard&&r.hazard.cross&&!hz){hz=1;}}
   window.__gfdHeadless=false;return {hz:r.stats.crossHz||0,wr:r.stats.crossWr||0,fin:r.phase,n:r.stats.crossN,c:r.stats.crossC,f8k:r.f8k,cd:r.f8cd,t:r.t,res:g.G.results[g.G.results.length-1]};});
  if(res.fin!=='finished'||!res.res)fail('figure-8 finish '+JSON.stringify(res));
  console.log(W,'figure-8 race:',JSON.stringify({finish:res.res.finish,crossHz:res.hz,crossWrecks:res.wr,n:res.n,c:res.c,f8k:res.f8k,cd:res.cd,t:res.t}));
  // late season ends P6: two stock car offers; sign one and the save is a stock car career
  await p.evaluate(()=>{const g=window.__gfd;g.G.reward=null;});
  const e2=await endSeason(6);
  if(e2.offers.filter(o=>o.startsWith('promote:stock')).length!==2)fail('late offers '+JSON.stringify(e2));
  await click('#e-next');
  const si=await p.evaluate(()=>window.__gfd.G.offers.list.findIndex(o=>o.kind==='promote'));
  await click(`[data-pick="${si}"]`);await click('#o-sign');if(await p.isVisible('#o-sign'))await click('#o-sign');
  const s0=await p.evaluate(()=>{const g=window.__gfd,G=g.G;return {series:G.series,ct:G.carType,sched:G.sched,arch:G.arch,tn:g.TEAMS[G.team].name,t0:g.TEAMS[0].name,ladder:G.ladder.join(),size:g.deckSize(),next:g.TRACKS[g.arcTi(G.raceIdx)].id};});
  if(s0.series!=='stock'||s0.ct!=='stock'||s0.sched!=='mixed'||s0.arch!=='drafter'||s0.t0!=='Apex Motorsports'||s0.ladder!=='kart,late,stock'||s0.size.min!==12||s0.next!=='mill')fail('stock promotion '+JSON.stringify(s0));
  // the top rung: no promotion; a low finish anywhere brings only a stay offer
  const e3=await endSeason(17);
  if(e3.offers.join()!=='stay:stock:'+(await p.evaluate(()=>window.__gfd.TEAMS[window.__gfd.G.team].r)))fail('low finish offers '+JSON.stringify(e3));
  await p.close();
 }
 if(errs.length)fail('page errors: '+errs.slice(0,3).join(' | '));
 console.log(process.exitCode?'promo smoke FAILED':'promo smoke ok');await b.close();})();
function window_crew(r){return Math.max(2,Math.min(8,3+Math.floor((r-3)/2)));}
function t_lap(){return 20;}
