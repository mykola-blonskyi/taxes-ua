# taxes-ua

A personal application for tracking income and taxes of a Group 3 FOP. Documentation lives in
`docs/`, `knowledge/`, `plans/`.

## Structure

- `api/` ASP.NET Core 10: `TaxesUa.Engine` (the tax engine, no dependencies), `TaxesUa.Api`,
  tests.
- `web/` Next.js, UI only. `/api/*` is proxied to the backend.
- `docker-compose.yml` for Coolify; `docker-compose.local.yml` adds Postgres and ports for a
  local run.

## Local run

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```

The UI is at http://localhost:3000, the health check at http://localhost:3000/api/health.

Without Docker: start Postgres on `localhost:5432` with the credentials from
`api/src/TaxesUa.Api/appsettings.Development.json`, then run `dotnet run --project
api/src/TaxesUa.Api` and `pnpm --dir web dev`.

## Tests

```bash
dotnet test api
```
