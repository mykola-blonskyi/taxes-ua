#!/usr/bin/env bash
# Asserts what the production compose file promises docs/deploy.md: Traefik reaches only `web`,
# every service restarts, is memory-limited and rotates its logs, `api` keeps its key ring across redeploys and exits when a startup guard throws,
# and `backup` publishes nothing, reads the api's database variable and lists every setting the backup script needs.
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
check '.services.api.environment.Migrations__DumpAgeRecipient' "$(jq -r '.services.backup.environment.BACKUP_AGE_RECIPIENT' <<<"$config")" \
  "api encrypts its pre-migration dumps to the same BACKUP_AGE_RECIPIENT the backup uses"
check '.services.api.environment | has("SOURCE_COMMIT") or has("App__Release")' false \
  "compose leaves SOURCE_COMMIT to Coolify, which turns a mention into an empty user variable"
check '.services.api.healthcheck.start_period' 5m0s "api may take five minutes to dump and migrate before it counts as unhealthy"
check '.services.backup.ports // [] | length' 0 "backup publishes no port"
check '.services.backup.init' true "backup runs under an init process"
check '.services.backup.environment.DATABASE_URL' "${DATABASE_URL:-}" \
  "backup reads DATABASE_URL, the variable that feeds the api's ConnectionStrings__Default"
check '.services.api.environment.ConnectionStrings__Default' "$(jq -r '.services.backup.environment.DATABASE_URL' <<<"$config")" \
  "backup and api get the same connection string"
check '.services.backup.environment | has("SOURCE_COMMIT") or has("App__Release")' false \
  "backup leaves SOURCE_COMMIT to Coolify as well"
for variable in BACKUP_AGE_RECIPIENT BACKUP_CHECK_AGE_IDENTITY \
  BACKUP_S3_ENDPOINT BACKUP_S3_BUCKET BACKUP_S3_ACCESS_KEY BACKUP_S3_SECRET_KEY \
  BACKUP_OFFSITE_S3_ENDPOINT BACKUP_OFFSITE_S3_BUCKET BACKUP_OFFSITE_S3_ACCESS_KEY BACKUP_OFFSITE_S3_SECRET_KEY BACKUP_OFFSITE_S3_REGION; do
  check ".services.backup.environment | has(\"$variable\")" true "backup lists $variable, so Coolify offers it as a variable"
done
for service in api web backup; do
  check ".services.$service.restart" unless-stopped "$service has a restart policy"
  check ".services.$service.mem_limit | tonumber > 0" true "$service has a memory limit"
  check ".services.$service.logging.driver" json-file "$service logs through json-file"
  check ".services.$service.logging.options[\"max-size\"] != null and .services.$service.logging.options[\"max-file\"] != null" true \
    "$service rotates its logs"
done

# The local override is not deployed, but it serves the Development sign-in, so it must stay on loopback.
local_config=$(docker compose -f docker-compose.yml -f docker-compose.local.yml config --format json)
if [ "$(jq -r '[.services.web.ports[] | select(.host_ip != "127.0.0.1")] | length' <<<"$local_config")" = 0 ]; then
  echo "ok    the local override publishes web on 127.0.0.1 only"
else
  echo "FAIL  the local override publishes web on 127.0.0.1 only"; exit 1
fi
check '.services.api.environment | keys | map(ascii_downcase) | index("auth__developmentsignin") != null' false "the deployed compose never enables the Development sign-in"
