#!/bin/zsh
cd "${0:A:h}"
while [ ! -f jobs/s9_misc/status ] && [ ! -f jobs/s6_crates/status ]; do sleep 15; done
./run.sh s5b_evos "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png" "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/Heavy/minigun.png" "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/Hero/rifle.png"
echo done > regen.done
