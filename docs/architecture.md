# Архитектура

## Обзор

Личное веб‑приложение для учёта доходов и налогов ФОП 3 группы (единый налог 5% без НДС).
Ведёт поступления в валюте с пересчётом по курсу НБУ, считает ЕП, ВЗ и ЕСВ, показывает сроки,
ведёт реестр платежей в бюджет с балансом по каждому виду и готовит цифры для декларации.

Приложение не платит налоги и не подаёт декларации. Платежи делает пользователь в банке,
декларацию подписывает КЭП в Электронном кабинете.

Полное ТЗ: `/Users/mykola/Documents/obsidian-notes/tsxes-ua/SPEC.md` (вне репозитория).

---

## Цели

- Точный расчёт по параметрам года без хардкода. Смена года не требует изменения кода.
- Главный экран отвечает на один вопрос: что сделать следующим, до какого числа, сколько.
- Один пользователь сейчас. Модель данных с `UserId`, чтобы позже открыть продукт другим ФОП.
- Бесплатный self‑hosted хостинг на существующем VPS. Никаких платных сервисов.

---

## Стек

| Слой | Выбор | Почему |
| --- | --- | --- |
| Backend | ASP.NET Core 10 (LTS), Minimal APIs, C# | Решение владельца ([ADR-001](decisions.md)). Строгая типизация, hosted services для cron, встроенный OpenAPI. |
| ORM | EF Core 10 + Npgsql, миграции | Стандарт платформы. |
| Auth | ASP.NET Core Identity + Google OAuth + passkey (встроено в Identity .NET 10), cookie‑сессия, allowlist email | Бесплатно и без вендора. 2FA обеспечивает Google‑аккаунт. |
| Налоговый движок | `TaxesUa.Engine`, class library без пакетов, xUnit | Тестируется без БД и UI. |
| Frontend | Next.js (App Router), TypeScript | Предпочтение владельца. Только UI, серверного кода нет. |
| Клиент | TanStack Query, Table, Form; типы из OpenAPI через `openapi-typescript` | Один источник типов, контракт API не дублируется руками. |
| UI | Tailwind CSS + shadcn/ui, next-intl (ru, позже uk), PWA через Serwist | Адаптив, тёмная тема, установка на телефон. |
| Состояние | Zustand только при реальной нужде | В MVP глобального клиентского состояния нет. |
| БД | PostgreSQL 16+ | Предпочтение владельца. Уже есть в Coolify. |
| Деплой | Coolify на VPS `blonskyi-dev`, Docker Compose из репозитория | Traefik с авто‑TLS, Postgres resource, бэкапы. |

---

## Компоненты

### api (ASP.NET Core)

Responsibilities:

- REST API: транзакции, платежи, настройки, параметры лет, периоды, обязательства, экспорт, бэкап.
- Аутентификация и сессии. Все запросы к данным фильтруются по `UserId`.
- Адаптеры границы: парсинг импорта, дата по Europe/Kyiv, курс НБУ, перевод в копейки, валидация.
- Фоновые задачи как `IHostedService`: напоминания, очередь синхронизации банков (Этап 2).
- Журнал изменений.

Dependencies: `TaxesUa.Engine`, PostgreSQL, NBU API, позже Telegram Bot API, SMTP, API банков.

### TaxesUa.Engine (class library)

Responsibilities:

- Доход по периодам с учётом возвратов.
- Начисления ЕП, ВЗ, ЕСВ по кварталам и месяцам, нарастающий итог для декларации.
- Сроки с переносом с выходных по настраиваемым правилам.
- Балансы по видам платежей, переплаты и остатки, рекомендуемые авансы.
- Контроль лимита дохода с порогами 85% и 100% и ставкой превышения.
- Предупреждения: операции до даты регистрации, год без проверенных параметров.

Dependencies: нет. Вход только простые данные: `DateOnly`, `long` копейки, перечисления, records.

### web (Next.js)

Responsibilities: экраны, формы, PWA, темы, i18n. Ходит в API через `/api/*`, который Next.js
проксирует на контейнер `api` (rewrites). Для браузера это один origin, cookie‑сессия работает
без CORS.

Dependencies: `api`.

### PostgreSQL

Хранит сущности из [knowledge/domain-model.md](../knowledge/domain-model.md). Деньги в `bigint`
копейках, курсы в целых с масштабом 10⁴, даты операций как `date` по Киеву.

### Интеграции

External systems:

- НБУ: `https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&date=YYYYMMDD&json`.
  На выходные отдаёт `[]`. Адаптер откатывается к последнему рабочему дню и сохраняет
  фактическую дату курса. Ответы кэшируются в таблице `fx_rate`.
- monobank personal API, ПриватБанк Автоклиент (Этап 2). Токены шифруются AES‑256‑GCM ключом из env.
- Telegram Bot API и SMTP для напоминаний (Этап 2).
- Схема XML декларации ДПС F0103309 (Этап 3).

---

## Поток данных

```
браузер (Next.js UI)
   │  /api/*  (same origin, rewrite → http://api:8080)
   ▼
api: граница
   UTC → дата Europe/Kyiv, сумма → копейки, курс НБУ → RateE4, валидация
   │
   ▼
PostgreSQL (transaction, budget_payment, settings, tax_year_config, …)
   │
   ▼
TaxesUa.Engine (чистые функции: obligations, balances, periods, limit)
   │
   ▼
JSON ответы API → экраны, экспорт, напоминания
```

Движок пересчитывает всё при каждом запросе. Объём данных одного ФОП это сотни строк в год,
кэш не нужен.

---

## Структура репозитория

```
api/                      .NET solution
  TaxesUa.sln
  src/TaxesUa.Engine/     движок, без пакетов
  src/TaxesUa.Api/        ASP.NET Core
  tests/TaxesUa.Engine.Tests/
  tests/TaxesUa.Api.Tests/
  Dockerfile
web/                      Next.js
  Dockerfile
docker-compose.yml        для Coolify
docs/ knowledge/ plans/   документация
```

---

## Деплой

- Хост: VPS `blonskyi-dev`, Ubuntu 24.04, 4 vCPU, 7.7 GB RAM, Docker 29, Coolify с Traefik v3.
- Приложение: Coolify Docker Compose resource из GitHub‑репозитория, сервисы `web` и `api`.
  Домен привязан к `web`. `api` наружу не публикуется.
- БД: Coolify PostgreSQL resource. Бэкапы: встроенные scheduled backups Coolify в MinIO на том же
  хосте плюс внешний S3‑совместимый бесплатный бакет (Cloudflare R2 или Backblaze B2).
- Cron: hosted services внутри `api`. Внешний планировщик не нужен.
- Секреты: переменные окружения Coolify. `.env.example` в репозитории без значений.
- Стоимость: 0.

---

## Безопасность

Authentication: ASP.NET Core Identity, внешний вход Google, passkey как второй способ.
Вход разрешён только email из `Auth__AllowedEmails`. Cookie `HttpOnly; Secure; SameSite=Lax`.

Authorization: каждая выборка и запись фильтруется по `UserId` из сессии.

Secrets Management: ключ шифрования токенов банков только в env. Токены расшифровываются в момент
вызова API банка, не попадают в логи, ответы и клиент.

Прочее: HTTPS через Traefik. Антифорджери для cookie‑auth через заголовок `X-Requested-With`
и SameSite. Журнал изменений `audit_log`. Дисклеймер в интерфейсе: расчёт справочный.

---

## Наблюдаемость

Logging: структурные логи ASP.NET Core в stdout, читаются через Coolify. Суммы логируются,
токены и email нет.

Metrics: не нужны для одного пользователя. `/api/health` с проверкой БД для мониторинга Coolify.

Tracing: нет.
