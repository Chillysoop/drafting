// Step 16: optional bal.quali / bal.qcard run the qualifying mini-game as a bot before each race (unset = old roll-off, byte-identical).
// Step 15b: optional bal.coll (extra collection cards) and bal.deckFor ('same' or a track offset) to test race decks.
// Balance harness v6 (step 8): v5 + 'human' tosses on purpose when a toss effect is live (and the card is weak or dead),
// the human's Focus is the game's own Focus meter (beat refills, risky successes, Focus tosses and Perfect Stops all count), new policies 'human2' (tosses only dead or <40% cards), 'bold' (human timing,
// plays >=50% / <8% wreck) and 'hspam' (human timing, always plays the best card), and toss/refill stats.
// Balance harness v4 (6b): like v3, plus: on builds with the pit mini-game, every policy but 'none' presses PIT on the plan lap
// (Q18: no auto-pit) and plays the pit stop as a bot (Setups + release near the drop); 'none' never pits on green and lets
// the crew chief run the stop. Extra output: dry (s out of fuel), stops, perfect, setups, pk, stopT.
// Balance harness v2. Usage: node harness2.js N html 'BALjson' out.json [policies]
// Policies: none (hands-off), smart (instant bot, plays >=65% / <5% wreck), spam (best card always),
// human (smart rules, but looks at the hand every 2.5 s and needs 1.5 s to react; at a draw beat it decides while paused,
// and while a card is selected the game runs at BAL.slowmo so the reaction costs less race time).
const {chromium}=require('playwright');
(async()=>{
 const [N,html,bal,out,pols]=[+process.argv[2]||20,process.argv[3],JSON.parse(process.argv[4]||'{}'),process.argv[5],(process.argv[6]||'none,smart,spam,human').split(',')];
 const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:800}});
 p.on('pageerror',e=>console.log('PAGEERR',e.message));
 await p.goto('file://'+html);await p.waitForTimeout(300);
 const res=await p.evaluate(({N,bal,pols})=>{const balIn=bal;
  window.__gfdHeadless=true;const g=window.__gfd;const PG6=!!g.pgGo;
  if(g.setBal){if(bal._p){g.setBal(bal._p);for(const k in bal)if(k in g.BAL)g.BAL[k]=bal[k];}else g.setBal(bal);}
  function want(R,c,policy){
   const cands=R.hand.map(h=>{const cd=g.CARDS[h.id];if(g.cardBlock(cd,c))return null;return {h,cd,o:g.computeOdds(h.id,c)};}).filter(Boolean).sort((a,b)=>b.o.succ-a.o.succ);
   if(!cands.length)return null;
   if(policy==='spam'||policy==='hspam')return cands[0];
   if(policy==='bold')return cands.find(x=>x.o.succ>=0.5&&x.o.wreck<0.08)||null;
   return cands.find(x=>x.o.succ>=0.65&&x.o.wreck<0.05)||null;
  }
  let __rs=0;const __mr=Math.random;
  function seedRng(s){let t=s>>>0;Math.random=function(){t+=0x6D2B79F5;let r=Math.imul(t^t>>>15,1|t);r^=r+Math.imul(r^r>>>7,61|r);return ((r^r>>>14)>>>0)/4294967296;};}
  function runRace(arch,trk,policy){if(bal.seed!=null)seedRng((bal.seed*7919+(__rs++)*104729+trk*31+arch.length*1009)>>>0);
   g.titleSel.arch=arch;g.titleSel.diff='normal';g.titleSel.info='full';g.titleSel.stakes='standard';
   window.__gfdPitPolicy=policy==='none'?'chief':'human';
   g.newCareer();if(bal.tune&&g.setTune)g.setTune(bal.tune);if(bal.statBoost)for(const k of ['power','handling','aero','racecraft','defense','consistency','tireMgmt'])g.G.stats[k]+=bal.statBoost;g.G.raceIdx=trk;
   // step 15b: bal.coll adds cards to the collection; bal.deckFor builds the race deck with the game's auto-build for that track index ('same' = this race), else the whole collection races
   if(bal.coll){g.G.coll=[...g.G.coll,...bal.coll];g.G.deck=[...g.G.coll];}
   if(bal.deckFor!=null&&g.autoDeck)g.G.deck=g.autoDeck(g.TRACKS[bal.deckFor==='same'?trk:(trk+bal.deckFor)%5]);
   // step 16: bal.quali = a qualifying bot (skip|poor|avg|good|great) sets the grid; bal.qcard 'best' also picks a qualifying card
   if(bal.quali&&g.qualiAuto)g.qualiAuto(bal.quali,bal.qcard||null);
   g.startRace();const r=g.race;
   const BAL=g.BAL||{slowmo:1};const dt=0.05;
   let dry=0;const S={focus:1,hz:0,hzSeen:null,t:0,nextLook:2.5,pend:null,lastBeat:0,beats:0,slowReal:0,idleMax:0,handS:0,deadS:0,emptyS:0,samples:0,odds:[],fg:0,tossFx:0};
   while(g.race===r&&r.phase!=='finished'){
    if(BAL.focusSec)r.focusMeter=S.focus;g.update(dt);if(BAL.focusSec)S.focus=r.focusMeter;S.t+=dt;
    if(g.race!==r||r.phase==='finished')break;
    const car=g.meCar();if(car.dnf)continue;if(car.fuel<=0)dry+=dt;
    if(PG6&&policy!=='none'&&r.phase==='green'&&!car.pit&&!r.pitReq){const pl=g.myPlan();if(pl&&g.lapOf(car)>=pl.lap)g.pitButton();}
    if(r.radio&&r.radio.kind==='buff'&&policy!=='none')g.acceptRadio();
    if(r.radio&&r.radio.kind==='mech'&&policy!=='none'&&r.radio.chance>=55)g.acceptRadio();
    const c=g.ctxFor(car);
    if(r.hazard&&S.hzSeen!==r.hazard.uid){S.hzSeen=r.hazard.uid;S.hz++;}
    if(Math.round(S.t*20)%20===0&&r.phase==='green'){S.samples++;S.handS+=r.hand.length;if(!r.hand.some(h=>!g.cardBlock(g.CARDS[h.id],c)))S.deadS++;if(!r.hand.length)S.emptyS++;if(g.idlePressure)S.idleMax=Math.max(S.idleMax,g.idlePressure());}
    if(policy!=='none'&&r.caution&&!r.caution.done&&!r.caution.botSet){r.caution.botSet=1;const tt=car.tires;r.caution.mine=tt>=60&&car.damage<25?'stay':tt>=40?'two':'four';}
    if(policy==='none'||policy==='pitonly'||r.attempt)continue;
    const HUM=policy==='human'||policy==='human2'||policy==='bold'||policy==='hspam';const pol=(policy==='human'||policy==='human2')?'smart':policy;const TCUT=policy==='human2'?0.4:0.55;

    const hz=r.hand.find(h=>h.special);
    if(hz&&policy==='smart'){g.playCard(hz.uid);continue;}
    if(hz&&HUM){if(!S.pend||S.pend.uid!==hz.uid)S.pend={uid:hz.uid,at:S.t+2.0*(BAL.slowmo<1?BAL.slowmo:1)};}
    if(!HUM){const pk=want(r,c,policy);if(pk){S.odds.push(pk.o.succ);g.playCard(pk.h.uid);}continue;}
    // human
    const beatNow=(r.beats||0)>S.lastBeat&&!hz;S.lastBeat=r.beats||0;
    if(beatNow&&BAL.board&&r.phase==='green'){for(const x of [...r.hand]){const cd=g.CARDS[x.id];if(!x.special&&!['restart','pit'].includes(cd.fam)&&(x.beat||0)<r.beats&&g.cardBlock(cd,c))g.burnCard(x.uid);}}
    // toss-aware: toss a weak or dead card when its toss effect is live (one per look/beat)
    const tossNow=()=>{if(!BAL.tossFx||!g.tossLive||r.phase!=='green'||policy==='hspam')return;const xs=r.hand.filter(x=>{const cd=g.CARDS[x.id];if(x.special||!cd.toss||cd.toss.k==='charge'||['restart','pit'].includes(cd.fam)||!g.tossLive(cd))return false;if(cd.toss.k==='focus'&&S.focus>0.6)return false;const b=g.cardBlock(cd,c);return b||g.computeOdds(x.id,c).succ<TCUT;});
     if(xs.length&&r.hand.length>=2){g.burnCard(xs[0].uid);S.tossFx++;if(r.focusMeter!=null){}}};
    if(beatNow)tossNow();
    if(beatNow&&BAL.beat&&BAL.beatPause&&r.phase==='green'){S.beats++;const pk=want(r,c,pol);if(pk){S.odds.push(pk.o.succ);g.playCard(pk.h.uid);S.pend=null;}continue;}
    if(S.pend&&!hz&&S.pend.uid.startsWith('hz')){S.pend=null;}
    if(S.pend){if(S.t>=S.pend.at){const h=r.hand.find(x=>x.uid===S.pend.uid);S.pend=null;if(h&&!g.cardBlock(g.CARDS[h.id],c)){S.odds.push(g.computeOdds(h.id,c).succ);g.playCard(h.uid);}}continue;}
    if(S.t>=S.nextLook){S.nextLook=S.t+2.5;tossNow();const pk=want(r,c,pol);if(pk){let delay=1.5*BAL.slowmo;if(BAL.focusSec&&BAL.slowmo<1){const cost=1.5/BAL.focusSec;if(S.focus>=cost)S.focus-=cost;else delay=1.5;}S.slowReal+=1.5;S.pend={uid:pk.h.uid,at:S.t+delay};}}
   }
   // run out the finish
   let k=0;while(g.race===r&&k++<200)g.update(0.05);
   const res=g.G.results[g.G.results.length-1];
   return {s:res.start,f:res.finish,e:res.expected,dnf:res.dnf?1:0,pl:res.plays,su:res.succ,cr:res.crits,m:res.total,beats:S.beats,slow:S.slowReal,dur:S.t,hand:S.handS/Math.max(1,S.samples),dead:S.deadS/Math.max(1,S.samples),idle:S.idleMax,hz:S.hz,ndnf:res.order.filter(x=>x.dnf).length,empty:S.emptyS/Math.max(1,S.samples),fol:r.stats.follow||0,combos:r.stats.combos||0,chain:r.stats.bestChain||0,board:r.stats.boardFires||0,exp:r.stats.expired||0,toss:r.stats.tossed||0,dry,stops:r.stats.pitStops||0,perfect:r.stats.perfect||0,setups:r.stats.setups||0,pk:r.stats.packages||0,stopT:(r.stats.stopTimes||[]).reduce((a,b)=>a+b,0)/Math.max(1,(r.stats.stopTimes||[]).length),cautions:r.cautions,hist:g.G.hist,tfx:S.tossFx,tlive:r.stats.tossLive||0,rref:r.stats.riskRefills||0,fgain:r.stats.focusGain||0,odds:S.odds.length?S.odds.reduce((a,b)=>a+b,0)/S.odds.length:0};
  }
  const R={};
  for(const arch of (bal.archs||['drafter','bully','whisperer','restart']))for(const trk of (bal.trks||[0,1,2]))for(const pol of pols){
   const k=`${arch}|${trk}|${pol}`;R[k]=[];for(let i=0;i<N;i++)R[k].push(runRace(arch,trk,pol));}
  return R;
 },{N,bal,pols});
 require('fs').writeFileSync(out,JSON.stringify(res));
 await b.close();
})();
