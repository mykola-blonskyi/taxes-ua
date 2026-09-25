# 01: Деплой скелета в Coolify и CI

GitHub: #2
Status: ready-for-agent
Blocked by: none

## Parent

#1

## What to build

Скелет из ветки feat/scaffold работает на реальном домене, а каждый следующий тикет можно проверить на нём. В Coolify созданы PostgreSQL resource и Docker Compose resource из репозитория, домен привязан к сервису web, задан DATABASE_URL, включены scheduled backups БД. GitHub Actions на push и PR гоняет тесты .NET, lint и сборку web и сборку образов.

## Acceptance criteria

- [ ] `https://<домен>/api/health` отвечает `{"status":"ok","database":true}`, главная страница открывается.
- [ ] Сервис `api` наружу не опубликован.
- [ ] В Coolify есть расписание бэкапов БД и хотя бы один успешный бэкап.
- [ ] Workflow CI зелёный на ветке, падает при сломанном тесте движка.

## Blocked by

- None (can start immediately)
