#!/bin/zsh
# usage: run.sh <name> <prompt_file> <images...>
C=/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex
cd "${0:A:h}"
name=$1; pf=$2; shift 2
args=(); for i in "$@"; do args+=(-i "$i"); done
$C exec -s workspace-write -c approval_policy="never" --skip-git-repo-check --add-dir ~/.codex/generated_images --add-dir "${PWD:h}" -C "$PWD" "${args[@]}" < "$pf" > "$name.log" 2>&1
echo "exit $?"
