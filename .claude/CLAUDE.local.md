# Local Project Instructions

## Project Context

Tax tracker for a single owner's Group 3 FOP. Spec:
`/Users/mykola/Documents/obsidian-notes/tsxes-ua/SPEC.md`. Chat with the owner is in Russian. All
repository documents (docs/, knowledge/, plans/, READMEs, CLAUDE.md files) are written in
English. The interface is Ukrainian by default, with Russian as a second language.

---

## Constraints

- Money only as integers: `long` kopecks/cents, `int RateE4`, percentages in basis points. No
  float and no decimal in the engine.
- Operation dates are `DateOnly` by Europe/Kyiv. UTC conversion happens only at the API boundary.
- `TaxesUa.Engine` has no `PackageReference`, no `DateTime.Now`, no database, no network.
- Tax parameters live only in `TaxYearConfig`. Never hardcode rates or dates in code.
- Bank tokens are never logged and never returned to the client.
- Free and self-hosted solutions only. No paid services.

---

## Architecture Notes

- `api/` ASP.NET Core 10 Minimal APIs, EF Core + Npgsql, Identity (Google + passkey), cookie
  session, email allowlist.
- `web/` Next.js, UI only. `/api/*` is proxied to the `api` container via rewrites.
- Web types are generated from OpenAPI (`openapi-typescript`), never hand-duplicated.
- Cron runs inside `api` as hosted services.
- Deploy: a Coolify Docker Compose resource on the `blonskyi-dev` VPS (ssh `blonskyi`). The
  database reuses the PostgreSQL instance already running on that VPS — a dedicated role and
  database are created for this project, not a new Coolify PostgreSQL resource.

---

## Coding Conventions

- C#: records for the engine's input/output, `DateOnly`, nullable enabled, warnings as errors in
  Engine.
- TS: TanStack Query/Table/Form, shadcn/ui, Tailwind. Zustand only when actually needed.
- Web layers: `app` → `features` → `data` → `shared`. Features are reachable from outside only
  through `index.ts` and never import each other. Boundaries are enforced by eslint.
- .NET: package versions live only in `api/Directory.Packages.props`; csproj files carry no
  `Version`.
- Engine tests: xUnit, one file per scenario, the 2026 reference table as a data table.
- Comments only for a non-obvious "why".

---

## Deployment Notes

- VPS: Ubuntu 24.04, 4 vCPU, 7.7 GB RAM, Docker 29, Coolify + Traefik v3, MinIO on the host.
- Secrets in Coolify environment variables. `.env.example` carries no values.
- Database: an existing PostgreSQL instance on the VPS, reused via a new role/database for this
  project. Backups depend on whatever already covers that instance.

---

## Known Limitations

- The owner's FOP is not registered yet. There is no real bank data.
- The EP/VZ payment-deadline interpretation and the ESV registration-month policy are not
  confirmed yet; both are configurable.
