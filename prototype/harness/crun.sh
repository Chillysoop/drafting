# crun.sh html tag N runs extraJSON : one career-sim process per run (policy:build), output tag-<run>.json in $OUT (default .)
H=$1;T=$2;N=$3;O=${OUT:-.}
for r in $(echo $4|tr ',' ' ');do node career-sim.js $N "$H" "$O/$T-${r/:/-}.json" $r "${5:-{\}}" & done;wait
