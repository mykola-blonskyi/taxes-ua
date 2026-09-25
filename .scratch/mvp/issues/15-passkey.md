# 15: Passkey

GitHub: #16
Status: ready-for-agent
Blocked by: #3

## Parent

#1

## What to build

После входа через Google владелец добавляет passkey в профиле, выходит и входит только им с телефона. Используются встроенные средства ASP.NET Core Identity .NET 10 и стандартный `navigator.credentials` в браузере.

## Acceptance criteria

- [ ] Регистрация passkey и вход по нему работают на iOS Safari, Android Chrome и десктопе.
- [ ] `IdentityPasskeyOptions.ServerDomain` совпадает с доменом деплоя.
- [ ] Тест API на отклонение невалидной attestation.

## Blocked by

- #3 (Вход через Google и каркас интерфейса)
