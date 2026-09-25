# 02: Вход через Google и каркас интерфейса

GitHub: #3
Status: ready-for-agent
Blocked by: none

## Parent

#1

## What to build

Владелец входит своим Google‑аккаунтом, чужой email получает отказ. После входа виден пустой главный экран с навигацией для телефона и десктопа, светлой и тёмной темой, переключателем языка (украинский по умолчанию, русский вторым) и дисклеймером о справочном расчёте. Backend: ASP.NET Core Identity схема v3, Google OAuth, cookie за Traefik, allowlist из конфигурации, /auth/me и выход. Frontend: shadcn/ui, Tailwind, next-intl с uk и ru, TanStack Query, генерация типов из OpenAPI, редирект на страницу входа при 401.

## Acceptance criteria

- [ ] Вход с email из allowlist даёт сессию, `/api/auth/me` возвращает пользователя; другой email получает 403 и понятное сообщение.
- [ ] Cookie `HttpOnly; Secure; SameSite=Lax`, секреты Google только в переменных окружения.
- [ ] Тема переключается вручную и следует системной по умолчанию; язык переключается uk/ru, все видимые строки из файлов переводов.
- [ ] Страницы‑заглушки всех экранов MVP открываются с телефона на 375 px без горизонтальной прокрутки.
- [ ] Тест API: неавторизованный запрос к защищённому ресурсу даёт 401, email вне allowlist 403.

## Blocked by

- None (can start immediately)
