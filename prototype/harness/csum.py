# csum.py dir tag : summary of career-sim.js runs (files tag-*.json in dir)
# per run: avg finish by career race (grouped by series + season race), win/top-5 share, money, and the step 23 checks.
import json,sys,glob,os,statistics as st
d,tag=sys.argv[1],sys.argv[2]
runs={}
for f in sorted(glob.glob(os.path.join(d,tag+'-*.json'))):
  for k,v in json.load(open(f)).items():runs[k]=v
def avg(x):return round(st.mean(x),2) if x else None
blocks=[(1,3),(4,6),(7,10),(11,14),(15,19),(20,24),(25,29),(30,34)]
print('finish by career race block (series mix in brackets)')
print('run'.ljust(14)+''.join(f'{a}-{b}'.rjust(12) for a,b in blocks))
for k,cs in runs.items():
  row=k.ljust(14)
  for a,b in blocks:
    xs=[r for c in cs for r in c if a<=r['cr']<=b]
    if not xs:row+=''.rjust(12);continue
    ser=''.join(sorted(set(r['series'][0] for r in xs)))
    row+=f"{avg([r['fin'] for r in xs])}[{ser}]".rjust(12)
  print(row)
print()
print('by series: avg finish, wins, top5, DNF, avg race pay, money at end of series, races, seasons')
for k,cs in runs.items():
  for s in ['kart','late','stock']:
    xs=[r for c in cs for r in c if r['series']==s]
    if not xs:continue
    n=len(xs);print(f"{k:14} {s:6} fin {avg([r['fin'] for r in xs]):6} exp {avg([r['exp'] for r in xs]):6} win {sum(r['fin']==1 for r in xs)/n:5.0%} top5 {sum(r['fin']<=5 for r in xs)/n:5.0%} dnf {sum(r['dnf'] for r in xs)/n:4.0%} pay {avg([r['tot'] for r in xs]):8} purse {avg([r['purse'] for r in xs]):7} bonus {avg([r['bonus'] for r in xs]):7} obj {avg([r['obj'] for r in xs]):6} ret {avg([r['ret'] for r in xs]):6} sal {avg([r['sal'] for r in xs]):6} n {n}")
print()
print('season race view (first season in each series): avg finish at season race 1..10')
for k,cs in runs.items():
  for s in ['kart','late','stock']:
    by={}
    for c in cs:
      first=None
      for r in c:
        if r['series']!=s:continue
        if first is None:first=r['season']
        if r['season']!=first:continue
        by.setdefault(r['ri'],[]).append(r['fin'])
    if by:print(f"{k:14} {s:6} "+' '.join(f"{avg(by[i]):5}" for i in sorted(by))+f"   (careers {max(len(v) for v in by.values())})")
print()
print('season ends: rank and what was signed')
for k,cs in runs.items():
  print(k.ljust(14),' | '.join(','.join(f"{r['series'][0]}P{r['seasonRank']}->{(r.get('signed') or {}).get('series','?')[0]}{(r.get('signed') or {}).get('r','')}" for r in c if 'seasonRank' in r) for c in cs))
print()
print('stats at career races 6/14/24: car, driver, crew')
for k,cs in runs.items():
  out=[]
  for i in (6,14,24):
    xs=[r for c in cs for r in c if r['cr']==i]
    if xs:out.append(f"r{i}: {avg([r['car'] for r in xs])}/{avg([r['drv'] for r in xs])}/{avg([r['crew'] for r in xs])} ${avg([r['money'] for r in xs])}")
  print(k.ljust(14),'  '.join(out))
