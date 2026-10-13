#!/bin/zsh
# usage: run.sh <job> <images...>   -> jobs/<job>/run.log, then copies the image into gen/<job>.png
C=/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex
G=${0:A:h}; job=$1; shift
mkdir -p "$G/jobs/$job"; cd "$G/jobs/$job"
args=(); for i in "$@"; do args+=(-i "$i"); done
$C exec -s workspace-write -c approval_policy="never" --skip-git-repo-check --add-dir ~/.codex/generated_images -C "$PWD" "${args[@]}" < "$G/jobs/$job.txt" > run.log 2>&1
rc=$?
sid=$(grep -m1 'session id:' run.log | awk '{print $3}')
img=$(ls -t ~/.codex/generated_images/$sid/*.png 2>/dev/null | head -1)
[ -n "$img" ] && cp "$img" "$G/$job.png"
echo "exit $rc sid $sid img $img" > status
