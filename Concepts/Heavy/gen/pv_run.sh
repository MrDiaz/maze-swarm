#!/bin/zsh
# usage: pv_run.sh <outdir> <prompt_file> <images...>
C=/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex
D=${0:A:h}; cd "$D/$1"
pf=$D/$2; shift 2
args=(); for i in "$@"; do args+=(-i "$i"); done
$C exec -s workspace-write -c approval_policy="never" --skip-git-repo-check --add-dir ~/.codex/generated_images -C "$PWD" "${args[@]}" < "$pf" > run.log 2>&1
echo "exit $?" > status
