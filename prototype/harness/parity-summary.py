# parity-summary.py out.json raw1.json [raw2.json ...]
# Step 33: crew calls are on in the baseline; each cell also keeps calls per race and the share that worked.
# Step 28: boil sim-harness-v10.js output down to per-cell averages for the C# parity check (sim/parity/browser-baseline.json).
# Each cell "persona|track|style" keeps n and the mean and standard deviation of the numbers the check compares.
import json,sys,statistics as st
out,raws=sys.argv[1],sys.argv[2:]
cells={}
for f in raws:
  for k,v in json.load(open(f)).items(): cells.setdefault(k,[]).extend(v)
def ms(xs): return [round(st.mean(xs),4),round(st.pstdev(xs),4)]
res={}
for k in sorted(cells):
  v=cells[k];stops=sum(x['stops'] for x in v);pl=sum(x['pl'] for x in v)
  res[k]={'n':len(v),'finish':ms([x['f'] for x in v]),'start':ms([x['s'] for x in v]),'expected':ms([x['e'] for x in v]),
          'gain':ms([x['e']-x['f'] for x in v]),'dnf':ms([x['dnf'] for x in v]),'plays':ms([x['pl'] for x in v]),
          'landed':round(sum(x['su'] for x in v)/pl,4) if pl else 0,'stops':ms([x['stops'] for x in v]),
          'perfect':round(sum(x['perfect'] for x in v)/stops,4) if stops else 0,'cautions':ms([x['cautions'] for x in v]),
          'calls':ms([x.get('calls',0) for x in v]),'callsWon':round(sum(x.get('cwon',0) for x in v)/max(1,sum(x.get('calls',0) for x in v)),4)}
json.dump({'source':'prototype/harness/sim-harness-v10.js, engine preset, Normal, crew calls on (step 33)','cells':res},open(out,'w'),indent=0)
print(f'{out}: {len(res)} cells, {sum(c["n"] for c in res.values())} races')
