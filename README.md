# taxes-ua

Личное приложение для учёта доходов и налогов ФОП 3 группы. Документация в `docs/`, `knowledge/`, `plans/`.

## Структура

- `api/` ASP.NET Core 10: `TaxesUa.Engine` (налоговый движок без зависимостей), `TaxesUa.Api`, тесты.
- `web/` Next.js, только интерфейс. `/api/*` проксируется на backend.
- `docker-compose.yml` для Coolify, `docker-compose.local.yml` добавляет Postgres и порты для локального запуска.

## Локальный запуск

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```

Интерфейс на http://localhost:3000, проверка здоровья на http://localhost:3000/api/health.

Без Docker: поднять Postgres на `localhost:5432` с данными из `api/src/TaxesUa.Api/appsettings.Development.json`,
затем `dotnet run --project api/src/TaxesUa.Api` и `pnpm --dir web dev`.

## Тесты

```bash
dotnet test api
```
