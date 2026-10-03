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

# Two age key pairs for this run only: the owner's recovery key and the restore check's identity.
"${compose[@]}" build --quiet backup >/dev/null
make_key() { "${compose[@]}" run --rm --no-deps -T --entrypoint age-keygen backup 2>/dev/null; }
owner_key=$(make_key)
check_key=$(make_key)
export SMOKE_BACKUP_AGE_RECIPIENT=$(sed -n 's/^# public key: //p' <<<"$owner_key")
export SMOKE_BACKUP_CHECK_AGE_IDENTITY=$(grep '^AGE-SECRET-KEY-' <<<"$check_key")
owner_identity=$(grep '^AGE-SECRET-KEY-' <<<"$owner_key")

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
check "$(header x-powered-by -H "Host: $domain" http://web:3000/login)" "" "/login does not advertise the framework"

# The first start migrated an empty database, so it had to dump it first with the real pg_dump.
dumps=$("${compose[@]}" exec -T api sh -c 'ls /var/lib/taxes-ua/dumps/taxes_ua-pre-migrate-*.dump | wc -l')
check "$(tr -d '[:space:]' <<<"$dumps")" 1 "api dumped the database before its first migration"

# The backup sidecar: one dump and one restore check by hand (the schedule would wait for 01:00 UTC),
# against the database the api just migrated and the S3 stand-in, with two targets.
backup_run() { "${compose[@]}" exec -T backup backup-db "$1" 2>&1; }
backup_out=$(backup_run backup) && rc=0 || rc=$?
check "$rc" 0 "backup-db backup succeeds"
check_out=$(backup_run check) && rc=0 || rc=$?
check "$rc" 0 "backup-db check restores the dump it just stored"

runs=$("${compose[@]}" exec -T db psql -U taxes_ua -d taxes_ua -At \
  -c 'select "Job" || '"':'"' || "Succeeded" from "DatabaseBackupRuns" order by "Id"' | tr -d '\r')
check "$runs" "Backup:true"$'\n'"RestoreCheck:true" "the database records a successful Backup, then a successful RestoreCheck"

# rclone remotes for the stand-in, set only for these commands.
s3() {
  "${compose[@]}" exec -T -e RCLONE_CONFIG_SMOKE_TYPE=s3 -e RCLONE_CONFIG_SMOKE_PROVIDER=Other \
    -e RCLONE_CONFIG_SMOKE_ENDPOINT=http://s3:9000 -e RCLONE_CONFIG_SMOKE_ACCESS_KEY_ID=smoke-access \
    -e RCLONE_CONFIG_SMOKE_SECRET_ACCESS_KEY=smoke-s3-secret-value -e RCLONE_S3_NO_CHECK_BUCKET=true \
    -e RCLONE_LOG_LEVEL=ERROR "$@"
}
for bucket in taxes-ua-backups taxes-ua-offsite; do
  objects=$(s3 backup rclone lsf "SMOKE:$bucket/daily/" | tr -d '\r' | grep -c '^taxes_ua-.*\.dump\.age$' || true)
  check "$objects" 1 "$bucket holds one dump under daily/"
done

# The owner's recovery key, not the check identity, must open what the primary bucket holds.
read_dump='name=$(rclone lsf SMOKE:taxes-ua-backups/daily/ | head -n 1)
rclone cat "SMOKE:taxes-ua-backups/daily/$name" | age -d -i <(printf "%s\n" "$OWNER_IDENTITY") | pg_restore --list'
if s3 -e OWNER_IDENTITY="$owner_identity" backup bash -c "$read_dump" | grep -q 'TABLE DATA'; then r=yes; else r=no; fi
check "$r" yes "the stored dump decrypts with the owner's recovery key and pg_restore lists it"

logs="$("${compose[@]}" logs backup 2>&1)$backup_out$check_out"
r=no
for secret in "$SMOKE_BACKUP_CHECK_AGE_IDENTITY" "$owner_identity" smoke-s3-secret-value "Password=smoke"; do
  case "$logs" in *"$secret"*) r=leaked ;; esac
done
check "$r" no "the backup container's log and output hold no secret"

exit "$failed"
