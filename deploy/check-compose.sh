#!/usr/bin/env bash
# Asserts what the production compose file promises docs/deploy.md: Traefik reaches only `web`,
# and `api` keeps its key ring across redeploys and exits when a startup guard throws.
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
