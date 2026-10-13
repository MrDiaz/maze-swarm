#!/bin/zsh
cd "${0:A:h}"
( while [ ! -f jobs/s1_cards_a/status ]; do sleep 10; done
  ./run.sh s2_cards_b "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png"; ./run.sh s3_cards_c "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png"; ./run.sh s4_cards_d "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png" "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/Heavy/minigun.png"; ./run.sh s6_crates "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png" ) &
( while [ ! -f jobs/s5_evos/status ]; do sleep 10; done
  ./run.sh s7_currency "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png"; ./run.sh s8_gear "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png" "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/Hero/hero_apose.png"; ./run.sh s9_misc "/Volumes/T7 Shield/Projects/Maze Swarm/Concepts/StartScreen2/built/02_loadout.png" ) &
wait; echo ALLDONE > lanes.done
