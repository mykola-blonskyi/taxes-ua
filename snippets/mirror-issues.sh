#!/usr/bin/env bash
# Usage: snippets/mirror-issues.sh <spec-number> <feature-slug>
# Rewrites .scratch/<feature-slug>/ from GitHub: spec.md from the spec issue, and one issues/NN-<slug>.md
# for every open or closed issue whose "## Parent" section names the spec, in issue-number order.
# GitHub wins: a rerun on unchanged GitHub state leaves no git diff. Format: docs/agents/issue-tracker.md.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <spec-number> <feature-slug>" >&2
  exit 2
fi
spec=$1
feature=$2
dir=.scratch/$feature
repo=$(gh repo view --json nameWithOwner --jq .nameWithOwner)

slugify() {
  local slug
  slug=$(tr '[:upper:]' '[:lower:]' <<<"$1" | tr -cs 'a-z0-9' '-' | cut -c1-50)
  slug=${slug#-}
  printf '%s' "${slug%-}"
}

blockers() {
  local list
  list=$(gh api "repos/$repo/issues/$1/dependencies/blocked_by" --jq '[.[].number] | sort | map("#\(.)") | join(", ")' 2>/dev/null || true)
  printf '%s' "${list:-none}"
}

status() {
  local state=$1 labels=$2 label
  if [ "$state" = CLOSED ]; then
    printf 'closed'
    return
  fi
  for label in needs-triage needs-info ready-for-agent ready-for-human wontfix; do
    case " $labels " in *" $label "*) printf '%s' "$label"; return ;; esac
  done
  printf 'needs-triage'
}

all=$(gh issue list --state all --limit 1000 --json number,title,body,state,labels)

mkdir -p "$dir/issues"
rm -f "$dir"/issues/*.md

{
  printf 'GitHub: #%s\n\n' "$spec"
  printf '%s\n' "$(jq -r --argjson n "$spec" '.[] | select(.number == $n) | .body | gsub("\r"; "")' <<<"$all")"
} >"$dir/spec.md"

children=$(jq -r --arg spec "$spec" '
  .[]
  | select((.body // "" | gsub("\r"; "")) | test("(^|\n)## Parent[ \t]*\n+[ \t]*#" + $spec + "([^0-9]|$)"))
  | .number' <<<"$all" | sort -n)

index=0
for number in $children; do
  index=$((index + 1))
  title=$(jq -r --argjson n "$number" '.[] | select(.number == $n) | .title' <<<"$all")
  state=$(jq -r --argjson n "$number" '.[] | select(.number == $n) | .state' <<<"$all")
  labels=$(jq -r --argjson n "$number" '.[] | select(.number == $n) | [.labels[].name] | join(" ")' <<<"$all")
  file=$(printf '%s/issues/%02d-%s.md' "$dir" "$index" "$(slugify "$title")")
  {
    printf 'GitHub: #%s\nStatus: %s\nBlocked by: %s\n\n# %s\n\n' "$number" "$(status "$state" "$labels")" "$(blockers "$number")" "$title"
    printf '%s\n' "$(jq -r --argjson n "$number" '.[] | select(.number == $n) | .body // "" | gsub("\r"; "")' <<<"$all")"
  } >"$file"
done

echo "Mirrored spec #$spec and $index ticket(s) into $dir"
