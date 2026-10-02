#!/usr/bin/env bash
# Asserts what the production compose file promises docs/deploy.md: Traefik reaches only `web`,
# every service restarts, is memory-limited and rotates its logs, and `api` keeps its key ring across redeploys and exits when a startup guard throws.
set -euo pipefail
cd "$(dirname "$0")/.."

config=$(docker compose -f docker-compose.yml config --format json)
check() {
  if [ "$(jq -r "$1" <<<"$config")" = "$2" ]; then echo "ok    $3"; else echo "FAIL  $3"; exit 1; fi
}

check '.services.api.ports // [] | length' 0 "api publishes no port"
check '.services.web.ports // [] | length' 0 "web publishes no port; Traefik reaches it through the domain"
check '.services.api.environment.ASPNETCORE_ENVIRONMENT' Production "api runs as Production"
check '.services.api.init' true "api runs under an init process"
check '[.services.api.volumes[] | select(.source == "dataprotection-keys") | .target] | .[0]' \
  "$(jq -r '.services.api.environment.DataProtection__KeysPath' <<<"$config")" \
  "the key ring volume is mounted where DataProtection__KeysPath points"
check '.services.web.healthcheck.test | length > 0' true "web has a healthcheck, so Coolify can report its status"
check '.services.api.environment.Monobank__PublicBaseUrl' "${MONOBANK_PUBLIC_BASE_URL:-}" \
  "MONOBANK_PUBLIC_BASE_URL reaches the api, which registers no webhook while it is empty"
check '[.services.api.volumes[] | select(.source == "migration-dumps") | .target] | .[0]' \
  "$(jq -r '.services.api.environment.Migrations__DumpDirectory' <<<"$config")" \
  "the dump volume is mounted where Migrations__DumpDirectory points"
check '.services.api.environment.App__Release' "${SOURCE_COMMIT:-}" "SOURCE_COMMIT reaches the api as its release"
for service in api web; do
  check ".services.$service.restart" unless-stopped "$service has a restart policy"
  check ".services.$service.mem_limit | tonumber > 0" true "$service has a memory limit"
  check ".services.$service.logging.driver" json-file "$service logs through json-file"
  check ".services.$service.logging.options[\"max-size\"] != null and .services.$service.logging.options[\"max-file\"] != null" true \
    "$service rotates its logs"
done
