# rpl.sh html tag N policies [extra json]: one process per stock persona over the three late model tracks (sim-harness-v9, sched "late")
H=$1;T=$2;N=$3;P=$4;X=${5:-}
for a in drafter bully whisperer restart gambler;do node "$(dirname "$0")/sim-harness-v9.js" $N $H "{\"_p\":\"engine\",\"sched\":\"late\",\"archs\":[\"$a\"],\"trks\":[7,8,9]$X}" r_${T}_$a.json $P & done; wait
