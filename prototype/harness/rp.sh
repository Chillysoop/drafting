# rp.sh html tag N policies [extra json]
H=$1;T=$2;N=$3;P=$4;X=${5:-}
for a in drafter bully whisperer restart gambler;do node "$(dirname "$0")/sim-harness-v7.js" $N $H "{\"_p\":\"engine\",\"archs\":[\"$a\"],\"trks\":[0,1,2,3,4]$X}" r_${T}_$a.json $P & done; wait
