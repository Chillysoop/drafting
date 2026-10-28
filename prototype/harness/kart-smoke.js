// kart-smoke.js /abs/slice.html [shotDir] : step 21 kart rung.
// The title's "Start in: Karts" swaps the personas for the four kart ones and the season for the kart series; a kart career
// starts with a 12-card kart deck (10 to 13 allowed) and only kart cards. Through the real garage and qualifying screens into
// a kart race: the pit module is the Kart module (Tow and Fairing), PIT does nothing, a wreck brings no caution, and the race
// is a 13-14 lap sprint on a kart circuit. Kart combos chain card to card: Show the Inside marks the kart ahead as covering
// and Switchback gets +20 on him, Around the Outside links into Own the Inside only when the next corner turns the other way,
// the window is 3 s shorter than a stock car's and a chain stops at x3. Tow pips feed Pull Out; three scruffy pushes put the
// fairing in and the 5 s penalty drops you behind the karts within 5 s at the flag (and shows on the reward screen). Rewards
// offer kart cards and no stock-only relics, the save keeps the kart series, a stock career never sees kart cards, and the
// phone layout has no horizontal scroll.
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
  const toGreen=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0;while(r.phase!=='green'&&k++<400){r.paused=false;g.update(0.05);}window.__gfdHeadless=false;return r.phase;});
  const fin=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0;
   while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}
   window.__gfdHeadless=false;g.saveSeason();return g.G.results[g.G.results.length-1];});

  await load();await p.evaluate(()=>localStorage.clear());await load();
  // title: Start in Karts
  const t0=await p.evaluate(()=>[...document.querySelectorAll('#title .personas [data-arch]')].map(b=>b.dataset.arch).join());
  if(t0!=='brawler,linepainter,packrat,latebraker')fail('kart personas by default on the title (Q77) '+t0);
  await click('[data-series="kart"]');
  const t1=await p.evaluate(()=>({arch:[...document.querySelectorAll('#title .personas [data-arch]')].map(b=>b.dataset.arch).join(),intro:!!document.querySelector('#title .kart-intro'),
   sched:window.__gfd.titleSel.sched,cal:[...document.querySelectorAll('#title .g2 .row')].map(r=>r.innerText).join('|')}));
  if(t1.arch!=='brawler,linepainter,packrat,latebraker'||!t1.intro||t1.sched!=='kart'||!/Hollow Creek/.test(t1.cal)||!/Pinewood/.test(t1.cal)||/Millbrook/.test(t1.cal))fail('kart title '+JSON.stringify(t1));
  if(await noScroll('title'))fail(W+': title scroll');
  await p.evaluate(()=>document.querySelector('#title .series-pick').scrollIntoView());await shot('kart-title');
  await click('[data-arch="brawler"]');
  await click('#t-go');
  const c0=await p.evaluate(()=>{const g=window.__gfd,G=g.G;return {series:G.series,ct:G.carType,deck:G.deck.length,allKart:G.coll.every(id=>g.CARDS[id].kart),size:g.deckSize(),track:g.TRACKS[g.arcTi(G.raceIdx)].id,tune:g.tuneLabel()};});
  if(c0.series!=='kart'||c0.ct!=='kart'||c0.deck!==12||!c0.allKart||c0.size.min!==10||c0.size.max!==14||c0.track!=='pinek'||!/Short gear/.test(c0.tune))fail('kart career '+JSON.stringify(c0));
  if(await noScroll('garage'))fail(W+': garage scroll');
  await shot('kart-garage');
  // into the race through the real garage and qualifying screens
  await click('#g-spons [data-sign]');
  if(await p.isVisible('#g-ev-walk'))await click('#g-ev-walk');
  await click('#g-go');if(await p.isVisible('#q-skip'))await click('#q-skip');await click('#q-race');
  const r0=await p.evaluate(()=>{const r=window.__gfd.race;return {id:r.track.id,laps:r.track.laps,kart:document.body.classList.contains('kart-race'),mod:document.getElementById('pitmod').innerText};});
  if(r0.id!=='pinek'||r0.laps<13||!r0.kart||!/TOW/.test(r0.mod)||!/FAIRING/.test(r0.mod)||/FUEL/.test(r0.mod))fail('kart race '+JSON.stringify(r0));
  if(await toGreen()!=='green')fail('no green flag');
  const m1=await p.evaluate(()=>{const g=window.__gfd,r=g.race,m=g.meCar();g.pitButton();const req=r.pitReq;
   // a wreck is a local yellow: no caution, no pit road
   const o=r.order.find(c=>!c.isMe&&!c.dnf);wreck(o,null,'spins');for(let i=0;i<40;i++)g.update(0.05);
   return {req,phase:r.phase,cautions:r.cautions,win:comboWin(),fuel:m.fuel>50};});
  if(m1.req||m1.phase!=='green'||m1.cautions||!m1.fuel)fail('no pits, no cautions '+JSON.stringify(m1));
  // combos: Show the Inside -> Switchback on a covering kart
  const k1=await p.evaluate(()=>{const g=window.__gfd,r=g.race,m=g.meCar();window.__gfdHeadless=true;
   // park a kart right ahead of you, mid-corner
   let k=0;while(k++<4000){g.update(0.05);const c=g.ctxFor(m);if(c.ahead&&c.gapA<0.5&&c.seg.turn)break;}
   const c=g.ctxFor(m),a=c.ahead;const before=g.computeOdds('k_switch',c);
   r.chain=1;r.chainFam='pass';r.chainId='k_show';r.chainAt=r.t;r.chainRoot='pass';a.kCover=r.t+6;
   const after=g.computeOdds('k_switch',c),links=comboFor(g.CARDS.k_switch),noLink=comboFor(g.CARDS.k_owninside);
   r.chain=3;const capped=comboFor(g.CARDS.k_switch);r.chain=1;
   r.chainAt=r.t-(comboWin()+0.1);const late=comboFor(g.CARDS.k_switch);
   // Own the Inside links after Around the Outside only when this corner turns the other way
   const dir=c.seg.dir;r.chainId='k_outside';r.chainAt=r.t;r.kOut={dir:-dir,at:r.t};const opp=comboFor(g.CARDS.k_owninside),oddsOpp=g.computeOdds('k_owninside',c).tags.some(t=>/became the inside/.test(t[1]));
   r.kOut={dir,at:r.t};const same=comboFor(g.CARDS.k_owninside);
   r.chain=0;r.chainFam=null;r.chainId=null;a.kCover=0;r.kOut=null;window.__gfdHeadless=false;
   return {cov:after.tags.some(t=>/covering the inside/.test(t[1])),up:after.read-before.read,links,noLink,capped,late,opp,oddsOpp,same,dir,win:comboWin(),stock:BAL.comboWin};});
  if(!k1.cov||k1.up<20||!k1.links||k1.noLink||k1.capped||k1.late)fail('show -> switchback '+JSON.stringify(k1));
  if(!k1.dir||!k1.opp||!k1.oddsOpp||k1.same)fail('outside -> own the inside '+JSON.stringify(k1));
  if(k1.win>k1.stock)fail('kart combo window '+JSON.stringify(k1));
  // tow pips feed Pull Out; the HUD shows them
  const tw=await p.evaluate(()=>{const g=window.__gfd,r=g.race,m=g.meCar(),c=g.ctxFor(m);r.tow=0;const blk=g.cardBlock(g.CARDS.k_pullout,Object.assign({},c,{gapA:0.3}));
   r.tow=2;const o=g.computeOdds('k_pullout',Object.assign({},c,{gapA:0.3})).tags.some(t=>/Tow ×2/.test(t[1]));renderPitMod(true);return {blk,o,pips:document.querySelectorAll('#pitmod .km-pips:not(.fair) i.on').length};});
  if(tw.blk!=='Needs a Tow pip'||!tw.o||tw.pips!==2)fail('tow pips '+JSON.stringify(tw));
  await shot('kart-race');
  // fairing: three scruffy pushes (four with the Nerf Bar the Brawler starts with) and the penalty at the flag
  const f1=await p.evaluate(()=>{const g=window.__gfd,r=g.race;const mx=window.__gfdKart.KART.fairMax+relicSum('fairMax');kFair(1);kFair(2);const early=r.fairOut;kFair(mx);renderPitMod(true);return {mx,early,fair:r.fair,out:r.fairOut,hud:document.querySelector('#pitmod .km-pips.fair.out')!=null};});
  if(f1.fair!==f1.mx||(f1.mx>3)!==!f1.early||!f1.out||!f1.hud)fail('fairing '+JSON.stringify(f1));
  const pen=await p.evaluate(()=>{const g=window.__gfd,r=g.race,m=g.meCar();const fo=[...r.order];const i=fo.indexOf(m);if(i>=fo.length-2)return {skip:1};
   // two karts 1 s and 2 s behind you, one 8 s behind
   const L=r.track.lapTime;fo[i+1].dist=m.dist-1/L;fo[i+2].dist=m.dist-2/L;if(fo[i+3])fo[i+3].dist=m.dist-8/L;
   r.finalOrder=fo;r.fairApplied=false;window.__gfdKart.kFairPenalty();const j=r.finalOrder.indexOf(m);r.fairApplied=false;r.finalOrder=null;return {i,j,lost:r.fairLost};});
  if(!pen.skip&&(pen.j!==pen.i+2||pen.lost!==2))fail('fairing penalty '+JSON.stringify(pen));
  const res=await fin();
  if(!res||!res.kart||!res.fair)fail('kart result '+JSON.stringify(res&&{kart:res.kart,fair:res.fair}));
  const rw=await p.evaluate(()=>{const g=window.__gfd,G=g.G;return {pen:!!document.querySelector('#reward .fair-pen'),offer:G.reward.cardOffer,allKart:G.reward.cardOffer.every(id=>g.CARDS[id].kart),relics:(G.reward.relicOffer||[]).filter(k=>['launch_control','pit_clipboard','fast_gun'].includes(k))};});
  if(!rw.pen||!rw.allKart||rw.relics.length)fail('kart reward '+JSON.stringify(rw));
  if(await noScroll('reward'))fail(W+': reward scroll');
  await p.evaluate(()=>{const e=document.querySelector('#reward .fair-pen');if(e)e.scrollIntoView();});await shot('kart-reward');
  // the save keeps the kart series; the next race is Hollow Creek
  await load();
  const sv=await p.evaluate(()=>{document.getElementById('t-cont').click();const g=window.__gfd,G=g.G;return {series:G.series,ct:G.carType,next:g.TRACKS[g.arcTi(G.raceIdx+(G.reward?1:0))].id};});
  if(sv.series!=='kart'||sv.ct!=='kart'||sv.next!=='hollow')fail('kart save '+JSON.stringify(sv));
  // a stock career never sees kart cards or kart tracks
  await p.evaluate(()=>localStorage.clear());await load();
  await click('[data-series="mixed"]');await click('#t-go');
  const st=await p.evaluate(()=>{const g=window.__gfd,G=g.G;const offers=[];for(let i=0;i<40;i++)offers.push(...g.CARDS&&rollCards('victory'));
   return {series:G.series,kartCards:offers.filter(id=>g.CARDS[id].kart).length,kartRelics:rollRelics(30).filter(k=>g.ARCH&&RELICS[k].kart).length,tracks:G.cars.length&&[...Array(10).keys()].map(i=>g.TRACKS[g.arcTi(i)].kart?1:0).reduce((a,b)=>a+b,0)};});
  if(st.series!=='stock'||st.kartCards||st.kartRelics||st.tracks)fail('stock career '+JSON.stringify(st));
  await p.close();
 }
 if(errs.length)fail('page errors: '+errs.slice(0,3).join(' | '));
 console.log(process.exitCode?'kart smoke FAILED':'kart smoke ok');await b.close();})();
