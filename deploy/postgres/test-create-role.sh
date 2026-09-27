#!/usr/bin/env bash
# Proves create-role.sql and pg_hba.conf.snippet against a throwaway postgres:16 container.
set -euo pipefail
cd "$(dirname "$0")"

container="taxes-ua-role-test-$$"
trap 'docker rm -f "$container" >/dev/null 2>&1 || true' EXIT

docker run -d --name "$container" -e POSTGRES_PASSWORD=admin postgres:16-alpine >/dev/null
until docker exec "$container" pg_isready -U postgres -h 127.0.0.1 >/dev/null 2>&1; do sleep 1; done

admin() { docker exec -i "$container" psql -v ON_ERROR_STOP=1 -qtA -U postgres "$@"; }
connects() { docker exec "$container" psql -qtA -c 'select 1' "host=127.0.0.1 user=$1 password=$2 dbname=$3" >/dev/null 2>&1; }
expect() {
  if [ "$1" = "$2" ]; then echo "ok    $3"; else echo "FAIL  $3 (expected '$2', got '$1')"; exit 1; fi
}

admin -c "CREATE DATABASE neighbour_db" -c "CREATE ROLE neighbour LOGIN PASSWORD 'neighbour'"

admin < create-role.sql >/dev/null
others=$(admin < create-role.sql | paste -sd, -)
expect "$others" "neighbour_db,postgres,template1" "a second run succeeds and reports the databases PUBLIC still opens"

admin -c "ALTER ROLE taxes_ua PASSWORD 'taxes_ua'"

hba=/var/lib/postgresql/data/pg_hba.conf
docker exec -i "$container" sh -c "cat - $hba > /tmp/hba && cat /tmp/hba > $hba" < pg_hba.conf.snippet
admin -c "SELECT pg_reload_conf()" >/dev/null && sleep 1

connects taxes_ua taxes_ua taxes_ua && r=yes || r=no; expect $r yes "taxes_ua connects to taxes_ua"
connects taxes_ua taxes_ua neighbour_db && r=yes || r=no; expect $r no "taxes_ua is refused on neighbour_db"
connects taxes_ua taxes_ua postgres && r=yes || r=no; expect $r no "taxes_ua is refused on postgres"
connects neighbour neighbour neighbour_db && r=yes || r=no; expect $r yes "neighbour still connects to its own database"
connects neighbour neighbour taxes_ua && r=yes || r=no; expect $r no "neighbour is refused on taxes_ua"

docker exec "$container" psql -qtA -v ON_ERROR_STOP=1 "host=127.0.0.1 user=taxes_ua password=taxes_ua dbname=taxes_ua" \
  -c 'CREATE TABLE probe (id int)' -c 'CREATE FUNCTION probe_fn() RETURNS int LANGUAGE sql AS $$ SELECT 1 $$' \
  -c 'DROP FUNCTION probe_fn()' -c 'DROP TABLE probe' >/dev/null 2>&1 && r=yes || r=no
expect $r yes "taxes_ua can create and drop tables and functions in public, as migrations do"
docker exec "$container" psql -qtA "host=127.0.0.1 user=taxes_ua password=taxes_ua dbname=taxes_ua" \
  -c 'CREATE SCHEMA probe' >/dev/null 2>&1 && r=yes || r=no
expect $r no "taxes_ua cannot create a schema"
docker exec "$container" psql -qtA "host=127.0.0.1 user=taxes_ua password=taxes_ua dbname=taxes_ua" \
  -c 'DROP DATABASE taxes_ua' >/dev/null 2>&1 && r=yes || r=no
expect $r no "taxes_ua cannot drop its database"
