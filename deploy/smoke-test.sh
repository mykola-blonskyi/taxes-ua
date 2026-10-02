#!/usr/bin/env bash
# Brings up the production compose as Coolify would run it and requests it the way Traefik does:
# plain http to web:3000 with the public Host and X-Forwarded-Proto: https.
set -euo pipefail
cd "$(dirname "$0")/.."

domain=taxes.test
project="taxes-ua-smoke-$$"
export SOURCE_COMMIT=0123456789abcdef
compose=(docker compose -p "$project" -f docker-compose.yml -f deploy/smoke.compose.yml)
trap '"${compose[@]}" down -v >/dev/null 2>&1 || true' EXIT

failed=0
check() {
  if [ "$1" = "$2" ]; then echo "ok    $3"; else echo "FAIL  $3 (expected '$2', got '$1')"; failed=1; fi
}

if ! "${compose[@]}" up -d --build --wait --wait-timeout 180; then
  echo "FAIL  the stack did not become healthy"
  "${compose[@]}" ps -a
  "${compose[@]}" logs --tail 30 api
  exit 1
fi

check "$(docker inspect -f '{{.State.Health.Status}}' "$("${compose[@]}" ps -q api)")" healthy "api is healthy"
check "$(docker inspect -f '{{.State.Health.Status}}' "$("${compose[@]}" ps -q web)")" healthy "web is healthy"

request() {
  docker run --rm --network "${project}_default" curlimages/curl:8.10.1 -s -o /dev/null -D - \
    -H "X-Forwarded-Proto: https" "$@"
}
status() { request "$@" | head -1 | cut -d' ' -f2; }
header() { local name=$1; shift; request "$@" | tr -d '\r' | grep -i "^$name:" | head -1 | cut -d' ' -f2-; }

health=$(docker run --rm --network "${project}_default" curlimages/curl:8.10.1 -s \
  -H "Host: $domain" -H "X-Forwarded-Proto: https" http://web:3000/api/health)
check "$health" '{"status":"ok","database":true,"release":"'"$SOURCE_COMMIT"'"}' \
  "/api/health through web answers ok with the database and the release Coolify built"
check "$(status -H "Host: evil.example" http://web:3000/api/health)" 400 "a foreign Host is refused"
check "$(status -H "Host: $domain" http://web:3000/api/openapi/v1.json)" 404 "/api/openapi/v1.json is not served"
check "$(status -H "Host: $domain" "http://web:3000/api/auth/login/development?email=owner@example.com")" 404 \
  "the Development sign-in does not exist"
check "$(status -H "Host: $domain" http://web:3000/api/auth/login/google)" 302 "/api/auth/login/google redirects"
location=$(header location -H "Host: $domain" http://web:3000/api/auth/login/google)
case "$location" in
  *"redirect_uri=https%3A%2F%2F$domain%2Fapi%2Fauth%2Fcallback%2Fgoogle"*) r=yes ;;
  *) r=no ;;
esac
check "$r" yes "the Google redirect_uri is https://$domain/api/auth/callback/google"

for path in /login /api/health; do
  check "$(header strict-transport-security -H "Host: $domain" "http://web:3000$path")" \
    "max-age=31536000; includeSubDomains" "$path sends HSTS"
  check "$(header x-content-type-options -H "Host: $domain" "http://web:3000$path")" nosniff "$path sends nosniff"
  check "$(header x-frame-options -H "Host: $domain" "http://web:3000$path")" DENY "$path forbids framing"
  check "$(header referrer-policy -H "Host: $domain" "http://web:3000$path")" \
    strict-origin-when-cross-origin "$path sends a referrer policy"
done
csp=$(header content-security-policy -H "Host: $domain" http://web:3000/login)
case "$csp" in *"frame-ancestors 'none'"*) r=yes ;; *) r=no ;; esac
check "$r" yes "/login sends a CSP with frame-ancestors 'none'"
script_src=$(tr ';' '\n' <<<"$csp" | grep -E '^ ?script-src')
case "$script_src" in *"'unsafe-inline'"*) r=no ;; *"'nonce-"*) r=yes ;; *) r=no ;; esac
check "$r" yes "/login's script-src carries a nonce and no 'unsafe-inline'"
check "$(header permissions-policy -H "Host: $domain" http://web:3000/login | grep -c 'camera=()')" 1 "/login sends a Permissions-Policy"
check "$(header x-powered-by -H "Host: $domain" http://web:3000/login)" "" "/login does not advertise the framework"

# The first start migrated an empty database, so it had to dump it first with the real pg_dump.
dumps=$("${compose[@]}" exec -T api sh -c 'ls /var/lib/taxes-ua/dumps/taxes_ua-pre-migrate-*.dump | wc -l')
check "$(tr -d '[:space:]' <<<"$dumps")" 1 "api dumped the database before its first migration"

exit "$failed"
