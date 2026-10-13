#!/bin/zsh
cd "${0:A:h}"
while [ ! -f jobs/s5b_evos/status ] && [ ! -f jobs/s6b_crates/status ]; do sleep 15; done
./run.sh s10_fixes "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png"
