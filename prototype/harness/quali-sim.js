// quali-sim.js /abs/slice.html [N] [statBoost] : step 16 balance check. For each track and bot skill, runs N qualifying
// sessions (new career each time, the four personas in turn) with the game's own qualifying code (qualiAuto) and prints the
// average starting spot. skip = the Skip button (an average lap, the same odds as the old roll-off). poor/avg/good/great =
// bots with timing error 0.07/0.045/0.028/0.018 bar widths that spend Focus on the first corners; "+card" picks the best
// of the three offered cards. Done when: good is 4+ places ahead of skip on average.
const {chromium}=require('playwright');
const [html,N,boost]=[process.argv[2],+process.argv[3]||400,+process.argv[4]||0];
(async()=>{const b=await chromium.launch();const p=await b.newPage();p.on('pageerror',e=>console.log('PAGEERR',e.message));
 await p.goto('file://'+html);await p.waitForTimeout(300);
 const R=await p.evaluate(({N,boost})=>{window.__gfdHeadless=true;const g=window.__gfd,arch=['drafter','bully','whisperer','restart'];
  const rows=[];const profs=[['skip',null],['poor',null],['avg',null],['good',null],['great',null],['good','best']];
  for(let ri=0;ri<5;ri++){const row={track:null};
   for(const [pf,cd] of profs){let s=0,pts=0;
    for(let i=0;i<N;i++){g.titleSel.arch=arch[i%4];g.newCareer();if(boost)for(const k of ['power','handling','aero','racecraft','consistency'])g.G.stats[k]+=boost;g.G.raceIdx=ri;
     const q=g.qualiAuto(pf,cd);s+=q.pos;pts+=q.avg||0;row.track=g.TRACKS[g.arcTi(ri)].short;}
    row[pf+(cd?'+card':'')]=+(s/N).toFixed(2);row[pf+(cd?'+card':'')+' pts']=+(pts/N).toFixed(2);}
   rows.push(row);}
  return rows;},{N,boost});
 const keys=['skip','poor','avg','good','great','good+card'];
 console.log('track       '+keys.map(k=>k.padStart(10)).join(''));
 const avg={};for(const r of R){console.log(r.track.padEnd(12)+keys.map(k=>String(r[k]).padStart(10)).join(''));for(const k of keys)avg[k]=(avg[k]||0)+r[k]/R.length;}
 console.log('ALL'.padEnd(12)+keys.map(k=>avg[k].toFixed(2).padStart(10)).join(''));
 console.log('pts/corner  '+keys.map(k=>(R.reduce((s,r)=>s+(r[k+' pts']||0),0)/R.length).toFixed(2).padStart(10)).join(''));
 console.log(`good vs skip: ${(avg.skip-avg.good).toFixed(2)} places (need 4+)`);
 await b.close();})();
