import json,sys,glob,statistics as st,collections
tags=sys.argv[1:]
print(f"{'tag':6} {'policy':7} {'n':>4} {'finish':>7} {'vsExp':>6} {'DNF':>5} {'tosses':>6} {'liveFx':>6} {'refills':>7} {'focus+':>6}")
for tag in tags:
  rows=collections.defaultdict(list)
  for f in glob.glob(f'r_{tag}_*.json'):
    for k,v in json.load(open(f)).items():
      a,t,p=k.split('|');rows[p]+=v
  for p in ['none','human','bold','hspam','spam']:
    v=rows.get(p)
    if not v:continue
    g=lambda k:st.mean(x.get(k,0) for x in v)
    print(f"{tag:6} {p:7} {len(v):4} {g('f'):7.2f} {st.mean(x['e']-x['f'] for x in v):+6.2f} {100*g('dnf'):4.0f}% {g('tfx'):6.1f} {g('tlive'):6.1f} {g('rref'):7.1f} {g('fgain'):6.2f}")
