#!/bin/zsh
# Runs the crate / door image jobs one at a time through Codex image gen (each in its own folder: gen/<name>/).
#   run_jobs.sh <jobs.tsv>   lines: <out dir>\t<name>\t<ref1>,<ref2>\t<prompt>
C=/Applications/ChatGPT.app/Contents/Resources/codex-cli/bin/codex
while IFS=$'\t' read -r outdir name refs prompt; do
  [ -z "$name" ] && continue
  job="$outdir/gen/$name"; mkdir -p "$job"
  print -r -- "$prompt" > "$job/prompt.txt"
  args=(); for r in ${(s:,:)refs}; do args+=(-i "$r"); done
  ( cd "$job" && perl -e 'alarm 900; exec @ARGV' nice -n 10 "$C" exec --skip-git-repo-check -s workspace-write --add-dir "$outdir" --add-dir "$HOME/.codex/generated_images" "${args[@]}" - < prompt.txt > run.log 2>&1; echo $? > status )
  echo "$name $(cat "$job/status") $(ls -la "$outdir/$name.png" 2>/dev/null | awk '{print $5}')"
done < "$1"
