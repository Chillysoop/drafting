// smoke.js /abs/slice.html : loads the page (UI + headless race on all 5 tracks), fails on page errors
const {chromium}=require('playwright');
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:800}});const errs=[];p.on('pageerror',e=>errs.push(e.message));
await p.goto('file://'+process.argv[2]);await p.waitForTimeout(800);
await p.screenshot({path:process.argv[3]||'/tmp/smoke-title.png'});
const r=await p.evaluate(()=>{window.__gfdHeadless=true;const g=window.__gfd;const out=[];
 for(let trk=0;trk<5;trk++){g.titleSel.arch=['drafter','bully','whisperer','restart','gambler'][trk];g.newCareer();g.setTune({focus:'draft',hand:-2});g.G.raceIdx=trk;g.startRace();const r=g.race;let k=0;
  while(g.race===r&&r.phase!=='finished'&&k++<20000){g.update(0.05);const c=g.meCar();if(!r.attempt&&r.phase==='green'){const h=r.hand.find(h=>!g.cardBlock(g.CARDS[h.id],g.ctxFor(c)));if(h)g.playCard(h.uid);}}
  out.push(`${r.track.id} ${r.phase} plays=${r.stats.plays} chief=${g.BAL.chiefLate} odds=${g.BAL.oddsAdj}`);}
 return out;});
console.log(r.join('\n'));console.log('pageerrors',errs.length,errs.slice(0,3));await b.close();process.exit(errs.length?1:0);})();
