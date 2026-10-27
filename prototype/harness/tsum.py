import json,glob,statistics as st,collections,sys
V=sys.argv[1].split(',');pre=sys.argv[2] if len(sys.argv)>2 else 't'
TR=['Mill','Carver','Talon','Pine','Ridge']
R={}
for v in V:
  R[v]=collections.defaultdict(list)
  for f in glob.glob(f'r_{pre}{v}_*.json'):
    for k,x in json.load(open(f)).items():
      a,t,p=k.split('|');R[v][t]+=x;R[v][(a,t)]+=x;R[v]['all']+=x;R[v][a]+=x
m=lambda L:st.mean(x['f'] for x in L)
print('variant   '+' '.join(f'{t:>7}' for t in TR)+'     all')
for v in V: print(f'{v:8}  '+' '.join(f'{m(R[v][str(i)]):7.2f}' for i in range(5))+f'  {m(R[v]["all"]):6.2f}  n={len(R[v]["all"])}')
print('by persona x track best variant:')
for a in ['drafter','bully','whisperer','restart','gambler']:
  print(f'{a:10}',' '.join(f"{min(V,key=lambda v:m(R[v][(a,str(i))])):>5}" for i in range(5)),' | overall',' '.join(f'{v}:{m(R[v][a]):.1f}' for v in V))
