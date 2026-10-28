// crew-smoke.js /abs/slice.html [shotDir] : step 19 Crew HQ.
// The garage links to Crew HQ (crew training left the shop); four staff with levels, XP and perk tiers; training charges the
// driver coaching price curve; perks lock in with two taps and only from their level; level 6 and 8 calls stay out of the radio until
// unlocked; a forced call pays XP and runs its effect; XP levels a member up; a hire replaces the seat with a trait and a
// wage that comes off the next payout; pit cards upgrade (+10%) and swap in from the dealer; pit crew perks change the stop;
// saves from before step 19 still load; phone layout has no horizontal scroll.
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
  const fin=async()=>p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let k=0;
   while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}
   window.__gfdHeadless=false;g.saveSeason();return g.G.results[g.G.results.length-1];});
  const goRace=async()=>{await click('#g-go');if(await p.isVisible('#q-skip'))await click('#q-skip');await click('#q-race');};

  await load();await p.evaluate(()=>localStorage.clear());await load();
  await click('#t-go');
  const c0=await p.evaluate(()=>{const G=window.__gfd.G;return {roles:Object.keys(G.crew),pit:G.stats.pitCrew,chief:G.stats.crewChief,deck:G.pitDeck&&G.pitDeck.length,
   shopCrew:[...document.querySelectorAll('#garage [data-buy]')].some(b=>['crewChief','engineer','spotter'].includes(b.dataset.buy)),btn:!!document.getElementById('g-crew')};});
  if(c0.roles.join()!=='crewChief,spotter,engineer,pitCrew'||c0.pit!==c0.chief||c0.deck!==16||c0.shopCrew||!c0.btn)fail('new crew / garage '+JSON.stringify(c0));
  await click('#g-spons [data-sign]');
  await p.evaluate(()=>{window.__gfd.G.money=80000;window.__gfd.showGarage();});
  await click('#g-crew');
  const h0=await p.evaluate(()=>({staff:document.querySelectorAll('#crewhq .cs').length,locked:document.querySelectorAll('#crewhq .perk.lock').length,hire:document.querySelectorAll('#crewhq [data-hire]').length,
   pit:document.querySelectorAll('#crewhq .pitdeck .pcard').length,screen:window.__gfd.G&&document.getElementById('crewhq').hidden===false}));
  if(h0.staff!==4||h0.locked!==24||h0.hire!==3||h0.pit!==19||!h0.screen)fail('Crew HQ layout '+JSON.stringify(h0));
  if(await noScroll('crewhq'))fail(W+': Crew HQ scroll');
  await shot('crewhq');
  // training: driver coaching price curve, +1 level each
  const m0=await p.evaluate(()=>window.__gfd.G.money);
  for(let i=0;i<3;i++)await click('[data-train="spotter"]');
  const t1=await p.evaluate(()=>({lv:window.__gfd.G.stats.spotter,m:window.__gfd.G.money}));
  if(t1.lv!==6||m0-t1.m!==3000+4200+5400)fail('training '+JSON.stringify(t1)+' from '+m0);
  // perks: a locked tier can't be picked; one tap arms, the second locks it in for good
  await p.evaluate(()=>document.querySelector('[data-perk="spotter|2|a"]').click());
  await click('[data-perk="spotter|0|a"]');
  const armed=await p.evaluate(()=>({arm:!!document.querySelector('[data-perk="spotter|0|a"]').dataset.armed,perks:window.__gfd.G.crew.spotter.perks}));
  if(!armed.arm||Object.keys(armed.perks).length)fail('perk first tap '+JSON.stringify(armed));
  await click('[data-perk="spotter|0|a"]');
  await click('[data-perk="pitCrew|0|a"]').catch(()=>{});
  const pk=await p.evaluate(()=>{const G=window.__gfd.G;return {sp:G.crew.spotter.perks,off:document.querySelector('[data-perk="spotter|0|b"]').classList.contains('off'),fx:window.__gfd.crewFx('chance','spotter')};});
  if(pk.sp[0]!=='a'||pk.sp[2]||!pk.off||pk.fx!==5)fail('perk lock-in '+JSON.stringify(pk));
  // pit cards: upgrade +10%, dealer swap replaces the card you pick
  await click('[data-pup="0"]');
  await click('[data-pbuy="swing_man"]');await click('[data-pswap="15"]');
  const pc=await p.evaluate(()=>{const g=window.__gfd,G=g.G,o=id=>{const d=g.PIT_CARDS[id];return d.base;};return {deck:[G.pitDeck[0],G.pitDeck[15]],b:[o('quick_jack'),o('quick_jack+')],len:G.pitDeck.length};});
  if(pc.deck[0]!=='quick_jack+'||pc.deck[1]!=='swing_man'||pc.b[1]-pc.b[0]!==10||pc.len!==16)fail('pit cards '+JSON.stringify(pc));
  // a hire takes the seat: trait, wage, level, perks fresh
  const hire=await p.evaluate(()=>window.__gfd.G.hire.list[0]);
  await click('[data-hire="0"]');await click('[data-hire="0"]');
  const hd=await p.evaluate(r=>{const G=window.__gfd.G;return {m:G.crew[r],lv:G.stats[r]};},hire.role);
  if(hd.m.name!==hire.name||hd.m.trait!==hire.trait||hd.m.wage!==hire.wage||hd.lv<hire.lvl||Object.keys(hd.m.perks).length)fail('hire '+JSON.stringify({hire,hd}));
  await shot('crewhq-after');
  // level-gated calls: never offered below the level, offered once unlocked
  await click('#c-back');
  const gar=await p.evaluate(()=>!document.getElementById('garage').hidden&&!!document.getElementById('g-crew'));if(!gar)fail('back to garage');
  await goRace();
  const calls=await p.evaluate(()=>{const g=window.__gfd,G=g.G,r=g.race,seen=lv=>{for(const k of ['crewChief','engineer'])G.stats[k]=lv;const s=new Set();
    for(let i=0;i<400;i++){r.radio=null;g.spawnRadio();if(r.radio&&r.radio.nm)s.add(r.radio.nm);}r.radio=null;return [...s];};
   const lo=seen(3),hi=seen(8);for(const k of ['crewChief','engineer'])G.stats[k]=3;return {lo,hi};});
  const big=['Fuel stretch','Engine map','Setup trick'];
  if(calls.lo.some(n=>big.includes(n))||!big.every(n=>calls.hi.includes(n)))fail('level-gated calls '+JSON.stringify(calls));
  // a forced call works: effect + XP for that member
  const fc=await p.evaluate(()=>{const g=window.__gfd,r=g.race,c=g.RADIO_CALLS.find(x=>x.nm==='Fuel stretch');const was=r.pitBonus||0;
   r.radio={...c,until:r.t+7,dur:7};g.G.stats.crewChief=6;const mr=Math.random;Math.random=()=>0;
   r.radio.chance=60;r.radio.q=1;r.radio.qf=1;g.acceptRadio();Math.random=mr;g.G.stats.crewChief=3;return {d:(r.pitBonus||0)-was,xp:r.crewXp&&r.crewXp.crewChief,calls:r.stats.calls,won:r.stats.callsWon};});
  if(Math.abs(fc.d-1.5)>1e-9||fc.xp!==6||fc.calls!==1||fc.won!==1)fail('forced call '+JSON.stringify(fc));
  // XP near the line levels the member up at the finish; the wage comes off the payout
  await p.evaluate(()=>{const G=window.__gfd.G;G.crew.engineer.xp=window.__gfd.CREW_XP(G.stats.engineer)-1;window.__gfd.race.crewXp=Object.assign(window.__gfd.race.crewXp||{},{engineer:5});});
  const lv0=await p.evaluate(()=>window.__gfd.G.stats.engineer);
  const res=await fin();
  const wages=await p.evaluate(()=>{const G=window.__gfd.G;return ['crewChief','spotter','engineer','pitCrew'].reduce((s,k)=>s+(G.crew[k].wage||0),0);});
  const sum=res.purse+res.bonus+(res.retainer||0)+(res.salary||0)-(res.wages||0)+res.objs.filter(o=>o.ok).reduce((s,o)=>s+o.pay,0);
  if(res.wages!==wages||res.total!==sum||!wages)fail('wages '+JSON.stringify({w:res.wages,wages,total:res.total,sum}));
  const lv1=await p.evaluate(()=>window.__gfd.G.stats.engineer);
  if(lv1!==lv0+1||!(res.crewUp||[]).includes('engineer'))fail('XP level-up '+lv0+'->'+lv1+' '+JSON.stringify(res.crewUp));
  const wc=await p.evaluate(()=>(document.getElementById('w-crew')||{}).innerText||'');
  if(!/Engineer \+\d+ XP/.test(wc)||!/Engineer level/.test(wc)||!/Wages/.test(wc))fail('reward crew panel: '+wc);
  await shot('reward-crew');
  // pit crew perks reach the stop: Deep bench (+1 pit card), Drilled (+0.15 s window), Body men (Fix 2 s, +10)
  const pp=await p.evaluate(()=>{const g=window.__gfd,G=g.G;G.crew.pitCrew.trait=null;G.stats.pitCrew=8;G.crew.pitCrew.perks={0:'a',1:'b',2:'b'};
   const out={hand:g.crewFx('pitHand'),fix:[g.PIT_CARDS.fix.time,g.PIT_CARDS.fix.fix]};G.crew.pitCrew.perks={0:'a',1:'a',2:'a'};out.win=g.crewFx('relWin');out.cut=g.crewFx('laneCut');
   G.crew.pitCrew.perks={};G.stats.pitCrew=3;out.fix0=[g.PIT_CARDS.fix.time,g.PIT_CARDS.fix.fix];return out;});
  if(pp.hand!==1||pp.fix.join()!=='2,30'||pp.win!==0.15||pp.cut!==0.4||pp.fix0.join()!=='4,20')fail('pit crew perks '+JSON.stringify(pp));
  // a stop with the bench perk draws 6 pit cards
  const st=await p.evaluate(()=>{const g=window.__gfd,G=g.G;G.stats.pitCrew=6;G.crew.pitCrew.perks={0:'a',1:'b'};G.nextObjectives=null;G._pti=null;
   g.startRace();const r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let n=null,k=0;const _o=window.pgOpen;
   while(g.race===r&&k++<30000&&n==null){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);const m=g.meCar();if(r.phase==='green'&&!m.pit&&!r.pitReq){const pl=g.myPlan();if(pl&&g.lapOf(m)>=pl.lap)g.pitButton();}
    if(r.pg2&&n==null)n=r.pg2.hand.length-(r.pg2.prep||0);}
   while(g.race===r&&k++<40000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}window.__gfdHeadless=false;G.crew.pitCrew.perks={};return n;});
  if(st!==6)fail('Deep bench pit hand '+st);
  // old save from before step 19: no crew, no pit crew stat
  await p.evaluate(()=>{const g=window.__gfd;g.saveSeason();const s=g.readSave(),G=s.G;delete G.crew;delete G.stats.pitCrew;delete G.hire;G.reward=null;G.inRace=null;G.snap=null;localStorage.setItem('gfd-season-v1',JSON.stringify(s));});
  await load();await click('#t-cont');
  const old=await p.evaluate(()=>{const G=window.__gfd.G;return {crew:G.crew&&Object.keys(G.crew).length,pit:G.stats.pitCrew,chief:G.stats.crewChief,g:!document.getElementById('garage').hidden||!document.getElementById('event').hidden};});
  if(old.crew!==4||old.pit!==old.chief||!old.g)fail('old save '+JSON.stringify(old));
  await p.close();
 }
 if(errs.length)fail('page errors: '+errs.join(' | '));
 console.log(process.exitCode?'crew smoke FAILED':'crew smoke ok');
 await b.close();
})();
