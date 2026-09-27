#!/usr/bin/env bash
# Writes one compressed logical dump of the taxes-ua database and prunes old ones. Meant for cron,
# and only needed when the PostgreSQL instance has no backup of its own (ADR-006).
#
#   BACKUP_DIR=/var/backups/taxes-ua dump.sh docker exec <postgres-container> pg_dump -U postgres -Fc taxes_ua
#
# Everything after the script name is the pg_dump command, so it works the same against a
# containerised or a host instance. RETAIN_DAYS (default 14) bounds how long dumps are kept.
set -euo pipefail

: "${BACKUP_DIR:?BACKUP_DIR must name the directory dumps are written to}"
retain_days="${RETAIN_DAYS:-14}"
[ "$#" -gt 0 ] || { echo "usage: dump.sh <pg_dump command...>" >&2; exit 2; }

mkdir -p "$BACKUP_DIR"
target="$BACKUP_DIR/taxes_ua-$(date -u +%Y%m%dT%H%M%SZ).dump"
partial="$target.partial"
trap 'rm -f "$partial"' EXIT

(umask 077 && "$@" > "$partial")
[ -s "$partial" ] || { echo "pg_dump produced an empty file" >&2; exit 1; }
mv "$partial" "$target"

# Pruning runs only after a dump succeeded, so a broken dump never deletes the last good one.
find "$BACKUP_DIR" -maxdepth 1 -name 'taxes_ua-*.dump' -mtime +"$retain_days" -delete
echo "$target"
