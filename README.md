# taxes-ua

A personal application for tracking income and taxes of a Group 3 FOP. Documentation lives in
`docs/`, `knowledge/`, `plans/`.

## Structure

- `api/` ASP.NET Core 10: `TaxesUa.Engine` (the tax engine, no dependencies), `TaxesUa.Api`,
  tests.
- `web/` Next.js, UI only. `/api/*` is proxied to the backend.
- `docker-compose.yml` for Coolify; `docker-compose.local.yml` adds Postgres and ports for a
  local run. Deploying is manual and follows `docs/deploy.md`.

The app has three parts: a PostgreSQL database, the api (talks to the database), and the web UI
(talks to the api). You can run them all in Docker with one command, or run each one yourself.

## Option 1: run everything in Docker

You need Docker. Nothing else.

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```

Open http://localhost:3000. The health check is at http://localhost:3000/api/health and should
answer `{"status":"ok","database":true}`.

Docker starts its own Postgres with its own data. It does not use a Postgres installed on your
machine, and it does not publish port 5432, so the two never clash.

Stop with `Ctrl+C`. To also wipe the Docker database, run
`docker compose -f docker-compose.yml -f docker-compose.local.yml down -v`.

## Option 2: run without Docker

Faster to restart while coding. You need:

- PostgreSQL 16 on `localhost:5432` (`brew install postgresql@16 && brew services start postgresql@16`)
- .NET SDK 10
- Node.js 20+ and pnpm (`corepack enable`)

### 1. Create the database (once)

The api expects a user `taxes_ua` with password `taxes_ua` and a database `taxes_ua`. These
values come from `api/src/TaxesUa.Api/appsettings.Development.json`.

```bash
psql -h localhost -d postgres -c "CREATE ROLE taxes_ua LOGIN PASSWORD 'taxes_ua';" -c "CREATE DATABASE taxes_ua OWNER taxes_ua;"
```

Check that it works:

```bash
PGPASSWORD=taxes_ua psql -h localhost -U taxes_ua -d taxes_ua -c "select 1"
```

### 2. Create the tables (migrations)

The api applies all pending migrations by itself every time it starts, so you can skip this step.
To apply them without starting the api:

```bash
cd api
dotnet tool restore
dotnet ef database update --project src/TaxesUa.Api
```

### 3. Start the api

```bash
Auth__AllowedEmails=you@example.com dotnet run --project api/src/TaxesUa.Api
```

It listens on http://localhost:5241. `Auth__AllowedEmails` is the list of emails allowed to sign
in (comma-separated). The `.env` file is only read by Docker, so outside Docker you pass it like
this. Without it nobody can sign in.

### 4. Start the web UI

In a second terminal:

```bash
pnpm --dir web install
API_URL=http://localhost:5241 pnpm --dir web dev
```

Open http://localhost:3000. `API_URL` is required: by default the UI looks for the api on port
8080 (the Docker port), not 5241.

## Signing in locally

A local run has no Google login. Instead, in Development there is a shortcut that signs in any
email from the allowed list. Open this in the browser (use your port and email):

```
http://localhost:3000/api/auth/login/development?email=you@example.com&returnUrl=/
```

An email outside the allowed list gets 403, the same as with Google. The shortcut does not exist
in Production.

## Changing the database schema

Edit the entities and `AppDbContext`, then create a migration from the `api/` folder:

```bash
dotnet ef migrations add <Name> --project src/TaxesUa.Api --output-dir Data/Migrations
```

Restart the api (or run `dotnet ef database update --project src/TaxesUa.Api`) to apply it.
Commit the generated files in `api/src/TaxesUa.Api/Data/Migrations/`.

To start over with an empty local database:

```bash
psql -h localhost -d postgres -c "DROP DATABASE taxes_ua;" -c "CREATE DATABASE taxes_ua OWNER taxes_ua;"
```

## Regenerating API types for the web

After changing api endpoints, with the api running on port 5241:

```bash
pnpm --dir web gen:api
```

## Tests

```bash
dotnet test api
```

## Troubleshooting

- `/api/health` says `"database":false`, or the api fails on start: Postgres is not running, or
  the database/user from step 1 do not exist.
- The UI loads but every request fails: you forgot `API_URL=http://localhost:5241`.
- Sign-in returns 403: the email is not in `Auth__AllowedEmails` (or `ALLOWED_EMAILS` in `.env`
  for Docker).
- `/api/auth/login/google` returns 503: Google keys are not set. Use the local sign-in above.
- Port 3000 is busy: `pnpm --dir web dev --port 3001`.
