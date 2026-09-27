# Deploying to Coolify

The owner runs every step here by hand. Nothing in the repository deploys itself. Each step ends
with a check; do not start the next step until the check passes.

Facts this runbook relies on, established in #3 and #20:

- Domain `taxes.blonskyi.dev`. VPS `23.88.118.219`, reached with `ssh blonskyi`.
- `blonskyi.dev` is on Cloudflare. The subdomain record does not exist yet.
- Google OAuth client in Google Cloud project `37938836769`. Its secret was exposed in an agent
  transcript and must be rotated before production.
- The database is a new role and database in the PostgreSQL instance already running on the VPS
  (ADR-006), not a new Coolify PostgreSQL resource.

What the repository guarantees, checked in CI by `deploy/check-compose.sh`:

- `api` publishes no port and gets no domain. Only `web` is reachable, through Traefik.
- `api` runs as `Production` and refuses to start while `DATABASE_URL`, `GOOGLE_CLIENT_ID`,
  `GOOGLE_CLIENT_SECRET`, `ALLOWED_EMAILS`, `ALLOWED_HOSTS` or `PASSKEY_SERVER_DOMAIN` is empty.
  Its log names the missing variables, and the container exits instead of staying up unhealthy.
- The data-protection key ring lives in the `dataprotection-keys` volume, so a redeploy keeps the
  owner signed in (ADR-010).

## 0. Find the PostgreSQL instance

On the VPS, find out how the instance runs:

```bash
ssh blonskyi 'docker ps --format "{{.Names}}\t{{.Image}}\t{{.Networks}}" | grep -i postgres; systemctl is-active postgresql'
```

Pick the admin command the rest of this runbook calls `$PSQL`, and set it in your shell on the VPS:

```bash
PSQL='docker exec -i <postgres-container> psql -U postgres'   # a container
PSQL='sudo -u postgres psql'                                  # a host service
```

Also find out whether that instance is already backed up (a cron job, a Coolify backup, a copy to
MinIO). Step 8 depends on the answer.

**Check.** `$PSQL -c 'select version()'` prints the server version.

## 1. DNS

In Cloudflare, add an `A` record `taxes` → `23.88.118.219`. Start with **DNS only** (grey cloud) so
Traefik can obtain its Let's Encrypt certificate over HTTP. Switch to proxied later only if the
owner's other Coolify subdomains are proxied too, with SSL mode **Full (strict)**.

**Check.** `dig +short taxes.blonskyi.dev` prints `23.88.118.219` (or Cloudflare addresses once
proxied).

## 2. Database role and database

Copy the script to the VPS and run it as the admin. It is safe to run again.

```bash
scp deploy/postgres/create-role.sql blonskyi:/tmp/
ssh blonskyi
$PSQL -v role=taxes_ua -v db=taxes_ua < /tmp/create-role.sql
```

It creates the role `taxes_ua` (login, no other attributes) and the database `taxes_ua` owned by
the admin, revokes `CONNECT` on it from `PUBLIC`, and grants the role only `CONNECT` plus
`USAGE, CREATE` on schema `public`. That is what the api's migrations need: they create tables, a
function and a trigger in `public`. The role cannot drop the database or create schemas.

Generate a password on your laptop (`openssl rand -base64 32 | tr -d '/+='`), then set it
interactively so it never lands in shell history or the server log:

```bash
ssh -t blonskyi "docker exec -it <postgres-container> psql -U postgres -c '\password taxes_ua'"
```

The script ends by listing every other database the role can still connect to. PostgreSQL grants
`CONNECT` to `PUBLIC` on every new database and has no per-role deny, so this list is usually not
empty (`postgres`, `template1` and every other project's database). Close it in `pg_hba.conf`
rather than by revoking `PUBLIC` on databases other projects rely on:

1. Find the file with `$PSQL -tAc 'show hba_file'`.
2. Put the four lines of `deploy/postgres/pg_hba.conf.snippet` at the **top** of it. The file stops
   at the first matching line, so they must come before any `all all` line.
3. Reload with `$PSQL -c 'select pg_reload_conf()'`, then confirm
   `$PSQL -c 'select line_number, error from pg_hba_file_rules where error is not null'` returns
   no rows.

**Check.** From inside the instance's host or container, over TCP with the new password:

```bash
psql "host=127.0.0.1 user=taxes_ua dbname=taxes_ua" -c 'select 1'   # succeeds
psql "host=127.0.0.1 user=taxes_ua dbname=postgres" -c 'select 1'   # fails: pg_hba.conf rejects
```

`deploy/postgres/test-create-role.sh` runs this whole step against a throwaway container, with a
second project's database next to it, and is the reference for what "passes" looks like.

## 3. The api's route to the database

`DATABASE_URL` needs a host the `api` container can reach. It depends on step 0:

- **The instance is a container.** Enable **Connect To Predefined Network** on the Coolify resource
  (step 5), and make sure the PostgreSQL container is on the `coolify` network too
  (`docker network connect coolify <postgres-container>` if it is not). The host is the container
  name.
- **The instance is a host service.** Enable **Connect To Predefined Network**, use the `coolify`
  network's gateway as the host
  (`docker network inspect coolify -f '{{(index .IPAM.Config 0).Gateway}}'`), and make sure
  `listen_addresses` includes that address.

The connection string is Npgsql's format, not a URL:

```
Host=<host>;Port=5432;Database=taxes_ua;Username=taxes_ua;Password=<password from step 2>
```

**Check.** Deferred to step 7, where `/api/health` reports `"database":true` only if this works.

## 4. Google OAuth client and secret

The current client is shared with two of the owner's other projects. Give taxes-ua its own:

1. Google Cloud → project `37938836769` → APIs & Services → Credentials → Create OAuth client ID,
   type Web application.
2. Authorized redirect URI, exactly: `https://taxes.blonskyi.dev/api/auth/callback/google`. It ends
   in `/callback/google`. `/api/auth/callback` is the app's own landing step, and registering it
   instead is the usual cause of `redirect_uri_mismatch`.
3. Copy the new client ID and secret into step 5.

Then deal with the exposed secret on the shared client: add a new secret there, move the two other
projects to it, and disable the old one. Keeping the shared client for taxes-ua instead works too,
but the rotation is then mandatory before this deploy, and it touches the other two projects.

**Check.** The client shows exactly that redirect URI, and the exposed secret is disabled.

## 5. Coolify resource

1. New Resource → the GitHub repository `mykola-blonskyi/taxes-ua`, branch `main`, build pack
   **Docker Compose**, compose file `/docker-compose.yml`. Do not add `docker-compose.local.yml`.
2. Domain on the `web` service: `https://taxes.blonskyi.dev:3000`. The `:3000` is the container
   port Traefik forwards to; the public side stays on 443. Leave `api` without a domain.
3. Enable **Connect To Predefined Network** if step 3 needs it.
4. Environment variables (Coolify lists them from the compose file):

   | Variable | Value |
   | --- | --- |
   | `DATABASE_URL` | the connection string from step 3 |
   | `GOOGLE_CLIENT_ID` | from step 4 |
   | `GOOGLE_CLIENT_SECRET` | from step 4 |
   | `ALLOWED_EMAILS` | the owner's email |
   | `ALLOWED_HOSTS` | `taxes.blonskyi.dev` |
   | `PASSKEY_SERVER_DOMAIN` | `taxes.blonskyi.dev` |

   Set these only in Coolify. Never put the domain in a local `.env`: `docker-compose.local.yml`
   overrides only the environment name and the connection string, so a local run would inherit it
   and refuse to start.

**Check.** Persistent Storage lists the `dataprotection-keys` volume. The `api` service has no
domain. All six variables have values.

## 6. First deploy

Click Deploy.

**Check.** The deployment log ends successfully, both containers are healthy, and the `api` log
has no `Missing required configuration` or `InvalidOperationException` line. If `api` exits with
code 134, its log names the variable to fix.

## 7. Verify the deployment

From the laptop:

```bash
D=https://taxes.blonskyi.dev
curl -s "$D/api/health"                                                   # {"status":"ok","database":true}
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/openapi/v1.json"         # 404
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/auth/login/development?email=<allowlisted email>"  # 404
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/auth/me"                 # 401
curl -sI "$D/api/auth/login/google" | grep -i '^location'                 # accounts.google.com, redirect_uri=https%3A%2F%2Ftaxes.blonskyi.dev%2Fapi%2Fauth%2Fcallback%2Fgoogle
curl -s -m 5 http://23.88.118.219:8080/api/health || echo unreachable     # unreachable
```

On the VPS, `api` has no host port and no Traefik router:

```bash
docker ps --format '{{.Names}}\t{{.Ports}}' | grep -E '^(api|web)-'       # no 0.0.0.0:... mappings
docker inspect $(docker ps -qf name='^api-') --format '{{json .Config.Labels}}' | tr ',' '\n' | grep -i 'traefik.http.routers'  # prints nothing
```

Then sign in with Google in a browser and walk every screen against a local run: home
(dashboard and next step), transactions (add, edit and delete a receipt, export CSV and XLSX),
payments (add and delete a payment), periods, settings (tax year parameters, download a backup and
restore it), and history. Delete what you added, or restore a backup taken before.

Finally, click Redeploy in Coolify and reload the page. You stay signed in. If you are sent to the
login screen, the key ring volume is not mounted.

**Check.** Every command prints the expected value, every screen behaves as it does locally, and
the session survives the redeploy. Tick the matching boxes on #20.

## 8. Backups

Skip this step if step 0 found a backup that covers the whole instance.

Otherwise schedule `deploy/postgres/dump.sh`. It writes one `pg_dump -Fc` file per run, never
leaves a partial file behind, and prunes dumps older than `RETAIN_DAYS` (default 14) only after a
dump succeeds.

```bash
scp deploy/postgres/dump.sh blonskyi:/home/mykola/bin/taxes-ua-dump.sh
ssh blonskyi 'sudo mkdir -p /var/backups/taxes-ua && sudo chown mykola /var/backups/taxes-ua'
ssh blonskyi 'crontab -e'
```

```
30 3 * * * BACKUP_DIR=/var/backups/taxes-ua /home/mykola/bin/taxes-ua-dump.sh docker exec <postgres-container> pg_dump -U postgres -Fc taxes_ua >> /var/backups/taxes-ua/cron.log 2>&1
```

For a host service, replace `docker exec <postgres-container> pg_dump -U postgres` with
`sudo -u postgres pg_dump`. The dumps sit on the same disk as the database; copy them off the VPS
(MinIO or the laptop) for them to survive losing it.

**Check.** Run the cron line once by hand, then
`pg_restore --list /var/backups/taxes-ua/<file>.dump | grep -c 'TABLE DATA'` prints a non-zero count.

## 9. Checks that need the real domain

These tickets were built and tested locally, but their last criteria need HTTPS on the real
domain. Do them now and tick them on their issues:

- **#16 Passkey.** Register a passkey in settings, sign out, and sign in with it on iOS Safari,
  Android Chrome and desktop.
- **#18 PWA.** Lighthouse marks the app installable. Install it on iOS and Android and confirm it
  opens in standalone mode.
- **#15 Prototype import.** Import a real export from the prototype, check the receipts and
  payments against it, then import the same file again and confirm the record count does not
  change.

## Rollback

**A bad release.** Revert the offending commit on `main` and deploy, or pick the previous
deployment in Coolify and redeploy it. Migrations run on startup and only move forward, so before
deploying a release that carries a migration, take a dump (step 8's command, run by hand). If the
schema change must be undone, restore that dump into the database:

```bash
docker exec -i <postgres-container> pg_restore -U postgres --clean --if-exists -d taxes_ua < <file>.dump
```

**Abandoning the first deploy.** Stop and delete the Coolify resource, remove the DNS record,
remove the `pg_hba.conf` lines and reload, then drop what step 2 created. This deletes the data:

```bash
$PSQL -c 'DROP DATABASE taxes_ua' -c 'DROP ROLE taxes_ua'
```

**Ending every session.** A stolen session cookie cannot be revoked one by one (ADR-009). Rotate
the key ring instead: delete the `key-*.xml` files in the `dataprotection-keys` volume and restart
`api`. Every cookie, including the owner's, stops working at once.
