#!/usr/bin/env bash
# Prints the available frontier: open ready-for-agent issues whose every "Blocked by" issue is closed.
# An issue with no blockers is the whole point, so blocker extraction must never fail the run.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

closed=" $(gh issue list --state closed --limit 200 --json number --jq '.[].number' | tr '\n' ' ') "

gh issue list --state open --limit 200 --json number,title,labels \
  --jq '.[] | select([.labels[].name] | index("ready-for-agent")) | select(.title | startswith("Spec:") | not) | "\(.number)\t\(.title)"' \
| sort -n | while IFS=$'\t' read -r num title; do
  [ -z "${num:-}" ] && continue
  blockers=$(gh issue view "$num" --json body --jq .body \
    | awk '/^## Blocked by/{f=1;next} /^## /{f=0} f' \
    | grep -oE '#[0-9]+' | tr -d '#' | sort -un || true)
  pending=""
  for b in $blockers; do
    case "$closed" in *" $b "*) ;; *) pending="$pending #$b" ;; esac
  done
  if [ -z "$pending" ]; then
    printf 'AVAILABLE  #%-3s %s\n' "$num" "$title"
  else
    printf 'blocked    #%-3s %s  (waiting on%s)\n' "$num" "$title" "$pending"
  fi
done
