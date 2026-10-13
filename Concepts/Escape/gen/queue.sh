#!/bin/zsh
cd "${0:A:h}"
C="/Volumes/T7 Shield/Projects/Maze Swarm/Concepts"
R=("$C/02_floor_clear.png" "$C/04_floor_escape.png" "$C/StartScreen2/built/07_gameplay_hud.png" "$C/StartScreen2/built/mapselect_ingame.png" "$C/Enemies/drone.png" "$C/Enemies/bloater.png" "$C/Hero/hero_apose.png")
names=("$@")
for ((i=1; i<=${#names}; i+=2)); do
  ./run.sh ${names[i]} ${names[i]}_prompt.txt "${R[@]}" > ${names[i]}.status 2>&1 &
  [[ -n ${names[i+1]} ]] && ./run.sh ${names[i+1]} ${names[i+1]}_prompt.txt "${R[@]}" > ${names[i+1]}.status 2>&1 &
  wait
done
