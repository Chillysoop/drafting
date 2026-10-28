# rpk.sh html tag N policies [extra json]: one process per kart persona over both kart tracks (sim-harness-v9)
H=$1;T=$2;N=$3;P=$4;X=${5:-}
for a in brawler linepainter packrat latebraker;do node "$(dirname "$0")/sim-harness-v9.js" $N $H "{\"_p\":\"engine\",\"sched\":\"kart\",\"archs\":[\"$a\"],\"trks\":[5,6]$X}" r_${T}_$a.json $P & done; wait
