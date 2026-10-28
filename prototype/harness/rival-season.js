// rival-season.js N /abs/slice.html [policies] [off] : step 20 balance check for rivals over whole seasons.
// Plays N seeded 10-race seasons per policy in one career each (Grudge carries race to race), with a bot that looks at the hand
// every 2.5 s: 'human' plays the best card at 65%+ with under 5% wreck, 'spam' always plays the best card.
// Prints per policy: average finish, DNF rate, rival wrecks and paybacks per season, races with a rival at Warm/Hot,
// and the average Grudge at the end. Pass 'off' to run the same seeds with the rival effects switched off (Grudge still counts).
const {chromium}=require('playwright');
const [N,html,pols,off]=[+process.argv[2]||4,process.argv[3],(process.argv[4]||'human,spam').split(','),process.argv[5]==='off'];
(async()=>{const b=await chromium.launch();const p=await b.newPage();p.on('pageerror',e=>console.log('PAGEERR',e.message));
 await p.goto('file://'+html);await p.waitForTimeout(300);
 const out=await p.evaluate(({N,pols,off})=>{window.__gfdHeadless=true;window.__gfdPitPolicy='human';const g=window.__gfd;
  function seedRng(s){let t=s>>>0;Math.random=function(){t+=0x6D2B79F5;let r=Math.imul(t^t>>>15,1|t);r^=r+Math.imul(r^r>>>7,61|r);return ((r^r>>>14)>>>0)/4294967296;};}
  if(off){window.rivalAttack=()=>0;window.rivalOdds=()=>{};window.rivalBlock=()=>1;}
  const res={};
  for(const pol of pols){const R={fin:0,dnf:0,n:0,wrecks:0,pay:0,warm:0,hot:0,gEnd:0,objOk:0,objN:0};
   for(let s=0;s<N;s++){seedRng(9001+s*7919);
    g.titleSel.arch=['drafter','bully','whisperer','restart','gambler'][s%5];g.titleSel.diff='normal';g.titleSel.info='full';g.titleSel.stakes='standard';
    g.newCareer();const G=g.G;
    for(let ri=0;ri<10;ri++){G.raceIdx=ri;G.nextObjectives=g.rollRaceObjectives();G.event=null;G.reward=null;
     const top=Math.max(...Object.values(G.riv).map(x=>x.g));if(top>=3)R.warm++;if(top>=6)R.hot++;
     window.__gfdHeadless=false;g.startRace();window.__gfdHeadless=true;const r=g.race;let look=0,k=0;
     while(g.race===r&&k++<40000){if(r.paused){r.paused=false;r._beatPaused=false;}
      if(r.t>=look&&!r.attempt&&r.phase!=='finished'){look=r.t+2.5;const m=g.meCar();if(!m.dnf){const c=g.ctxFor(m);
       const cands=r.hand.map(h=>{const cd=g.CARDS[h.id];if(g.cardBlock(cd,c))return null;return {h,o:g.computeOdds(h.id,c)};}).filter(Boolean).sort((a,b)=>b.o.succ-a.o.succ);
       const pick=pol==='spam'?cands[0]:cands.find(x=>x.o.succ>=0.65&&x.o.wreck<0.05);if(pick)g.playCard(pick.h.uid);}}
      g.update(0.05);}
     const x=G.results[G.results.length-1];R.n++;R.fin+=x.finish;if(x.dnf)R.dnf++;R.objN+=x.objs.filter(o=>o.rv).length;R.objOk+=x.objs.filter(o=>o.rv&&o.ok).length;
     R.wrecks+=(r.rinc||[]).filter(i=>i.k==='wreck').length;R.pay+=(r.rinc||[]).filter(i=>i.k==='payback').length;}
    R.gEnd+=Object.values(G.riv).reduce((a,x)=>a+x.g,0)/6;}
   res[pol]={avgFin:+(R.fin/R.n).toFixed(2),dnf:+(R.dnf/R.n*100).toFixed(1)+'%',rivalWrecksPerSeason:+(R.wrecks/N).toFixed(2),paybacksPerSeason:+(R.pay/N).toFixed(2),
    racesWithWarmRival:+(R.warm/R.n*100).toFixed(0)+'%',racesWithHotRival:+(R.hot/R.n*100).toFixed(0)+'%',avgGrudgeAtSeasonEnd:+(R.gEnd/N).toFixed(2),rivalObjMet:R.objN?+(R.objOk/R.objN*100).toFixed(0)+'%':'-'};}
  return res;},{N,pols,off});
 console.log(JSON.stringify({off,N,...out},null,1));await b.close();})();
