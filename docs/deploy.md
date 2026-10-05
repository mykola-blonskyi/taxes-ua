# Deploying to Coolify

The owner runs every setup step here by hand. Each step ends with a check; do not start the next
step until the check passes. Once set up, every push to `main` deploys itself: the `Deploy to
Coolify` job in `.github/workflows/ci.yml` calls the resource's deploy webhook after the API tests,
the web lint and build, both image builds and the deploy checks pass (see "Automatic deploys").

Facts this runbook relies on, established in #3 and #20:

- Domain `taxes.blonskyi.dev`. The VPS is reached with `ssh blonskyi`; `<vps-ip>` below is its
  public address. This file writes neither the address nor any container id down (the repository is
  public, and the address defeats the Cloudflare proxy); look them up on the VPS or in Coolify.
- `blonskyi.dev` is on Cloudflare with one proxied `A` record per subdomain, all pointing at
  `<vps-ip>`. There is no wildcard. Traefik obtains certificates through the
  Cloudflare DNS challenge, so none of this needs HTTP to reach the VPS.
- Google sign-in uses the OAuth client shared with the owner's other projects, with a secret
  rotated before production (step 4).
- The database is a new role and database in Coolify's `shared-database` resource (ADR-006): the
  container `<pg-container>` (its name is on the resource's page in Coolify, and `docker ps` on the VPS shows it), PostgreSQL 18, admin role `postgres`, on the `coolify`
  network. It already holds `fitness`, `todo`, `hub`, `plane` and `login`.

What the repository guarantees, checked in CI by `deploy/check-compose.sh` and
`deploy/smoke-test.sh` (which runs this compose file the way Traefik reaches it):

- `api` publishes no port and gets no domain. Only `web` is reachable, through Traefik.
- `api` runs as `Production` and refuses to start while `DATABASE_URL`, `GOOGLE_CLIENT_ID`,
  `GOOGLE_CLIENT_SECRET`, `ALLOWED_EMAILS`, `ALLOWED_HOSTS` or `PASSKEY_SERVER_DOMAIN` is empty.
  Its log names the missing variables, and the container exits instead of staying up unhealthy.
- Every service restarts `unless-stopped`, is memory-limited (`api` 512 MB, `web` 384 MB, `backup`
  256 MB) and rotates its logs (10 MB, 5 files). `api` dumps the database to the `migration-dumps` volume before it runs a
  pending migration, and does not migrate if that fails (ADR-027, "Rollback").
- `MONOBANK_TOKEN_ENCRYPTION_KEY` is the one secret that is *not* required to start (ADR-011): left
  empty, the api still comes up and the monobank settings section answers "not configured" instead
  of 500s. Set it whenever the owner is ready to connect monobank.
- `MONOBANK_PUBLIC_BASE_URL` is optional too (ADR-012). Empty, the api never registers a monobank
  webhook and new operations arrive through "sync now" and the nightly run at 03:00 Kyiv. Set to the
  public origin (`https://taxes.blonskyi.dev`), the api registers
  `<origin>/api/monobank/webhook/<secret>` for the owner after each token save, and a new operation
  appears within a minute or two. The secret in that path is what keeps strangers from queuing syncs,
  so keep Traefik access logs off, or rotate the secret by saving the token again after sharing one.
- `TELEGRAM_BOT_TOKEN` is optional as well (ADR-015). Empty, settings shows Telegram as unavailable and nothing
  polls. Set, the api long-polls the bot for the owner pressing Start on the link settings shows. The token
  travels in the URL path of every Bot API call, so the api logs no Telegram URL and never returns the token;
  one process may poll a bot, so a local stack must not be given the production token.
- The data-protection key ring lives in the `dataprotection-keys` volume, so a redeploy keeps the
  owner signed in (ADR-010).
- The `backup` service publishes no port and backs up `taxes_ua` each night, encrypted, to the MinIO on the
  VPS and, when configured, to a second target off the VPS. It proves a restore every Sunday (ADR-031,
  step 8). The smoke test runs one backup and one restore check against S3-compatible storage and asserts
  that both succeed, that the owner's recovery key decrypts the stored file, and that no secret reaches
  the log. With its variables empty, the service still starts and each nightly run records a failed
  backup. The first Sunday's restore check then fails too, and that alerts the owner at once. Configure
  step 8b before the first Sunday 01:00 UTC after this deploys, or expect that alert.
- `api` answers only for `ALLOWED_HOSTS`, as forwarded by `web`, plus its own internal names for
  the healthcheck and the rewrite. A foreign host gets 400.
- Every response carries HSTS, `nosniff`, `X-Frame-Options: DENY`, a referrer policy and a
  Permissions-Policy, and pages carry a nonce-based Content-Security-Policy.

## 0. The PostgreSQL instance

Every project on `shared-database` follows one convention, and taxes-ua follows it too: a login
role `<project>_app` with no other attributes owns the database `<project>`, keeps the default
privileges, and has no lines of its own in `pg_hba.conf`. ADR-006 records why.

Find out whether the instance is backed up (a Coolify scheduled backup, a cron job, a copy to
MinIO). Step 8 depends on the answer. On 2026-09-27 it had none.

**Check.** The instance answers:

```bash
ssh blonskyi 'docker exec <pg-container> psql -U postgres -Atc "select version()"'
```

## 1. DNS

In Cloudflare, add an `A` record `taxes` → `<vps-ip>`, proxied, like the other subdomains. Until
step 6 deploys, the domain answers 526 because Traefik has no route and no certificate for it yet.

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

**Check.** In CloudBeaver, create a connection with host `<pg-container>`, port `5432`,
database `taxes_ua`, user `taxes_ua_app` and the password, and click Test. It connects. A
connection over `127.0.0.1` inside the container proves nothing here: the image trusts local
connections without a password.

## 3. The api's route to the database

The instance is on the `coolify` network, so the `api` container reaches it by container name once
**Connect To Predefined Network** is enabled on the Coolify resource (step 5).

The connection string is Npgsql's format, not a URL:

```
Host=<pg-container>;Port=5432;Database=taxes_ua;Username=taxes_ua_app;Password=<password from step 2>
```

**Check.** Deferred to step 7, where `/api/health` reports `"database":true` only if this works.

**Limits the api adds.** For one owner the api fills in three limits of its own on this string
(`DatabaseConnection.WithDefaults`), unless the string already names them:

| Npgsql key | Default | Why |
| --- | --- | --- |
| `Maximum Pool Size` | `10` | the api and its hosted services need a handful of connections; Npgsql's own 100 could use up Postgres's connection limit |
| `Command Timeout` | `60` (seconds) | a statement running longer is stuck, not slow |
| `Options` | `-c lock_timeout=30000` (30 s) | a statement waiting for a lock, the owner's advisory lock included, errors out instead of queueing behind a stuck holder |

To change one, add the key to `DATABASE_URL`, for example `;Maximum Pool Size=20` or
`;Options=-c lock_timeout=60000`. The `backup` service reads the same string and ignores these keys.

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
   | `DATABASE_URL` | the connection string from step 3. The `backup` service reads it too. It accepts spaces around keys and values, and a value in single or double quotes may hold `;` and `=` (a doubled quote inside stands for one), as in Npgsql |
   | `GOOGLE_CLIENT_ID` | from step 4 |
   | `GOOGLE_CLIENT_SECRET` | from step 4 |
   | `ALLOWED_EMAILS` | the owner's email |
   | `ALLOWED_HOSTS` | `taxes.blonskyi.dev` |
   | `PASSKEY_SERVER_DOMAIN` | `taxes.blonskyi.dev` |
   | `MONOBANK_TOKEN_ENCRYPTION_KEY` | `openssl rand -base64 32`, once, kept in the password manager (ADR-011) |
   | `MONOBANK_PUBLIC_BASE_URL` | `https://taxes.blonskyi.dev`, or empty to run without the webhook (ADR-012) |
   | `TELEGRAM_BOT_TOKEN` | the token @BotFather gives, see "Telegram bot" below, or empty to run without Telegram (ADR-015) |
   | `SMTP_HOST`, `SMTP_PORT`, `SMTP_TLS`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` | the mail server, see "Email" below, or all empty to run without email (ADR-022) |
   | `BACKUP_AGE_RECIPIENT` | the recovery public key, `age1...`, step 8b |
   | `BACKUP_CHECK_AGE_IDENTITY` | the check private key, `AGE-SECRET-KEY-1...`, step 8b |
   | `BACKUP_S3_ENDPOINT`, `BACKUP_S3_BUCKET`, `BACKUP_S3_ACCESS_KEY`, `BACKUP_S3_SECRET_KEY` | the MinIO bucket and key, step 8b |
   | `BACKUP_OFFSITE_S3_ENDPOINT`, `BACKUP_OFFSITE_S3_BUCKET`, `BACKUP_OFFSITE_S3_ACCESS_KEY`, `BACKUP_OFFSITE_S3_SECRET_KEY`, `BACKUP_OFFSITE_S3_REGION` | empty for now; an optional off-VPS bucket, step 8b |

   Set these only in Coolify. Never put the domain in a local `.env`: `docker-compose.local.yml`
   overrides only the environment name and the connection string, so a local run would inherit it
   and refuse to start.

Coolify 4.2.0 can refuse to save the General form while a compose service has no entry in the resource's
`docker_compose_domains`. The workaround is the same as for `api`: give `backup` an entry with no domain,
`"backup":{"domain":null}`, next to the existing ones.

**Check.** Persistent Storage lists the `dataprotection-keys` volume. The `api` and `backup` services
have no domain. The six required variables have values. The two `MONOBANK_*` ones, `TELEGRAM_BOT_TOKEN`
and the six `SMTP_*` ones may stay empty. The `BACKUP_*` ones may stay empty until step 8b, and every
night without them is a failed backup.

### Telegram bot

Reminders reach the owner through a bot the owner creates; nothing is paid for and no public route is needed.

1. In Telegram open @BotFather and send `/newbot`. Give it a name and a username ending in `bot`, for
   example `taxes_ua_reminders_bot`. BotFather answers with a token like `123456789:AA...`.
2. Set it as `TELEGRAM_BOT_TOKEN` in Coolify, next to the other variables, and redeploy. Do not put it in a
   file, in chat or in a local `.env`.
3. Optional: `/setprivacy` is irrelevant (the bot only talks in private chats), and `/setdescription` can say
   what the bot is for.
4. The bot must not have a webhook: a webhook makes Telegram refuse `getUpdates` (409) and linking would never
   complete. The api removes one with `deleteWebhook` on the first 409 and logs a warning saying so; if
   "Telegram updates could not be read" keeps repeating after that, another process is polling the same bot
   (see below).
5. In the app open settings, the Notifications tab, and press "Connect Telegram". Open the link, press Start
   in Telegram, and the tab shows the channel as connected within a few seconds. "Send a test message"
   confirms delivery.

From then on the api sends deadline reminders to the chat at 09:00 Kyiv (Rule 17). Each ends with a link to
the app, built from `APP_PUBLIC_URL` when set and otherwise from the first `ALLOWED_HOSTS` domain, so production
needs nothing more; set `APP_PUBLIC_URL` only when the app is reached at another address.

If the token leaks, use `/revoke` in @BotFather, put the new token in Coolify and redeploy: the api reads
updates for the new bot from its own beginning and the chat is linked again from settings. A bot must not
be polled from two places at once, so a local stack keeps `TELEGRAM_BOT_TOKEN` empty or uses a second bot.

### Email

Reminders and the address confirmation go out through an SMTP server you already have (a mailbox at your
domain's host, a relay such as Brevo's or Mailjet's free tier, your own Postfix). The app only sends; it needs no
inbound mail and no new service.

| Variable | Meaning |
| --- | --- |
| `SMTP_HOST` | the server's name. Empty switches email off |
| `SMTP_PORT` | optional. 587 for `starttls`, 465 for `implicit`, otherwise what the server documents |
| `SMTP_TLS` | `starttls` (default), `implicit` (TLS from the first byte, usually port 465) or `none` |
| `SMTP_USER`, `SMTP_PASSWORD` | the sign-in, both or neither. Refused with `SMTP_TLS=none` unless the host is on this machine (`localhost`, a loopback address), so a password never crosses a network in the clear |
| `SMTP_FROM` | the sender, `noreply@taxes.example.com` or `Taxes UA <noreply@taxes.example.com>`. Use an address the server may send as, and give its domain SPF and DKIM or the mail lands in spam |

Set them in Coolify only, like every secret, and redeploy. `SMTP_PASSWORD` is read from configuration and
never logged, never put in a URL and never returned by the api. A half-set or invalid configuration (a
password without a user, a malformed sender, an unknown `SMTP_TLS`) is not a startup error: the api logs
one warning that names the variable, not its value, and settings shows email as unavailable. Email also
needs an address for the confirmation link, which `APP_PUBLIC_URL` or the first `ALLOWED_HOSTS` domain
already provides in production.

In the app open settings, the Notifications tab, type the address and press "Send confirmation". The
owner opens the link in the email while signed in (it expires after 24 hours; "Send the link again" issues
a new one), and from then on the address gets the reminders. "Send a test email" confirms delivery.
A refused sign-in, an unreachable server or a rejected address shows on the tab with its reason.

The confirmation link is signed with the data-protection key ring, so it survives a redeploy because the
ring is on the `dataprotection-keys` volume, and an unconfirmed link dies with the ring if the volume is lost.

A local stack needs a sink instead of a real server, for example `SMTP_HOST=host.docker.internal`,
`SMTP_PORT=2525`, `SMTP_TLS=none`, `SMTP_FROM=noreply@taxes.test` and `APP_PUBLIC_URL=http://localhost:3000`
against any SMTP catcher listening on the host. Never point a local stack at a real mailbox you do not want mailed.

## 6. First deploy

Click Deploy.

**Check.** The deployment log ends successfully, both containers are healthy, and the `api` log
has no `Missing required configuration` or `InvalidOperationException` line. If `api` exits with
code 134, its log names the variable to fix.

## 7. Verify the deployment

From the laptop:

```bash
D=https://taxes.blonskyi.dev
curl -s "$D/api/health"                                                   # {"status":"ok","database":true,"release":"<commit>"}
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/openapi/v1.json"         # 404
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/auth/login/development?email=<allowlisted email>"  # 404
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/auth/me"                 # 401
curl -s -o /dev/null -D - "$D/api/auth/login/google" | grep -i '^location'  # accounts.google.com, redirect_uri=https%3A%2F%2Ftaxes.blonskyi.dev%2Fapi%2Fauth%2Fcallback%2Fgoogle
curl -s -o /dev/null -D - "$D/" | grep -i -E '^(strict-transport|content-security|x-frame)'  # all three present
curl -s -m 5 http://<vps-ip>:8080/api/health || echo unreachable          # unreachable
curl -s -o /dev/null -w '%{http_code}\n' "$D/api/monobank/webhook/0"      # 404
```

With `MONOBANK_PUBLIC_BASE_URL` set and monobank connected, settings shows the bank notifications as
registered within a minute of saving the token. If it shows them failed, monobank could not reach
the URL: its check is a plain GET from the bank's servers, so a Cloudflare bot challenge on the
domain would fail it.

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

Two backups run, and they protect against different things:

- **The instance dump (8a).** Coolify dumps every database on `shared-database` once a day. The copies are
  on the VPS disk and in MinIO, on the same disk. They survive a broken or wiped database. They do not
  survive losing the server, and restoring one brings back every project at once.
- **The app backup (8b).** The `backup` service dumps `taxes_ua` alone each night at 01:00 UTC. It
  encrypts the dump and sends it to MinIO and, when configured, to a bucket off the VPS. Every Sunday it
  restores the newest copy into a scratch database to prove that the copy works (ADR-031). This is the one
  to use when the server is lost, once it has an off-VPS bucket; until then it shares the VPS disk.

### 8a. The instance dump

Coolify backs up the whole `shared-database` instance with `pg_dumpall`. The dumps go to local storage on the VPS
and to the owner's MinIO. This covers `taxes_ua` along with every other project on the instance.
It was set up on 2026-09-28. Redo these steps only if it is gone.

1. **Bucket.** In the MinIO console (`https://s3-console.blonskyi.dev`), create the bucket
   `coolify-backups`.
2. **Access key.** The Community Edition console has no key management, so create the key with
   `mc` inside the MinIO container. The policy limits the key to that one bucket. Open a shell:

   ```bash
   ssh -t blonskyi "docker exec -it <minio-container> sh"
   ```

   and paste:

   ```sh
   mc alias set root http://127.0.0.1:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
   cat > /tmp/coolify-backups.json <<'EOF'
   {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Action":["s3:GetBucketLocation","s3:ListBucket"],"Resource":["arn:aws:s3:::coolify-backups"]},{"Effect":"Allow","Action":["s3:PutObject","s3:GetObject","s3:DeleteObject"],"Resource":["arn:aws:s3:::coolify-backups/*"]}]}
   EOF
   mc admin accesskey create root --name coolify-backups --policy /tmp/coolify-backups.json
   rm -f /tmp/coolify-backups.json; mc alias remove root >/dev/null; exit
   ```

   The command prints the access key and the secret key once.
3. **DNS.** The `s3` record in Cloudflare must be **DNS only**, not proxied. Coolify pins the S3
   host for `mc` with `--resolve` and writes IPv6 addresses in brackets, which `mc` rejects. A
   proxied record always carries Cloudflare's IPv6, so the upload fails with
   `invalid DNS resolve entry ... ParseAddr("[2a06:...]")`. An internal endpoint such as
   `http://minio:9000` is no way around this, because Coolify refuses private addresses for S3.
4. **S3 storage.** In Coolify, open S3 Storages → New. Set the endpoint to
   `https://s3.blonskyi.dev`, the bucket to `coolify-backups`, the region to `us-east-1`, and the
   keys from item 2.
5. **Schedule.** Open `shared-database` → Backups, add a daily schedule, and turn on Save to S3.

**Check.** Click Backup Now. The run shows Success, with Local Storage and S3 Storage both green.
The newest dump is intact and contains `taxes_ua`:

```bash
ssh blonskyi 'docker run --rm -v /data/coolify/backups/databases/<team>/shared-database-<pg-container>:/b:ro alpine:3 sh -c "f=\$(ls -t /b/*.gz | head -1); gzip -t \$f && zcat \$f | grep -c \"connect taxes_ua\""'
```

It prints a non-zero count.

MinIO runs on the same VPS and disk as the database. These dumps survive a broken or wiped
database, but not the loss of the server. Section 8b covers that.

### 8b. The app backup and its weekly restore check

The `backup` service needs two age keys and a MinIO bucket. A bucket off the VPS is optional and is not
set up for now (owner's decision, 2026-10-03): with its five variables empty, the service skips it. Every
item is done by the owner. Nothing here is in the repository.

1. **Keys.** On the laptop, with `age` installed (`brew install age`):

   ```bash
   age-keygen -o taxes-ua-recovery.key      # prints "Public key: age1..."
   age-keygen -o taxes-ua-check.key
   ```

   Store `taxes-ua-recovery.key` in the password manager. It is the **recovery key**, and it never goes to
   the server: a restore after losing the server needs it, so keep it where losing the server cannot take
   it. Its public line (`age1...`) is `BACKUP_AGE_RECIPIENT`. The line of `taxes-ua-check.key` that starts
   with `AGE-SECRET-KEY-1` is `BACKUP_CHECK_AGE_IDENTITY`. The weekly check decrypts with it, so it lives in
   Coolify. Store it in the password manager too, then delete both files from the laptop.
2. **MinIO bucket and key.** In the MinIO console (`https://s3-console.blonskyi.dev`), create the bucket
   `taxes-ua-backups`. Create a key limited to it with `mc`, in the shell from 8a's item 2:

   ```sh
   mc alias set root http://127.0.0.1:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
   cat > /tmp/taxes-ua-backups.json <<'EOF'
   {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Action":["s3:GetBucketLocation","s3:ListBucket"],"Resource":["arn:aws:s3:::taxes-ua-backups"]},{"Effect":"Allow","Action":["s3:PutObject","s3:GetObject","s3:DeleteObject"],"Resource":["arn:aws:s3:::taxes-ua-backups/*"]}]}
   EOF
   mc admin accesskey create root --name taxes-ua-backups --policy /tmp/taxes-ua-backups.json
   rm -f /tmp/taxes-ua-backups.json; mc alias remove root >/dev/null; exit
   ```

   In Coolify set `BACKUP_S3_ENDPOINT` to `https://s3.blonskyi.dev`, `BACKUP_S3_BUCKET` to
   `taxes-ua-backups`, and `BACKUP_S3_ACCESS_KEY` and `BACKUP_S3_SECRET_KEY` to the two keys it printed.
3. **The off-VPS bucket (optional, skip for now).** Until this is done, the copies live on the VPS disk
   only, so losing the server still loses them; the recovery key in the password manager survives, but there
   is nothing for it to open. To add it later, use any S3-compatible provider outside the VPS's provider and account, for
   example Backblaze B2 or Cloudflare R2. Create a private bucket and a key limited to it. The key needs
   list, read, write and delete, because retention deletes old copies. If the provider offers versioning or
   object lock, turn it on with a retention of at least 14 days, so a leaked key cannot erase the history.
   In Coolify set `BACKUP_OFFSITE_S3_ENDPOINT` (the provider's S3 endpoint URL),
   `BACKUP_OFFSITE_S3_BUCKET`, `BACKUP_OFFSITE_S3_ACCESS_KEY` and `BACKUP_OFFSITE_S3_SECRET_KEY`. Set
   `BACKUP_OFFSITE_S3_REGION` when the provider wants one: B2 wants the region in its endpoint, such as
   `us-west-004`, and R2 wants `auto`. Empty means `us-east-1`. Leave all five empty to run without an
   off-VPS copy, as now. Setting only some of them is a failed backup that names the missing ones.
4. **Keys into Coolify.** Set `BACKUP_AGE_RECIPIENT` and `BACKUP_CHECK_AGE_IDENTITY`, then Redeploy.

**Check.** Run one backup and one check by hand, then read what they recorded:

```bash
B=$(ssh blonskyi "docker ps -qf name='^backup-tpx1vnmef2rgcbjjpqlbvour'")
ssh blonskyi "docker exec $B backup-db backup"
ssh blonskyi "docker exec $B backup-db check"
P='docker exec -i 3p9qjnulllqn3bcjqokir0wq'
ssh blonskyi "$P psql -U postgres -d taxes_ua -Atc 'select \"Job\", \"FinishedAt\", \"Succeeded\", \"Detail\" from \"DatabaseBackupRuns\" order by \"Id\" desc limit 5'"
```

Both commands exit 0, and the two newest rows are a successful `Backup` and `RestoreCheck`. Then prove
the recovery key, which the weekly check cannot do. On the laptop, download the newest object from MinIO
(or the off-VPS bucket, once there is one), put the recovery key from the password manager in a file, and run:

```bash
age -d -i taxes-ua-recovery.key taxes_ua-<time>.dump.age | pg_restore --list | head
```

It lists the tables. Delete the key file afterwards. Repeat this after changing either key.

**What runs and what is kept.** Each night at 01:00 UTC, one dump goes to `daily/` on every target, plus a
copy to `weekly/` on Sundays. Retention keeps 14 days of `daily/` and 8 weeks of `weekly/` on each target.
It deletes only after that night's upload to the target succeeded. On Sundays, after the backup, the
restore check runs. For each target, it restores the newest copy into a scratch database inside the
container with `--single-transaction --exit-on-error`. It checks the last `__EFMigrationsHistory` row and
that the restored tables hold rows, then drops the scratch database. A copy older than 48 hours fails the
check.

**Alerts.** If the newest check failed, or no check has succeeded for 8 days, the owner is alerted once
through every switched-on channel (Telegram, email), like a stalled sync (ADR-026). The next successful
check clears it. Whatever the alert says, read the `backup` service's log in Coolify, or the rows above.
The `Detail` of a failed row names the target and the tool's last error line. Common causes are an empty
`BACKUP_*` variable (the row names it), a rotated bucket key, a full bucket, and a check key that differs
from the one the backups were encrypted to. After changing `BACKUP_CHECK_AGE_IDENTITY`, run a backup by
hand before the next check, or it fails.

### Restoring from the app backup

Use this when the server is lost, or when no pre-migration dump fits (see "Rollback" for those).

1. Get the copy. Download the newest `daily/taxes_ua-<time>.dump.age` from MinIO, or from the off-VPS
   bucket if one is set up and the VPS is gone. Any S3 client works.
2. Decrypt it on the laptop with the recovery key from the password manager:

   ```bash
   age -d -i taxes-ua-recovery.key -o taxes_ua.dump taxes_ua-<time>.dump.age
   ```

3. On the new or existing instance, make sure the role from step 2 exists, then restore into a fresh
   `taxes_ua_restore` as `postgres`, as in "Rollback" step 3:

   ```bash
   P='docker exec -i 3p9qjnulllqn3bcjqokir0wq'
   ssh blonskyi "$P psql -U postgres -c 'DROP DATABASE IF EXISTS taxes_ua_restore' -c 'CREATE DATABASE taxes_ua_restore OWNER taxes_ua_app'"
   ssh blonskyi "$P pg_restore -U postgres --no-owner --role=taxes_ua_app --single-transaction --exit-on-error -d taxes_ua_restore" < taxes_ua.dump
   ```

4. Verify it and swap it in with "Rollback" steps 3 and 4. On a new server `taxes_ua` does not exist yet,
   so rename `taxes_ua_restore` to `taxes_ua` directly. Then deploy as in steps 5 to 7. The monobank token
   decrypts only with the same `MONOBANK_TOKEN_ENCRYPTION_KEY`, so take that from the password manager
   too. The owner signs in again, because the key ring volume was on the lost server.
5. Delete `taxes_ua.dump` and the key file from the laptop.

## 9. Checks that need the real domain

These tickets were built and tested locally, but their last criteria need HTTPS on the real
domain. Do them now and tick them on their issues:

- **#16 Passkey.** While signed in, open `/login` and add a passkey, sign out, and sign in with it
  on Android Chrome and desktop. iOS is not a target.
- **#18 PWA.** Install the app from Chrome on Android and confirm it opens in standalone mode.
- **#15 Prototype import.** Import a real export from the prototype, check the receipts and
  payments against it, then import the same file again and confirm the record count does not
  change.

## 10. Host firewall

Only Cloudflare reaches ports 80 and 443 on the VPS, and nothing else a container publishes reaches
the internet. Docker-published ports bypass UFW, so the rule lives in Docker's `DOCKER-USER` chain (and
in `mangle` `INPUT` for anything that lands on the host). `deploy/firewall/cloudflare-only.sh` installs
it, and a systemd unit and timer reapply it whenever Docker starts and refresh Cloudflare's ranges
weekly. `deploy/firewall/README.md` has the install, outside checks and rollback (#260).

Anything opened as `<vps-ip>:<port>` or through a DNS-only record stops answering from outside once it
is installed. Containers that must not be public publish on `127.0.0.1` or not at all.

**Check.** `sudo cloudflare-only.sh --check` on the VPS prints only `ok` lines. From outside,
`https://taxes.blonskyi.dev/` loads, and a request straight to `<vps-ip>` on 443 times out.

## Rollback

**A bad release without a migration.** Revert the offending commit on `main` and deploy. That is
the documented path for the Compose build pack: Coolify builds whatever `main` holds.

**A bad release with a migration.** Migrations run when `api` starts and only move forward, so the
old code may not run against the new schema. Before each migration `api` dumps this database alone
to the `migration-dumps` volume (ADR-027), so there is always a dump to go back to. The instance-wide
dumps from step 8a are no substitute: they restore every project on the instance at once. The nightly app
backup from step 8b restores this database alone. It is older than the pre-migration dump, so it loses
more, but it is there when the volume is not ("Restoring from the app backup").

Where the dumps are: the compose volume `migration-dumps`, mounted in `api` at
`/var/lib/taxes-ua/dumps`. The newest 10 are kept, named
`taxes_ua-pre-migrate-<UTC time>-from-<last applied migration>-to-<last pending migration>.dump`
(custom format, compressed). The volume is on the VPS disk, so copy the file off the VPS before a risky
restore.

If the dump itself fails, `api` logs `The pre-migration dump failed` at critical level, does not migrate and
exits. The database is untouched, the deploy fails, and CI's `Wait for this commit to report healthy` step
fails at its 15-minute timeout. Read the `api` log in Coolify, fix the cause (disk full, volume permissions,
`pg_dump` older than the server) and redeploy.

1. Find the dump taken before the bad release: the newest file whose name ends in the migration the release
   added. The volume's real name carries Coolify's prefix.

   ```bash
   V=$(ssh blonskyi "docker volume ls -q | grep migration-dumps")
   ssh blonskyi "docker run --rm -v $V:/d:ro alpine:3 ls -lt /d"
   ```

2. Stop the resource in Coolify, so nothing writes to the database while it is restored.
3. Restore that dump into a fresh database, as `postgres`, so a table the bad migration added cannot
   survive. Nothing here touches `taxes_ua` yet.

   ```bash
   P='docker exec -i <pg-container>'
   ssh blonskyi "$P psql -U postgres -c 'DROP DATABASE IF EXISTS taxes_ua_restore' -c 'CREATE DATABASE taxes_ua_restore OWNER taxes_ua_app'"
   ssh blonskyi "docker run --rm -v $V:/d:ro alpine:3 cat /d/<file>.dump" \
     | ssh blonskyi "$P pg_restore -U postgres --no-owner --role=taxes_ua_app --single-transaction --exit-on-error -d taxes_ua_restore"
   ```

   Verify it: the last row of `__EFMigrationsHistory` is the migration the dump name says it was taken
   *from*, and the row counts look right.

   ```bash
   ssh blonskyi "$P psql -U postgres -d taxes_ua_restore -Atc 'select \"MigrationId\" from \"__EFMigrationsHistory\" order by 1 desc limit 1'"
   ssh blonskyi "$P psql -U postgres -d taxes_ua_restore -Atc 'select count(*) from \"Receipts\"'"
   ```

4. Swap it in. **Everything written after the dump was taken is lost.** The resource is stopped, so
   nothing is connected; `WITH (FORCE)` also drops any stray session.

   ```bash
   ssh blonskyi "$P psql -U postgres -c 'ALTER DATABASE taxes_ua RENAME TO taxes_ua_bad' -c 'ALTER DATABASE taxes_ua_restore RENAME TO taxes_ua'"
   ```

   Keep `taxes_ua_bad` until the old release is confirmed healthy, then
   `DROP DATABASE taxes_ua_bad WITH (FORCE)`. If the rename fails because a session is still open, run
   `DROP DATABASE taxes_ua WITH (FORCE)` instead, then the second `ALTER`.

5. Revert the release on `main`, so the old code is what deploys. Merge the revert and let CI deploy it, or
   click Deploy in Coolify after the revert is on `main`. The old code starts against the restored schema and
   finds nothing pending to migrate.

If the migration failed halfway, nothing needs restoring unless a migration suppresses its transaction (none
does today): EF runs each migration in a transaction, so the database is still on the previous migration. Revert the release and deploy.

**Check.** Step 7's commands pass again.

**Abandoning the first deploy.** Stop and delete the Coolify resource, remove the DNS record,
then drop what step 2 created. This deletes the data:

```bash
ssh blonskyi 'docker exec <pg-container> psql -U postgres -c "DROP DATABASE taxes_ua" -c "DROP ROLE taxes_ua_app"'
```

**Ending every session.** A stolen session cookie cannot be revoked one by one (ADR-009). Rotate
the key ring instead: delete the `key-*.xml` files in the `dataprotection-keys` volume and restart
`api`. Every cookie, including the owner's, stops working at once.

## Automatic deploys

The `Deploy to Coolify` job runs only on a push to `main`, and only after every other CI job has
passed, so a red `main` never reaches the server. It sends a `POST` to the resource's **Deploy
Webhook (auth required)** with the API token as a bearer header. Coolify's documentation shows a
`GET`, but the route answers an authenticated `GET` with 405.

1. In Coolify, open the resource → Configuration → Webhooks and copy **Deploy Webhook (auth
   required)**. It looks like `https://<coolify>/api/v1/deploy?uuid=<uuid>&force=false`. The GitHub
   webhook URL on the same page is a different endpoint.
2. In Coolify, Keys & Tokens, create an API token with the deploy permission.
3. In GitHub, repository Settings → Secrets and variables → Actions, add `COOLIFY_WEBHOOK_URL`
   (step 1) and `COOLIFY_WEBHOOK_TOKEN` (step 2).

**Check.** After the next merge to `main`, the CI run shows `Deploy to Coolify` green and Coolify's
Deployments list a new deployment for that commit. The CI's `concurrency` group cancels an older
run on `main` when a newer push arrives, so only the latest commit deploys.

After the webhook, the job polls `https://taxes.blonskyi.dev/api/health` every 15 seconds for up to 15
minutes and passes only when it answers `status: ok` and `release` is the commit being deployed. The
`release` is Coolify's `SOURCE_COMMIT`, which Coolify injects into the services and `api` reads from its
environment (the compose file must not mention it); the old release also answers ok, so the commit is what tells them apart. If it never
appears the job fails: open the Coolify deployment log and the `api` log, then see "Rollback". The job
shares the `deploy-coolify` concurrency group, so two deploys never run at once and one in progress is
not cancelled. A run whose webhook builds a newer `main` than its own commit fails at the timeout even
though the newer release is up; check the newer run.

**Check.** `curl -s https://taxes.blonskyi.dev/api/health` shows the commit of the latest merge in
`release`. If it shows `unknown`, delete any user-defined `SOURCE_COMMIT` variable on the resource
(Environment Variables): an empty one stops Coolify from injecting the real commit.

### Keeping auto-merge branches current

`main` requires branches to be up to date, so a PR with auto-merge armed goes `BEHIND` each time
another PR merges. The `Update auto-merge branches` workflow runs on every push to `main` (and by
hand from the Actions tab) and calls `gh pr update-branch` on each open PR with auto-merge enabled
that is behind. A branch updated with the built-in `GITHUB_TOKEN` does not start CI, so the
workflow uses a personal token instead.

1. In GitHub, Settings → Developer settings → Personal access tokens → Fine-grained tokens, create
   a token for this repository only, with Repository permissions **Contents: Read and write** and
   **Pull requests: Read and write**, and an expiry you will remember to renew.
2. In the repository, Settings → Secrets and variables → Actions, add it as `AUTO_UPDATE_TOKEN`.

Without the secret the job logs a notice and passes, and nothing is updated. **Check.** Arm
auto-merge on two PRs, merge one, and the other shows a new merge commit from the update and a CI
run on it.

### No AI attribution

The `No AI attribution` workflow fails a PR whose own commits (`origin/main..head`), title or body
carry a `Co-Authored-By:` naming Claude, Anthropic or an AI, `Generated with [Claude Code]`, or
`noreply@anthropic.com`. Older history is not read. Run it locally with
`.github/scripts/check-no-ai-attribution.sh origin/main HEAD`. It is not in the required checks of
`main`'s branch protection; add `No AI attribution` there to make it block the merge.
