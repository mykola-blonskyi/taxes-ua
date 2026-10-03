#!/usr/bin/env bash
# Encrypted pg_dump to S3-compatible storage, and a restore check of the newest dump.
#   schedule  (default) every day at 01:00 UTC run `backup`; on Sundays then run `check`
#   backup    dump the app database, encrypt it with age, upload it, prune old copies
#   check     restore the newest stored dump into a throwaway cluster and compare it with the live database
# Every run records one row in "DatabaseBackupRuns" and exits 0 only if it succeeded.
# Secrets arrive in the environment and go to tools by environment or process substitution, never on a
# command line, and are never printed.
set -euo pipefail

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

WORK=""
SCRATCH=""
cleanup() {
  if [ -n "$SCRATCH" ]; then
    pg_ctl -D "$SCRATCH/data" -m immediate stop >/dev/null 2>&1 || true
    rm -rf "$SCRATCH"
  fi
  if [ -n "$WORK" ]; then rm -rf "$WORK"; fi
  return 0
}
trap cleanup EXIT

# ---- database --------------------------------------------------------------------------------------

DB_HOST="" DB_PORT="" DB_NAME="" DB_USER="" DB_PASS=""

# Npgsql form: Key=Value pairs split by ';'. Keys are case-insensitive, and spaces around keys and values are
# dropped. A value in single or double quotes may hold ';' and '=', and a doubled quote inside stands for
# one. Keys other than the five below, such as Maximum Pool Size, are ignored.
parse_database_url() {
  [ -n "${DATABASE_URL:-}" ] || return 1
  local s=$DATABASE_URL i=0 n c q key val
  n=${#s}
  DB_HOST="" DB_PORT="" DB_NAME="" DB_USER="" DB_PASS=""
  while [ "$i" -lt "$n" ]; do
    key=""
    while [ "$i" -lt "$n" ] && [ "${s:i:1}" != "=" ] && [ "${s:i:1}" != ";" ]; do
      key+=${s:i:1}
      i=$((i + 1))
    done
    if [ "$i" -ge "$n" ] || [ "${s:i:1}" = ";" ]; then
      i=$((i + 1))
      continue
    fi
    i=$((i + 1))
    while [ "$i" -lt "$n" ] && [[ "${s:i:1}" == [[:space:]] ]]; do i=$((i + 1)); done
    val=""
    c=${s:i:1}
    if [ "$c" = '"' ] || [ "$c" = "'" ]; then
      q=$c
      i=$((i + 1))
      while [ "$i" -lt "$n" ]; do
        c=${s:i:1}
        if [ "$c" = "$q" ]; then
          if [ "${s:i+1:1}" = "$q" ]; then
            val+=$q
            i=$((i + 2))
            continue
          fi
          i=$((i + 1))
          break
        fi
        val+=$c
        i=$((i + 1))
      done
      while [ "$i" -lt "$n" ] && [ "${s:i:1}" != ";" ]; do i=$((i + 1)); done
    else
      while [ "$i" -lt "$n" ] && [ "${s:i:1}" != ";" ]; do
        val+=${s:i:1}
        i=$((i + 1))
      done
      val=${val%"${val##*[![:space:]]}"}
    fi
    i=$((i + 1))
    key=${key,,}
    key=${key//[[:space:]]/}
    case "$key" in
      host | server) DB_HOST=$val ;;
      port) DB_PORT=$val ;;
      database | db) DB_NAME=$val ;;
      username | userid | user) DB_USER=$val ;;
      password | pwd) DB_PASS=$val ;;
    esac
  done
  : "${DB_PORT:=5432}"
  [ -n "$DB_HOST" ] && [ -n "$DB_NAME" ] && [ -n "$DB_USER" ]
}

# Runs a command against the live database. The settings live in a subshell so they never reach the
# scratch cluster the check also talks to.
live() {
  (
    export PGHOST="$DB_HOST" PGPORT="$DB_PORT" PGDATABASE="$DB_NAME" PGUSER="$DB_USER" PGPASSWORD="$DB_PASS" PGCONNECT_TIMEOUT=15
    exec "$@"
  )
}

# Runs a command against the throwaway cluster, over its unix socket.
scratch() {
  (
    export PGHOST="$SCRATCH" PGUSER=postgres
    unset PGPORT PGDATABASE PGPASSWORD
    exec "$@"
  )
}

RECORD_SQL='insert into "DatabaseBackupRuns" ("Job", "FinishedAt", "Succeeded", "Detail")
values (:'"'job'"', now(), :'"'ok'"'::boolean, :'"'detail'"');'

# finish JOB true|false DETAIL: prints the result, records it, and returns the exit status of the run.
finish() {
  local job=$1 ok=$2 detail=$3 rc=0 out
  detail=${detail//$'\n'/ }
  detail=${detail:0:1000}
  if [ "$ok" = true ]; then
    log "$job ok: $detail"
  else
    log "$job FAILED: $detail"
    rc=1
  fi
  if ! out=$(printf '%s\n' "$RECORD_SQL" | live psql -X -q -v ON_ERROR_STOP=1 -v "job=$job" -v "ok=$ok" -v "detail=$detail" 2>&1); then
    printf '%s\n' "$out" >"$WORK/record.err"
    log "could not record the $job run: $(last_error "$WORK/record.err")"
    rc=1
  fi
  return $rc
}

# ---- configuration ---------------------------------------------------------------------------------

# Name of a target's setting: primary BACKUP_S3_<S>, offsite BACKUP_OFFSITE_S3_<S>.
target_var() {
  case "$1" in
    primary) printf 'BACKUP_S3_%s' "$2" ;;
    offsite) printf 'BACKUP_OFFSITE_S3_%s' "$2" ;;
  esac
}
target_get() {
  local name
  name=$(target_var "$1" "$2")
  printf '%s' "${!name:-}"
}

TARGETS=()
MISSING=()

# Fills TARGETS and MISSING (names only) from the environment. The offsite target is skipped when none
# of its variables is set; set in part, it fails naming the rest.
load_targets() {
  local s any=""
  TARGETS=(primary)
  MISSING=()
  for s in ENDPOINT BUCKET ACCESS_KEY SECRET_KEY; do
    [ -n "$(target_get primary "$s")" ] || MISSING+=("$(target_var primary "$s")")
    [ -z "$(target_get offsite "$s")" ] || any=1
  done
  [ -z "$(target_get offsite REGION)" ] || any=1
  if [ -n "$any" ]; then
    TARGETS+=(offsite)
    for s in ENDPOINT BUCKET ACCESS_KEY SECRET_KEY; do
      [ -n "$(target_get offsite "$s")" ] || MISSING+=("$(target_var offsite "$s")")
    done
  fi
}

# rclone reads each remote from RCLONE_CONFIG_<REMOTE>_* variables, so no config file holds a key.
export_remotes() {
  local t up region
  export RCLONE_CONFIG="$WORK/rclone.conf" RCLONE_S3_NO_CHECK_BUCKET=true RCLONE_LOG_LEVEL=ERROR
  : >"$RCLONE_CONFIG"
  for t in "${TARGETS[@]}"; do
    up=${t^^}
    region=$(target_get "$t" REGION)
    export "RCLONE_CONFIG_${up}_TYPE=s3" "RCLONE_CONFIG_${up}_PROVIDER=Other" \
      "RCLONE_CONFIG_${up}_ENDPOINT=$(target_get "$t" ENDPOINT)" \
      "RCLONE_CONFIG_${up}_ACCESS_KEY_ID=$(target_get "$t" ACCESS_KEY)" \
      "RCLONE_CONFIG_${up}_SECRET_ACCESS_KEY=$(target_get "$t" SECRET_KEY)" \
      "RCLONE_CONFIG_${up}_REGION=${region:-us-east-1}"
  done
}

# One line saying what a tool's stderr complained about, shortened: the first line that mentions an
# error, else the last line that is not a psql pointer. Takes the first non-empty file.
last_error() {
  local f line
  for f in "$@"; do
    if [ -s "$f" ]; then
      line=$(grep -m 1 -iE 'error|fatal' "$f" || true)
      [ -n "$line" ] || line=$(grep -vE '^(LINE [0-9]+:|[[:space:]]*\^)' "$f" | tail -n 1 || true)
      printf '%s' "${line:0:200}"
      return 0
    fi
  done
  printf 'no error output'
}

join_by() {
  local sep=$1 out="" x
  shift
  for x in "$@"; do out+="${out:+$sep}$x"; done
  printf '%s' "$out"
}

start_work() {
  WORK=$(mktemp -d)
  ERR="$WORK/err"
  ERR2="$WORK/err2"
  ERR3="$WORK/err3"
  : >"$ERR"
  : >"$ERR2"
  : >"$ERR3"
}

# ---- backup ----------------------------------------------------------------------------------------

# Dumps and encrypts into $OUT. Not streamed to the bucket: a failed pg_dump would leave a truncated object.
dump_encrypt() {
  local rargs=(-r "$BACKUP_AGE_RECIPIENT") pub
  if [ -n "${BACKUP_CHECK_AGE_IDENTITY:-}" ]; then
    if ! pub=$(age-keygen -y <(printf '%s\n' "$BACKUP_CHECK_AGE_IDENTITY") 2>"$ERR"); then
      STEP_ERR="BACKUP_CHECK_AGE_IDENTITY is not a valid age identity"
      return 1
    fi
    rargs+=(-r "$pub")
  fi
  if ! live pg_dump --format=custom 2>"$ERR" | age "${rargs[@]}" >"$OUT" 2>"$ERR2"; then
    STEP_ERR="pg_dump or age: $(last_error "$ERR2" "$ERR")"
    return 1
  fi
  if [ ! -s "$OUT" ]; then
    STEP_ERR="the encrypted dump is empty"
    return 1
  fi
}

upload_target() {
  local t=$1 bucket
  bucket=$(target_get "$t" BUCKET)
  if ! rclone copyto "$OUT" "$t:$bucket/daily/$NAME" 2>"$ERR"; then
    STEP_ERR="upload: $(last_error "$ERR")"
    return 1
  fi
  if [ "$(date -u +%u)" = 7 ] && ! rclone copyto "$t:$bucket/daily/$NAME" "$t:$bucket/weekly/$NAME" 2>"$ERR"; then
    STEP_ERR="weekly copy: $(last_error "$ERR")"
    return 1
  fi
  # Retention runs only after this target's upload succeeded, so a broken backup never deletes the last good one.
  if ! rclone delete --use-server-modtime --min-age 14d "$t:$bucket/daily" 2>"$ERR"; then
    STEP_ERR="pruning daily: $(last_error "$ERR")"
    return 1
  fi
  if ! rclone delete --use-server-modtime --min-age 56d "$t:$bucket/weekly" 2>"$ERR"; then
    STEP_ERR="pruning weekly: $(last_error "$ERR")"
    return 1
  fi
}

cmd_backup() {
  if ! parse_database_url; then
    log "backup FAILED: DATABASE_URL is missing or has no host, database and username; nothing to record into"
    return 1
  fi
  start_work
  load_targets
  [ -n "${BACKUP_AGE_RECIPIENT:-}" ] || MISSING+=(BACKUP_AGE_RECIPIENT)
  if [ "${#MISSING[@]}" -gt 0 ]; then
    finish Backup false "missing configuration: $(join_by ', ' "${MISSING[@]}")"
    return
  fi
  export_remotes

  NAME="taxes_ua-$(date -u +%Y%m%dT%H%M%SZ).dump.age"
  OUT="$WORK/$NAME"
  STEP_ERR=""
  if ! dump_encrypt; then
    finish Backup false "$NAME: $STEP_ERR"
    return
  fi
  local size ok=() failed=() t
  size=$(wc -c <"$OUT" | tr -d ' ')
  for t in "${TARGETS[@]}"; do
    if upload_target "$t"; then ok+=("$t"); else failed+=("$t: $STEP_ERR"); fi
  done
  if [ "${#failed[@]}" -gt 0 ]; then
    finish Backup false "$NAME, $size bytes; failed: $(join_by '; ' "${failed[@]}")"
    return
  fi
  finish Backup true "$NAME, $size bytes, to $(join_by ', ' "${ok[@]}")"
}

# ---- restore check ---------------------------------------------------------------------------------

start_scratch() {
  SCRATCH=$(mktemp -d)
  if ! initdb -D "$SCRATCH/data" -U postgres -E UTF8 --auth=trust >/dev/null 2>"$ERR"; then
    STEP_ERR="initdb: $(last_error "$ERR")"
    return 1
  fi
  if ! pg_ctl -D "$SCRATCH/data" -w -l "$SCRATCH/pg.log" \
    -o "-c listen_addresses='' -c unix_socket_directories=$SCRATCH -c shared_buffers=16MB -c fsync=off -c max_connections=10" \
    start >/dev/null 2>"$ERR"; then
    STEP_ERR="pg_ctl start: $(last_error "$ERR" "$SCRATCH/pg.log")"
    return 1
  fi
}

# The newest stored dump must be under 48 hours old, decrypt with the check identity, restore, carry a
# migration the live database has too, and hold rows. Sets CHECK_DETAIL on success.
check_target() {
  local t=$1 bucket listing latest ts epoch hours last_migration known rows
  bucket=$(target_get "$t" BUCKET)
  if ! listing=$(rclone lsf --files-only "$t:$bucket/daily/" 2>"$ERR"); then
    STEP_ERR="cannot list daily/: $(last_error "$ERR")"
    return 1
  fi
  latest=$(printf '%s\n' "$listing" | grep -E '^taxes_ua-[0-9]{8}T[0-9]{6}Z\.dump\.age$' | sort | tail -n 1 || true)
  if [ -z "$latest" ]; then
    STEP_ERR="no backup under daily/"
    return 1
  fi
  ts=${latest#taxes_ua-}
  ts=${ts%%.*}
  epoch=$(date -u -d "${ts:0:4}-${ts:4:2}-${ts:6:2} ${ts:9:2}:${ts:11:2}:${ts:13:2}" +%s)
  hours=$((($(date -u +%s) - epoch) / 3600))
  if [ "$hours" -ge 48 ]; then
    STEP_ERR="latest backup is from ${ts:0:4}-${ts:4:2}-${ts:6:2} ${ts:9:2}:${ts:11:2} UTC ($hours hours old): $latest"
    return 1
  fi

  scratch dropdb --if-exists taxes_ua_restore 2>/dev/null || true
  if ! scratch createdb taxes_ua_restore 2>"$ERR"; then
    STEP_ERR="createdb: $(last_error "$ERR")"
    return 1
  fi
  # Streamed, so no decrypted dump is ever written to disk.
  : >"$ERR"
  : >"$ERR2"
  : >"$ERR3"
  if ! rclone cat "$t:$bucket/daily/$latest" 2>"$ERR" \
    | age -d -i <(printf '%s\n' "$BACKUP_CHECK_AGE_IDENTITY") 2>"$ERR2" \
    | scratch pg_restore --no-owner --no-privileges --single-transaction --exit-on-error -d taxes_ua_restore 2>"$ERR3"; then
    STEP_ERR="restoring $latest: $(last_error "$ERR2" "$ERR" "$ERR3")"
    return 1
  fi

  if ! last_migration=$(scratch psql -X -At -d taxes_ua_restore \
    -c 'select "MigrationId" from "__EFMigrationsHistory" order by 1 desc limit 1' 2>"$ERR"); then
    STEP_ERR="reading the restored migration history: $(last_error "$ERR")"
    return 1
  fi
  if [ -z "$last_migration" ]; then
    STEP_ERR="the restored database has no migration history"
    return 1
  fi
  if ! known=$(printf '%s\n' 'select count(*) from "__EFMigrationsHistory" where "MigrationId" = :'"'m'"';' |
    live psql -X -At -v ON_ERROR_STOP=1 -v "m=$last_migration" 2>"$ERR"); then
    STEP_ERR="reading the live migration history: $(last_error "$ERR")"
    return 1
  fi
  if [ "$known" != 1 ]; then
    STEP_ERR="restored migration $last_migration is not in the live database"
    return 1
  fi
  if ! rows=$(scratch psql -X -At -d taxes_ua_restore -c "select coalesce(sum((xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', schemaname, tablename), false, true, '')))[1]::text::bigint), 0) from pg_tables where schemaname = 'public' and tablename not in ('__EFMigrationsHistory', 'DatabaseBackupRuns')" 2>"$ERR"); then
    STEP_ERR="counting restored rows: $(last_error "$ERR")"
    return 1
  fi
  if [ -z "$rows" ] || [ "$rows" -le 0 ]; then
    STEP_ERR="the restored database has no rows"
    return 1
  fi
  scratch dropdb taxes_ua_restore 2>/dev/null || true
  CHECK_DETAIL="restored $latest, migration $last_migration, $rows rows"
}

cmd_check() {
  if ! parse_database_url; then
    log "check FAILED: DATABASE_URL is missing or has no host, database and username; nothing to record into"
    return 1
  fi
  start_work
  load_targets
  [ -n "${BACKUP_CHECK_AGE_IDENTITY:-}" ] || MISSING+=(BACKUP_CHECK_AGE_IDENTITY)
  if [ "${#MISSING[@]}" -gt 0 ]; then
    finish RestoreCheck false "missing configuration: $(join_by ', ' "${MISSING[@]}")"
    return
  fi
  export_remotes
  STEP_ERR=""
  if ! start_scratch; then
    finish RestoreCheck false "$STEP_ERR"
    return
  fi
  local t passed=true results=()
  CHECK_DETAIL=""
  for t in "${TARGETS[@]}"; do
    if check_target "$t"; then
      results+=("$t: $CHECK_DETAIL")
    else
      passed=false
      results+=("$t: FAILED $STEP_ERR")
    fi
  done
  finish RestoreCheck "$passed" "$(join_by '; ' "${results[@]}")"
}

# ---- schedule --------------------------------------------------------------------------------------

# Seconds from now ($1, epoch seconds) until the next 01:00 UTC.
seconds_until_next_run() {
  local now=$1 next
  next=$((now - now % 86400 + 3600))
  if [ "$next" -le "$now" ]; then next=$((next + 86400)); fi
  printf '%s' $((next - now))
}

cmd_schedule() {
  local self=${BASH_SOURCE[0]}
  # A run killed mid-way (a deploy, an OOM kill) leaves its temp dirs, and a check's scratch cluster holds
  # restored data. Nothing else runs at start, so every mktemp dir is stale.
  rm -rf /tmp/tmp.*
  log "scheduler started; backup daily at 01:00 UTC, restore check on Sundays"
  while true; do
    sleep "$(seconds_until_next_run "$(date -u +%s)")"
    "$self" backup || true
    if [ "$(date -u +%u)" = 7 ]; then "$self" check || true; fi
  done
}

main() {
  case "${1:-schedule}" in
    schedule) cmd_schedule ;;
    backup) cmd_backup ;;
    check) cmd_check ;;
    *)
      echo "usage: backup-db [schedule|backup|check]" >&2
      return 2
      ;;
  esac
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then main "$@"; fi
