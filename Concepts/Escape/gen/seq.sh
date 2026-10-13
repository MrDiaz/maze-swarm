#!/bin/zsh
cd "${0:A:h}"
C="/Volumes/T7 Shield/Projects/Maze Swarm/Concepts"
R=("$C/02_floor_clear.png" "$C/04_floor_escape.png" "$C/StartScreen2/built/07_gameplay_hud.png" "$C/StartScreen2/built/mapselect_ingame.png" "$C/Enemies/drone.png" "$C/Enemies/bloater.png" "$C/Hero/hero_apose.png")
for n in "$@"; do ./run.sh $n ${n}_prompt.txt "${R[@]}" > $n.status 2>&1; echo "$n $(cat $n.status)"; done
