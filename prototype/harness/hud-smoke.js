// hud-smoke.js /abs/slice.html [shotDir] : HUD round 3 through the real mouse.
// Checks: dragging a card onto the toss bin tosses it, a toss buff shows as a countdown tile, hovering a card it boosts makes
// that tile glow, a banked buff shows as a READY tile, full Momentum / Heat get the pulsing outline, and the hand never scrolls vertically.
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const errs=[];const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 for(const [W,H] of [[1600,900],[390,844]]){
  const p=await b.newPage({viewport:{width:W,height:H}});p.on('pageerror',e=>errs.push(e.message));
  await p.goto('file://'+html);await p.waitForTimeout(300);await p.evaluate(()=>localStorage.clear());await p.goto('file://'+html);await p.waitForTimeout(300);
  await p.click('[data-series="mixed"]');await p.waitForTimeout(80);await p.click('#t-go');await p.waitForTimeout(200);if(await p.$('#g-spons [data-sign]')){await p.click('#g-spons [data-sign]');await p.waitForTimeout(100);}if(await p.isVisible('#g-go')){await p.click('#g-go');await p.waitForTimeout(150);await p.click('#q-skip');await p.waitForTimeout(100);await p.click('#q-race');}await p.waitForTimeout(300);
  await p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;let k=0;while(r.t<40&&k++<20000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}window.__gfdHeadless=false;r.paused=true;
   r.hand=['side_draft','bump_draft','crossover','dive_bomb','tuck_in','save_tires'].map((id,i)=>({id,uid:'u'+i}));r.momentum=5;r.heat=5;r.buffs=[{v:6,fam:'draft',src:'Engineer call'}];renderHand();});
  await p.waitForTimeout(400);
  if(!(await p.$('#hudcore #focusbar'))||!(await p.$('#hudcore #combo')))fail(W+': Focus and combo are not in one console');
  const ov=await p.$eval('#hand',e=>e.scrollHeight-e.clientHeight);if(ov>0&&(await p.$eval('#hand',e=>getComputedStyle(e).overflowY))!=='hidden')fail(W+': hand scrolls vertically');
  const full=await p.evaluate(()=>[document.querySelector('#m-mom').classList.contains('full'),document.querySelector('#m-heat').classList.contains('full')]);if(!full[0]||!full[1])fail(W+': full outline '+full);
  const bg=await p.$eval('#m-mom',e=>getComputedStyle(e).backgroundColor);if(bg!=='rgba(0, 0, 0, 0)')fail(W+': momentum module has a color fill '+bg);
  const c=await (await p.$('.hcard[data-uid="u0"]')).boundingBox(),bin=await (await p.$('#tossbin')).boundingBox();
  await p.mouse.move(c.x+c.width/2,c.y+c.height/2);await p.mouse.down();await p.mouse.move(c.x+c.width/2,c.y-30,{steps:5});
  await p.mouse.move(bin.x+bin.width/2,bin.y+bin.height/2,{steps:8});await p.waitForTimeout(150);
  if(!(await p.$('.dragghost.willtoss')))fail(W+': ghost not marked over the bin');
  await p.mouse.up();await p.waitForTimeout(700);
  const st=await p.evaluate(()=>{const r=window.__gfd.race;return {tossed:!r.hand.some(h=>h.uid==='u0'),buff:(r.tossBuffs||[]).map(b=>b.fam),tiles:[...document.querySelectorAll('#tossfx .tsk')].map(e=>e.className)};});
  if(!st.tossed)fail(W+': drop on bin did not toss');if(!st.buff.includes('pass'))fail(W+': no pass buff '+JSON.stringify(st));
  if(!st.tiles.some(t=>t.includes('k-buff')))fail(W+': no countdown tile');if(!st.tiles.some(t=>t.includes('k-bank')&&t.includes('wait')))fail(W+': no ready tile');
  if(W>700){const k=await (await p.$('.hcard[data-uid="u2"]')).boundingBox();await p.mouse.move(k.x+k.width/2,k.y+k.height/2);await p.waitForTimeout(400);
   if(!(await p.$('#tossfx .tsk.k-buff.glow')))fail(W+': hovering a passing card did not light the passing tile');}
  if(shots)await p.screenshot({path:`${shots}/hud-${W}.png`});
  await p.close();}
 console.log(process.exitCode?'hud smoke FAILED':'hud smoke ok');console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;await b.close();})();
