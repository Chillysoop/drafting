# pp.py TAG [policy] : finish vs expected by persona x track for one policy
import json,sys,glob,statistics as st,collections
tag=sys.argv[1];pol=sys.argv[2] if len(sys.argv)>2 else 'human'
R=collections.defaultdict(list)
for f in glob.glob(f'r_{tag}_*.json'):
  for k,v in json.load(open(f)).items():
    a,t,p=k.split('|')
    if p==pol: R[(a,t)]+=v
A=sorted({a for a,t in R});T=sorted({t for a,t in R})
print(f"{tag} {pol}: mean finish (vs exp)   tracks "+' '.join(T))
for a in A:
  row=[];allv=[]
  for t in T:
    v=R.get((a,t),[]);allv+=v
    row.append(f"{st.mean(x['f'] for x in v):5.1f}({st.mean(x['e']-x['f'] for x in v):+4.1f})" if v else '   -  ')
  print(f"{a:10} "+' '.join(row)+f"  | all {st.mean(x['f'] for x in allv):5.2f} ({st.mean(x['e']-x['f'] for x in allv):+.2f}) n={len(allv)} dnf {100*st.mean(x['dnf'] for x in allv):.0f}%")
