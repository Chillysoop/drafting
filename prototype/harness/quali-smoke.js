// quali-smoke.js /abs/slice.html [shotDir] : step 16 qualifying through the real screens. Picks a card, plays the run with
// real Space key events (tap near the zone, one hold for slow-mo), checks the grid, that the race starts you where you
// qualified with the card in the discard pile, that a reload mid-race keeps the grid, and that the phone layout fits.
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const errs=[];const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 for(const [W,H] of [[1280,900],[390,844]]){
  const p=await b.newPage({viewport:{width:W,height:H}});p.on('pageerror',e=>errs.push(e.message));
  const shot=async n=>{if(shots)await p.screenshot({path:`${shots}/${n}-${W}.png`});};
  const load=async()=>{await p.goto('file://'+html);await p.waitForTimeout(350);};
  const click=async sel=>{await p.click(sel);await p.waitForTimeout(80);};
  await load();await p.evaluate(()=>localStorage.clear());await load();
  await click('#t-go');await click('#g-spons [data-sign]');await click('#g-go');
  if(await p.evaluate(()=>document.getElementById('quali').hidden))fail(W+': Go racing did not open qualifying');
  const nc=await p.$$eval('#quali .qcard[data-qc]',e=>e.length);if(nc<3)fail(W+': card offer '+nc);
  await click('#quali .qcard[data-qc]:not(.qnone)');
  const card=await p.evaluate(()=>window.__gfd.QS.card);if(!card)fail(W+': card pick');
  await shot('quali-pick');
  await p.evaluate(()=>{window.__gfd.QL.timeK=2.5;});
  await click('#q-go');
  // in-page bot: real keydown/keyup Space events; holds once through a sweep to check Focus drains
  const r=await p.evaluate(()=>new Promise(res=>{const g=window.__gfd,ev=(t)=>document.dispatchEvent(new KeyboardEvent(t,{code:'Space',key:' ',bubbles:true}));
   let held=false,heldOnce=false,f0=null,drained=false;
   const tick=()=>{const Q=g.QS;if(!Q||Q.phase!=='run'){res({drained,held:heldOnce});return;}
    const c=Q.pl.checks.find(c=>!c.res&&Q.time<c.s1);
    if(c&&Q.time>=c.s0){const x=(Q.time-c.s0)/(c.s1-c.s0);
     if(!heldOnce&&!held&&x<0.3){ev('keydown');held=true;heldOnce=true;f0=Q.focus;}
     if(held&&x>=c.c-0.004){ev('keyup');held=false;drained=Q.focus<f0;}
     else if(!held&&x>=c.c-0.012&&!c.res){ev('keydown');ev('keyup');}}
    requestAnimationFrame(tick);};
   requestAnimationFrame(tick);
   setTimeout(()=>{const Q=g.QS;if(Q&&Q.phase==='run'&&!window.__shot){window.__shot=1;}},3000);}));
  if(!r.drained)fail(W+': holding Space did not use Focus');
  const q=await p.evaluate(()=>{const g=window.__gfd.G.qual;return {pos:g.pos,n:g.grid.length,counts:g.counts,card:g.card,rows:document.querySelectorAll('#quali .q-grid li').length,me:document.querySelector('#quali .q-grid li.me')?.innerText};});
  console.log(W,'qualified',JSON.stringify(q));
  if(q.n!==20||q.rows!==20)fail(W+': grid size');
  if(!q.me||!q.me.startsWith('P'+q.pos))fail(W+': your grid row '+q.me);
  if(!((q.counts.P||0)+(q.counts.G||0)>=6))fail(W+': bot timing too poor (input lag?) '+JSON.stringify(q.counts));
  await shot('quali-done');
  await p.keyboard.press('Enter');await p.waitForTimeout(200);
  const st=await p.evaluate(()=>{const g=window.__gfd,r=g.race,m=g.meCar();return {race:!document.getElementById('race').hidden,start:m.start,disc:r.discard.slice(),pile:r.pile.length+r.hand.length};});
  if(!st.race)fail(W+': Enter did not start the race');
  if(st.start!==q.pos)fail(W+': start P'+st.start+' != qualified P'+q.pos);
  if(!st.disc.includes(card))fail(W+': qualifying card not in the discard pile');
  if(W===1280){ // reload mid-race: continue restarts the race from the same grid
   await p.evaluate(()=>{const g=window.__gfd;for(let k=0;k<300;k++)g.update(0.05);});
   await load();await click('#t-cont');await click('#g-go');
   const again=await p.evaluate(()=>!document.getElementById('quali').hidden&&window.__gfd.QS.phase==='done'&&window.__gfd.G.qual.pos);
   if(again!==q.pos)fail('after reload qualifying is not kept '+again);
   await click('#q-race');const s2=await p.evaluate(()=>window.__gfd.meCar().start);if(s2!==q.pos)fail('restart grid '+s2);
   // a mouse hold on the BRAKE button, and Skip, on the next race
   await p.evaluate(()=>{const g=window.__gfd;g.G.raceIdx++;g.G.qual=null;g.showGarage();});await click('#g-go');await click('#q-go');
   await p.waitForTimeout(900);const bb=await p.$('#q-brake');const bx=await bb.boundingBox();await p.mouse.move(bx.x+bx.width/2,bx.y+bx.height/2);await p.mouse.down();
   await p.waitForTimeout(500);await shot('quali-run');const held=await p.evaluate(()=>window.__gfd.QS.held);await p.mouse.up();if(held!=='ptr')fail('brake button hold');
   await click('#q-quit');const sk=await p.evaluate(()=>window.__gfd.G.qual);if(!sk.skip||sk.card)fail('give up = average lap');
  }else{await p.evaluate(()=>{const g=window.__gfd;g.G.raceIdx++;g.G.qual=null;g.showGarage();});await click('#g-go');await click('#q-go');await p.waitForTimeout(1500);await shot('quali-run');
   const ov=await p.evaluate(()=>document.documentElement.scrollWidth>innerWidth+1);if(ov)fail('phone: horizontal scroll');}
  await p.close();
 }
 if(errs.length)fail('page errors: '+errs.join(' | '));
 await b.close();if(!process.exitCode)console.log('quali smoke ok');
})();
