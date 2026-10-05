#!/usr/bin/env bash
# Usage: check-no-ai-attribution.sh <base> <head>
# Fails when a commit in base..head, or the PR title or body (env PR_TITLE, PR_BODY), carries AI
# attribution. Only the range is read: older history holds trailers that must not be rewritten.
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "usage: $0 <base> <head>" >&2
  exit 2
fi
base=$1
head=$2

coauthor='^[[:space:]]*co-authored-by:.*(claude|anthropic|chatgpt|openai|copilot|gemini|codex|cursor|[^[:alnum:]]ai([^[:alnum:]]|$))'
generated='generated (with|by) (\[claude code\]|claude)'
noreply='noreply@anthropic\.com'
pattern="$coauthor|$generated|$noreply"

failed=0

scan() {
  local label=$1 text=$2 hits
  hits=$(grep -inE "$pattern" <<<"$text" || true)
  if [ -n "$hits" ]; then
    echo "::error::AI attribution in $label"
    sed 's/^/    /' <<<"$hits"
    failed=1
  fi
}

while read -r sha; do
  scan "commit $(git rev-parse --short "$sha")" "$(git log -1 --format=%B "$sha")"
done < <(git rev-list "$base..$head")

scan "the PR title" "${PR_TITLE:-}"
scan "the PR body" "${PR_BODY:-}"

if [ "$failed" -ne 0 ]; then
  echo "Remove the attribution (amend or rewrite the commits, edit the PR text); the owner's rule is no AI attribution anywhere." >&2
  exit 1
fi
echo "No AI attribution in $(git rev-list --count "$base..$head") commit(s), the PR title or the PR body."
