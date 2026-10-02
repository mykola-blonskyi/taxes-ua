#!/usr/bin/env bash
# Fails when web/src/data/api/schema.d.ts is not what `pnpm gen:api` produces from the API as it stands.
# Starts the API in Development (the only environment that serves the OpenAPI document) against the
# Postgres the caller provides at localhost:5432 (user, password and database taxes_ua, the Development
# connection string), generates the types into a scratch file and compares.
set -euo pipefail
cd "$(dirname "$0")/../.."

port=5241
log=$(mktemp)
fresh=$(mktemp)
api_pid=
cleanup() {
  [ -n "$api_pid" ] && kill "$api_pid" 2>/dev/null || true
}
trap cleanup EXIT

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$port" \
  dotnet run --project api/src/TaxesUa.Api -c Release --no-launch-profile >"$log" 2>&1 &
api_pid=$!

for _ in $(seq 1 120); do
  if curl -fsS "http://localhost:$port/api/health" >/dev/null 2>&1; then break; fi
  if ! kill -0 "$api_pid" 2>/dev/null; then
    echo "::error::The API exited before it answered"
    cat "$log"
    exit 1
  fi
  sleep 2
done
curl -fsS "http://localhost:$port/api/health" >/dev/null || { echo "::error::The API did not answer in 4 minutes"; cat "$log"; exit 1; }

(cd web && pnpm exec openapi-typescript "http://localhost:$port/api/openapi/v1.json" -o "$fresh")

if ! diff -u web/src/data/api/schema.d.ts "$fresh"; then
  echo "::error file=web/src/data/api/schema.d.ts::schema.d.ts is out of date with the API. Run the api on port 5241 and 'pnpm --dir web gen:api', then commit the result."
  exit 1
fi
echo "schema.d.ts matches the API's OpenAPI document"
