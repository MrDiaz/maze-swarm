#!/bin/zsh
cd "${0:A:h}"
./run.sh a & ./run.sh b & wait
./run.sh c
