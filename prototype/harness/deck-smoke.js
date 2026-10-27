// deck-smoke.js /abs/slice.html [shotDir] : collection + race deck builder (step 15b) through the real buttons.
// Checks: new career has collection = deck, the builder blocks illegal decks, auto-build is legal, the tune moves slot caps,
// a reward pick lands in the collection, the garage fixes a deck that breaks the next track's rule, and old saves without a collection still load.
const {chromium}=require('playwright');
const html=process.argv[2],shots=process.argv[3]||null;
(async()=>{const b=await chromium.launch();const p=await b.newPage({viewport:{width:1280,height:900}});const errs=[];p.on('pageerror',e=>errs.push(e.message));
 const fail=m=>{console.log('FAIL',m);process.exitCode=1;};
 const shot=async n=>{if(shots)await p.screenshot({path:`${shots}/${n}.png`,fullPage:true});};
 const load=async()=>{await p.goto('file://'+html);await p.waitForTimeout(400);};
 const click=async sel=>{await p.click(sel);await p.waitForTimeout(60);};
 const G=()=>p.evaluate(()=>{const g=window.__gfd.G;return {deck:[...g.deck],coll:[...g.coll],tune:g.tune,idx:g.raceIdx};});
 const ok=()=>p.evaluate(()=>{const g=window.__gfd;return g.deckOk(g.TRACKS[g.arcTi(g.G.raceIdx)]);});
 await load();await p.evaluate(()=>localStorage.clear());await load();
 await click('#t-go');
 let s=await G();if(s.coll.length!==s.deck.length||s.deck.length!==12)fail('new career collection '+JSON.stringify(s));
 // give a mid-season collection so there are choices
 await p.evaluate(()=>{const g=window.__gfd;g.G.coll.push('slingshot','draft_train','crossover','clear_air','hot_laps','send_it','block','high_line','regroup','restart_jump');g.showGarage();});
 await shot('garage');
 const t0=Date.now();
 await click('#g-deck');if(await p.evaluate(()=>document.getElementById('deckb').hidden))fail('builder did not open');
 await click('#d-clear');if(!(await p.$eval('#d-done',e=>e.disabled)))fail('empty deck allowed');
 await click('#d-auto');s=await G();if(!(await ok()))fail('auto-build illegal '+JSON.stringify(s.deck));if(await p.$eval('#d-done',e=>e.disabled))fail('done disabled after auto');
 await shot('builder');
 // tune: Tight ++ shrinks attack slots to 4
 const caps0=await p.evaluate(()=>window.__gfd.slotCaps());
 await click('[data-su="hand"][data-v="-2"]');const caps1=await p.evaluate(()=>window.__gfd.slotCaps());
 if(caps1.attack!==4+(s.tune.focus==='bumper'?1:0))fail('tight++ attack cap '+JSON.stringify(caps1));
 // fill attack slots and check + is blocked
 await click('[data-tab="pass"]');
 for(let k=0;k<8;k++){const btn=await p.$('[data-in]:not([disabled])');if(!btn)break;await btn.click();await p.waitForTimeout(40);}
 const used=await p.evaluate(()=>{const g=window.__gfd;return g.G.deck.filter(id=>['pass','risk'].includes(g.CARDS[id].fam)).length;});
 if(used>caps1.attack)fail('attack over cap '+used);
 await click('[data-tab="all"]');
 if(!(await ok())){await click('#d-fix');}
 if(!(await ok()))fail('fix rules did not make it legal');
 await click('#d-done');const secs=(Date.now()-t0)/1000;
 if(await p.evaluate(()=>document.getElementById('garage').hidden))fail('done did not go back to the garage');
 // garage auto-fix: break the next track's minimum, then come back for the next race
 await p.evaluate(()=>{const g=window.__gfd,G=g.G;const nt=g.TRACKS[g.arcTi(G.raceIdx+1)],m=g.TRACK_MIN[nt.id];const f=Object.keys(m)[0];G.deck=G.deck.filter(id=>g.CARDS[id].fam!==f);while(G.deck.length<10)G.deck.push(G.coll.find(id=>g.CARDS[id].fam==='crew'));G.raceIdx++;g.showGarage();});
 if(!(await ok()))fail('garage did not fix the deck for the new track');
 if(!(await p.evaluate(()=>!!window.__gfd.G.deckNote)))fail('no note after garage fix');
 await shot('garage-fixed');
 // an illegal deck blocks Go racing
 await p.evaluate(()=>{const g=window.__gfd;g.G.deck=g.G.deck.slice(0,4);g.showGarage();});
 if(!(await p.$eval('#g-go',e=>e.disabled)))fail('go racing allowed with 4 cards');
 await click('#g-fix');if(!(await ok())||await p.$eval('#g-go',e=>e.disabled))fail('garage fix');
 // race, pick a card: it lands in the collection
 const n0=(await G()).coll.length;
 await click('#g-go');
 await p.evaluate(()=>{const g=window.__gfd,r=g.race;window.__gfdHeadless=true;window.__gfdPitPolicy='human';let k=0;while(g.race===r&&k++<30000){if(r.paused){r.paused=false;r._beatPaused=false;}g.update(0.05);}window.__gfdHeadless=false;g.saveSeason();});
 const pick=await p.$('#reward .minicard.pickable');if(pick){await pick.click();if((await G()).coll.length!==n0+1)fail('pick not in collection');}
 // reload keeps the collection
 const before=await G();await load();await click('#t-cont');const after=await G();
 if(JSON.stringify(before.coll)!==JSON.stringify(after.coll)||JSON.stringify(before.deck)!==JSON.stringify(after.deck))fail('collection/deck lost on reload');
 // an old (v36) save without a collection still loads
 await p.evaluate(()=>{const s=JSON.parse(localStorage.getItem('gfd-season-v1'));delete s.G.coll;delete s.G.trackDecks;localStorage.setItem('gfd-season-v1',JSON.stringify(s));});
 await load();await click('#t-cont');const old=await G();if(!old.coll||old.coll.length!==old.deck.length)fail('old save migration '+JSON.stringify(old));
 console.log(process.exitCode?'deck smoke FAILED':'deck smoke ok',`(builder pass took ${secs.toFixed(1)} s of clicks)`);
 console.log('pageerrors',errs.length,errs.slice(0,3));if(errs.length)process.exitCode=1;
 await b.close();
})();
