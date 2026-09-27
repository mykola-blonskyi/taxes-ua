# Deploying to Coolify

The owner runs every step here by hand. Nothing in the repository deploys itself. Each step ends
with a check; do not start the next step until the check passes.

Facts this runbook relies on, established in #3 and #20:

- Domain `taxes.blonskyi.dev`. The VPS is reached with `ssh blonskyi`; `<vps-ip>` below is its
  public address.
- `blonskyi.dev` is on Cloudflare with a proxied wildcard record, so `taxes.blonskyi.dev` already
  resolves. Traefik obtains certificates through the Cloudflare DNS challenge, so none of this
  needs HTTP to reach the VPS.
- Google sign-in uses an OAuth client in the owner's Google Cloud project. taxes-ua gets a client
  of its own with a fresh secret before production (step 4).
- The database is a new role and database in Coolify's `shared-database` resource (ADR-006): the
  container `3p9qjnulllqn3bcjqokir0wq`, PostgreSQL 18, admin role `postgres`, on the `coolify`
  network. It already holds `fitness`, `todo`, `hub`, `plane` and `login`.

What the repository guarantees, checked in CI by `deploy/check-compose.sh` and
`deploy/smoke-test.sh` (which runs this compose file the way Traefik reaches it):

- `api` publishes no port and gets no domain. Only `web` is reachable, through Traefik.
- `api` runs as `Production` and refuses to start while `DATABASE_URL`, `GOOGLE_CLIENT_ID`,
  `GOOGLE_CLIENT_SECRET`, `ALLOWED_EMAILS`, `ALLOWED_HOSTS` or `PASSKEY_SERVER_DOMAIN` is empty.
  Its log names the missing variables, and the container exits instead of staying up unhealthy.
- The data-protection key ring lives in the `dataprotection-keys` volume, so a redeploy keeps the
  owner signed in (ADR-010).
- `api` answers only for `ALLOWED_HOSTS`, as forwarded by `web`, plus its own internal names for
  the healthcheck and the rewrite. A foreign host gets 400.
- Every response carries HSTS, `nosniff`, `X-Frame-Options: DENY` and a referrer policy, and pages
  carry a Content-Security-Policy.

## 0. The PostgreSQL instance

Every project on `shared-database` follows one convention, and taxes-ua follows it too: a login
role `<project>_app` with no other attributes owns the database `<project>`, keeps the default
privileges, and has no lines of its own in `pg_hba.conf`. ADR-006 records why.

Find out whether the instance is backed up (a Coolify scheduled backup, a cron job, a copy to
MinIO). Step 8 depends on the answer. On 2026-09-27 it had none.

**Check.** The instance answers:

```bash
ssh blonskyi 'docker exec 3p9qjnulllqn3bcjqokir0wq psql -U postgres -Atc "select version()"'
```

## 1. DNS

Nothing to do: the wildcard record already covers `taxes.blonskyi.dev`. Until step 6 deploys,
the domain answers 526 because Traefik has no route and no certificate for it yet.

**Check.** `dig +short taxes.blonskyi.dev` prints Cloudflare addresses.

## 2. Database role and database

Generate a password and keep it in the password manager:

```bash
openssl rand -base64 32 | tr -d '/+='
```

In CloudBeaver (`db.blonskyi.dev`), connected to the instance as `postgres`, run this as a script
(Alt+X) with auto-commit on, since `CREATE DATABASE` cannot run inside a transaction:

```sql
CREATE ROLE taxes_ua_app WITH LOGIN PASSWORD '<password>';
CREATE DATABASE taxes_ua OWNER taxes_ua_app;
```

That is all the rights the api needs. As the owner, the role creates the tables, function and
trigger the migrations add to `public`. Like the other projects' roles, it can still connect to
their databases, and they to this one, but none of them can read another's tables: each owns its
own objects and grants nothing.

Run it once. The instance logs the text of a failed statement (`log_min_error_statement =
error`), so if `CREATE ROLE` fails, for example because the role already exists, the password
lands in the server log. To change the password later, use
`ALTER ROLE taxes_ua_app PASSWORD '<password>';`, which carries the same risk if it fails.

**Check.** In CloudBeaver, create a connection with host `3p9qjnulllqn3bcjqokir0wq`, port `5432`,
database `taxes_ua`, user `taxes_ua_app` and the password, and click Test. It connects. A
connection over `127.0.0.1` inside the container proves nothing here: the image trusts local
connections without a password.

## 3. The api's route to the database

The instance is on the `coolify` network, so the `api` container reaches it by container name once
**Connect To Predefined Network** is enabled on the Coolify resource (step 5).

The connection string is Npgsql's format, not a URL:

```
Host=3p9qjnulllqn3bcjqokir0wq;Port=5432;Database=taxes_ua;Username=taxes_ua_app;Password=<password from step 2>
```

**Check.** Deferred to step 7, where `/api/health` reports `"database":true` only if this works.

## 4. Google OAuth client and secret

The client used during development is shared with two of the owner's other projects. Give
taxes-ua its own:

1. Google Cloud → the owner's project → APIs & Services → Credentials → Create OAuth client ID,
   type Web application.
2. Authorized redirect URI, exactly: `https://taxes.blonskyi.dev/api/auth/callback/google`. It ends
   in `/callback/google`. `/api/auth/callback` is the app's own landing step, and registering it
   instead is the usual cause of `redirect_uri_mismatch`.
3. Copy the new client ID and secret into step 5.

Then rotate the secret on the shared client as routine hygiene: add a new secret there, move the
two other projects to it, and disable the old one. Keeping the shared client for taxes-ua instead
works too, but only with a freshly rotated secret, and the rotation touches the other two projects.

**Check.** The new client shows exactly that redirect URI, and the shared client's previous secret
is disabled.

## 5. Coolify resource

1. New Resource → the GitHub repository `mykola-blonskyi/taxes-ua`, branch `main`, build pack
   **Docker Compose**, compose file `/docker-compose.yml`. Do not add `docker-compose.local.yml`.
2. Domain on the `web` service: `https://taxes.blonskyi.dev:3000`. The `:3000` is the container
   port Traefik forwards to; the public side stays on 443. Leave `api` without a domain.
3. Enable **Connect To Predefined Network** (step 3).
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
curl -s -o /dev/null -D - "$D/api/auth/login/google" | grep -i '^location'  # accounts.google.com, redirect_uri=https%3A%2F%2Ftaxes.blonskyi.dev%2Fapi%2Fauth%2Fcallback%2Fgoogle
curl -s -o /dev/null -D - "$D/" | grep -i -E '^(strict-transport|content-security|x-frame)'  # all three present
curl -s -m 5 http://<vps-ip>:8080/api/health || echo unreachable          # unreachable
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
ssh blonskyi 'sudo install -d -m 700 -o mykola /var/backups/taxes-ua'
ssh blonskyi 'crontab -e'
```

```
30 3 * * * BACKUP_DIR=/var/backups/taxes-ua /home/mykola/bin/taxes-ua-dump.sh docker exec 3p9qjnulllqn3bcjqokir0wq pg_dump -U postgres -Fc taxes_ua >> /var/backups/taxes-ua/cron.log 2>&1
```

The dumps sit on the same disk as the database; copy them off the VPS (MinIO or the
laptop) for them to survive losing it.

**Check.** Run the cron line once by hand, then list the newest dump's contents. It prints a
non-zero count:

```bash
f=$(ls -t /var/backups/taxes-ua/*.dump | head -1)
docker exec -i 3p9qjnulllqn3bcjqokir0wq pg_restore --list < "$f" | grep -c 'TABLE DATA'
```

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

**A bad release without a migration.** Revert the offending commit on `main` and deploy. That is
the documented path for the Compose build pack: Coolify builds whatever `main` holds.

**A bad release with a migration.** Migrations run when `api` starts and only move forward, so the
old code may not run against the new schema. Take a dump before deploying any release that
carries a migration (step 8's command, run by hand). To roll back:

1. Stop the resource in Coolify, so nothing writes to the database while it is restored.
2. Restore the dump taken before the release. **Everything written after that dump is lost.**

   ```bash
   docker exec -i 3p9qjnulllqn3bcjqokir0wq pg_restore -U postgres --clean --if-exists --no-owner --role=taxes_ua_app -d taxes_ua < <file>.dump
   ```

3. Revert the release on `main`.
4. Deploy, which starts the old code against the restored schema.

**Check.** Step 7's commands pass again.

**Abandoning the first deploy.** Stop and delete the Coolify resource, remove the DNS record,
then drop what step 2 created. This deletes the data:

```bash
ssh blonskyi 'docker exec 3p9qjnulllqn3bcjqokir0wq psql -U postgres -c "DROP DATABASE taxes_ua" -c "DROP ROLE taxes_ua_app"'
```

**Ending every session.** A stolen session cookie cannot be revoked one by one (ADR-009). Rotate
the key ring instead: delete the `key-*.xml` files in the `dataprotection-keys` volume and restart
`api`. Every cookie, including the owner's, stops working at once.
