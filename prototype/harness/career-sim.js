// career-sim.js N /abs/slice.html out.json [runs] [extraJSON] : step 23 season balance. Plays N whole seeded careers per run,
// from a kart season up the ladder (karts 6 races -> late models 8 -> stock cars 10, then more stock seasons) with the real
// between-race rules: sponsor signing, off-track d20 events (a bot picks the best odds), reward cards and parts, the race deck
// auto-built for each track, qualifying, garage spending, and season-end contract offers (negotiate once, take the best step up).
// runs = comma list of policy:build, e.g. "human:mix,human:driver,human:crew,none:mix,spam:mix".
//  policy: none (hands-off: no cards, chief runs the stops), human (v9 human bot: looks every 2.5 s, plays >=65% / <5% wreck, presses PIT, sets caution calls), spam (v9 hspam: best card always)
//  build: what the garage money goes on, cheapest first: mix (car, driver coaching and crew), driver (driver coaching only),
//         crew (crew training only), car (car only), save (nothing)
// extraJSON: {"start":"stock"|"late" starts there with a stock persona, "stats":{...} sets G.stats at the start, "econ":{"kart":0.2,...} overrides ECON, "races":34,"seed":61,"bal":{...BAL overrides},"pre":"js run in the page first","quali":"avg"}
// Output: per run, one row per career race (series, season race, finish, expected, start, DNF, money, stats) for every career.
const {chromium}=require('playwright');
(async()=>{
 const [N,html,out,runsArg,extra]=[+process.argv[2]||4,process.argv[3],process.argv[4]||'career.json',process.argv[5]||'human:mix,none:mix',JSON.parse(process.argv[6]||'{}')];
 const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:800}});
 const errs=[];p.on('pageerror',e=>{errs.push(e.message);console.log('PAGEERR',e.message);});
 await p.goto('file://'+html);await p.waitForTimeout(300);
 const res=await p.evaluate(({N,runs,X})=>{const g=window.__gfd,L=window.__gfdLate;
  if(X.pre)(0,eval)(X.pre);if(X.econ&&window.__gfdSeason)Object.assign(window.__gfdSeason.ECON,X.econ);
  if(X.bal)Object.assign(g.BAL,X.bal);
  function seedRng(s){let t=s>>>0;Math.random=function(){t+=0x6D2B79F5;let r=Math.imul(t^t>>>15,1|t);r^=r+Math.imul(r^r>>>7,61|r);return ((r^r>>>14)>>>0)/4294967296;};}
  const DRV=['racecraft','defense','consistency','tireMgmt'],CAR=['power','handling','aero','durability'],CREW=g.CREW_ROLES;
  const shopCost=k=>window.__gfdSeason?window.__gfdSeason.shopCost(k):CAR.includes(k)?3500+1500*Math.max(0,g.G.stats[k]-3):DRV.includes(k)?3000+1200*Math.max(0,g.G.stats[k]-4):g.CREW_TRAIN(g.G.stats[k]);
  function spend(build){const G=g.G;if(build==='save')return 0;const crew=G.series==='kart'?CREW.filter(k=>k!=='pitCrew'):CREW,keys=build==='mix'?[...CAR,...DRV,...crew]:build==='driver'?DRV:build==='crew'?crew:CAR;let n=0,spent=0;
   for(let k=0;k<40;k++){const c=keys.filter(x=>G.stats[x]<10).map(x=>({x,c:shopCost(x)})).sort((a,b)=>a.c-b.c||G.stats[a.x]-G.stats[b.x])[0];if(!c||G.money<c.c)break;G.money-=c.c;spent+=c.c;G.stats[c.x]++;n++;}
   return spent;}
  // the off-track event as a bot: each round, the answer with the best odds (a success counts 1, mixed 0.6, a crit 1.4)
  function evBot(){const G=g.G;let k=0;while(G.event&&!G.event.done&&k++<12){const ev=G.event;
    if(ev.cur){g.evNext();continue;}
    const r=g.evRound(ev);let best=null,bv=-1;
    for(const o of r.opts){let v=0.5;if(!o.auto){try{const bb=g.chkBuild(o,ev,false),od=bb.odds||g.chkOdds(bb.mod,bb.dc,bb.mode);v=od.crit*1.4+od.succ+od.mixed*0.6;}catch(e){v=0.3;}}if(v>bv){bv=v;best=o;}}
    if(!best){g.evWalk();break;}g.evChoose(best.id);if(G.event&&G.event.cur)g.evNext();}
   if(G.event&&!G.event.done)g.evWalk();}
  function signSponsor(){const G=g.G;if(G.sponsor)return;if(!G.sponsorOffers)g.showGarage();const o=[...(G.sponsorOffers||[])].sort((a,b)=>(b.per*10+(b.bonus||0))-(a.per*10+(a.bonus||0)))[0];if(!o)return;
   G.sponsor={id:o.id,per:o.per,bonus:o.bonus,since:G.season||1,renew:!!o.renew};G.sponsorOffers=null;G.nextObjectives=g.rollRaceObjectives();}
  function offers(){const G=g.G;if(!G.offers)return null;const list=G.offers.list;
   const rank=o=>(o.kind==='promote'?100:0)+o.r;let i=list.map((o,i)=>[rank(o),i]).sort((a,b)=>b[0]-a[0])[0][1];L.negotiate(i);const o=list[i];L.signOffer(i);return {kind:o.kind,series:o.series,r:o.r};}
  function runRace(policy){const G=g.G,BAL=g.BAL,PG6=!!g.pgGo;window.__gfdPitPolicy=policy==='none'?'chief':'human';
   window.__gfdHeadless=false;g.startRace();window.__gfdHeadless=true;const r=g.race,dt=0.05;
   const S={t:0,nextLook:2.5,pend:null,lastBeat:0,focus:1,hzSeen:null};let guard=0;
   const want=(c)=>{const cands=r.hand.map(h=>{const cd=g.CARDS[h.id];if(g.cardBlock(cd,c))return null;return {h,o:g.computeOdds(h.id,c)};}).filter(Boolean).sort((a,b)=>b.o.succ-a.o.succ);
    if(!cands.length)return null;if(policy==='spam')return cands[0];return cands.find(x=>x.o.succ>=0.65&&x.o.wreck<0.05)||null;};
   while(g.race===r&&r.phase!=='finished'&&guard++<60000){if(r.paused){r.paused=false;r._beatPaused=false;}
    if(BAL.focusSec)r.focusMeter=S.focus;g.update(dt);if(BAL.focusSec)S.focus=r.focusMeter;S.t+=dt;
    if(g.race!==r||r.phase==='finished')break;
    const car=g.meCar();if(car.dnf)continue;
    if(PG6&&policy!=='none'&&!X.noStrat&&r.phase==='green'&&!car.pit&&!r.pitReq){const pl=g.myPlan();if(pl&&g.lapOf(car)>=pl.lap)g.pitButton();}
    if(r.radio&&r.radio.kind==='buff'&&policy!=='none')g.acceptRadio();
    if(r.radio&&r.radio.kind==='mech'&&policy!=='none'&&r.radio.chance>=(X.callMin||55))g.acceptRadio();
    const c=g.ctxFor(car);
    if(policy!=='none'&&!X.noStrat&&r.caution&&!r.caution.done&&!r.caution.botSet){r.caution.botSet=1;const tt=car.tires;r.caution.mine=tt>=60&&car.damage<25?'stay':tt>=40?'two':'four';}
    if(policy==='none'||r.attempt)continue;
    const hz=r.hand.find(h=>h.special);
    if(hz){if(!S.pend||S.pend.uid!==hz.uid)S.pend={uid:hz.uid,at:S.t+2.0*(BAL.slowmo<1?BAL.slowmo:1)};}
    const beatNow=(r.beats||0)>S.lastBeat&&!hz;S.lastBeat=r.beats||0;
    if(beatNow&&BAL.board&&r.phase==='green'){for(const x of [...r.hand]){const cd=g.CARDS[x.id];if(!x.special&&!['restart','pit'].includes(cd.fam)&&(x.beat||0)<r.beats&&g.cardBlock(cd,c))g.burnCard(x.uid);}}
    if(beatNow&&BAL.beat&&BAL.beatPause&&r.phase==='green'){const pk=want(c);if(pk){g.playCard(pk.h.uid);S.pend=null;}continue;}
    if(S.pend&&!hz&&S.pend.uid.startsWith('hz'))S.pend=null;
    if(S.pend){if(S.t>=S.pend.at){const h=r.hand.find(x=>x.uid===S.pend.uid);S.pend=null;if(h&&!g.cardBlock(g.CARDS[h.id],c))g.playCard(h.uid);}continue;}
    if(S.t>=S.nextLook){S.nextLook=S.t+2.5;const pk=want(c);if(pk){let delay=1.5*BAL.slowmo;if(BAL.focusSec&&BAL.slowmo<1){const cost=1.5/BAL.focusSec;if(S.focus>=cost)S.focus-=cost;else delay=1.5;}S.pend={uid:pk.h.uid,at:S.t+delay};}}
   }
   let k=0;while(g.race===r&&k++<400)g.update(0.05);window.__gfdHeadless=false;
   return {calls:r.stats.calls||0,cwon:r.stats.callsWon||0,combos:r.stats.combos||0};}
  function career(policy,build,seed,arch){seedRng(seed);const T=g.titleSel;T.sched=X.start==='stock'?'mixed':X.start==='late'?'late':'kart';T.arch=X.start&&X.start!=='kart'?['drafter','bully','whisperer','restart','gambler'][seed%5]:arch;T.diff='normal';T.info='full';T.stakes='standard';T.bg=null;T.name='Bot';
   g.newCareer();const G=g.G;if(X.stats)Object.assign(G.stats,X.stats);G.nextObjectives=g.rollRaceObjectives();const rows=[];let cr=0,signs=[];
   while(cr<(X.races||34)){
    signSponsor();evBot();
    const t=g.TRACKS[g.arcTi(G.raceIdx)];
    const spent=spend(build);
    // driver level-up points: Grit first (late-race odds), then Wits (longer radio calls), then Savvy
    if(G.drv)while(G.drv.pts>0){const k=['grit','wits','savvy'].find(x=>G.drv.attr[x]<(x==='savvy'?20:18));if(!k)break;G.drv.pts--;G.drv.attr[k]++;}
    G.deck=g.autoDeck(t);G.deckFor=G.raceIdx;
    if(g.qualiAuto)g.qualiAuto(policy==='none'?'skip':(X.quali||'avg'),null);
    const st=runRace(policy);cr++;
    const x=G.results[G.results.length-1],R=G.reward;
    const s=G.stats;rows.push({cr,series:G.series,season:G.season||1,ri:G.raceIdx+1,len:g.seasonLen(),trk:t.id,fin:x.finish,exp:x.expected,start:x.start,dnf:x.dnf?1:0,tot:x.total,purse:x.purse,bonus:x.bonus,ret:x.retainer||0,sal:x.salary||0,wage:x.wages||0,obj:x.objs.filter(o=>o.ok).reduce((a,o)=>a+o.pay,0),money:G.money,spent,
     car:(s.power+s.handling+s.aero)/3,drv:(s.racecraft+s.defense+s.consistency+s.tireMgmt)/4,crew:CREW.reduce((a,k)=>a+s[k],0)/CREW.length,team:L.myTeam(),tr:g.TEAMS[L.myTeam()].r,
     rk:g.myRank(),calls:st.calls,cwon:st.cwon,lvl:G.drv?G.drv.lvl:0});
    // reward: the rarest card offered, the first part offered
    if(R){if(R.cardOffer&&R.cardOffer.length){const rr={rare:3,uncommon:2,common:1};const id=[...R.cardOffer].sort((a,b)=>(rr[g.CARDS[b].rar]||0)-(rr[g.CARDS[a].rar]||0))[0];G.coll.push(id);R.picked=id;}
     if(R.relicOffer&&R.relicOffer.length){window.__gfd.addRelic?window.__gfd.addRelic(R.relicOffer[0]):(G.relics.includes(R.relicOffer[0])||G.relics.push(R.relicOffer[0]));}}
    const last=G.raceIdx>=g.seasonLen()-1;G.reward=null;
    if(last){rows[rows.length-1].seasonRank=g.myRank();g.showEnd(false);const o=offers();signs.push(o);if(o)rows[rows.length-1].signed=o;}
    else{G.raceIdx++;G.relicShop=null;G.shopRerolls=0;G.nextObjectives=g.rollRaceObjectives();G.event=g.offTrackNext();}
   }
   return rows;}
  const ARCHS=['packrat','brawler','linepainter','latebraker'];
  const outR={};
  for(const run of runs){const [pol,build]=run.split(':');outR[run]=[];for(let i=0;i<N;i++){outR[run].push(career(pol,build||'mix',(X.seed||61)*7919+i*104729,ARCHS[i%ARCHS.length]));}}
  return outR;},{N,runs:runsArg.split(','),X:extra});
 require('fs').writeFileSync(out,JSON.stringify(res));
 console.log('wrote',out,'page errors',errs.length);await b.close();
})();
