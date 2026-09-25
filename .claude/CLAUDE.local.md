# Local Project Instructions

## Project Context

Учёт налогов ФОП 3 группы для одного владельца. ТЗ: `/Users/mykola/Documents/obsidian-notes/tsxes-ua/SPEC.md`.
Общение с владельцем на русском. Интерфейс на украинском по умолчанию, русский вторым языком.

---

## Constraints

- Деньги только целые: `long` копейки/центы, `int RateE4`, проценты в базисных пунктах. Никаких float и decimal в движке.
- Даты операций `DateOnly` по Europe/Kyiv. Перевод из UTC только на границе API.
- `TaxesUa.Engine` без `PackageReference`, без `DateTime.Now`, БД и сети.
- Налоговые параметры только в `TaxYearConfig`. Не хардкодить ставки и даты в коде.
- Токены банков не логировать и не отдавать клиенту.
- Бесплатные и self-hosted решения. Никаких платных сервисов.

---

## Architecture Notes

- `api/` ASP.NET Core 10 Minimal APIs, EF Core + Npgsql, Identity (Google + passkey), cookie-сессия, allowlist email.
- `web/` Next.js только UI. `/api/*` проксируется на контейнер `api` через rewrites.
- Типы для web генерируются из OpenAPI (`openapi-typescript`), руками не дублировать.
- Cron внутри `api` как hosted services.
- Деплой: Coolify Docker Compose resource на VPS `blonskyi-dev` (ssh `blonskyi`), Postgres как Coolify resource.

---

## Coding Conventions

- C#: records для входа/выхода движка, `DateOnly`, nullable включён, warnings как ошибки в Engine.
- TS: TanStack Query/Table/Form, shadcn/ui, Tailwind. Zustand только при реальной нужде.
- Тесты движка: xUnit, один файл на сценарий, эталон 2026 как таблица.
- Комментарии только для неочевидного «почему».

---

## Deployment Notes

- VPS: Ubuntu 24.04, 4 vCPU, 7.7 GB RAM, Docker 29, Coolify + Traefik v3, MinIO на хосте.
- Секреты в переменных окружения Coolify. `.env.example` без значений.
- Бэкапы БД: Coolify scheduled backups в MinIO и внешний S3-совместимый бесплатный бакет.

---

## Known Limitations

- ФОП владельца ещё не зарегистрирован. Реальных выписок нет.
- Трактовка срока уплаты ЕП/ВЗ при переносе декларации и ЕСВ в месяц регистрации не подтверждены, настраиваемы.
