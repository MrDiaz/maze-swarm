#!/bin/zsh
# usage: run.sh <letter>  -> runs codex in gen/<letter> with prompt <letter>.txt
C=/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex
cd "${0:A:h}"
E="${PWD:h:h}"
l=$1
$C exec -s workspace-write -c approval_policy="never" --skip-git-repo-check --add-dir ~/.codex/generated_images -C "$PWD/$l" -i "$E/escape_a_overview.png" -i "$E/escape_c_pillar_room.png" < $l.txt > $l.log 2>&1
echo "exit $?" > $l.status
