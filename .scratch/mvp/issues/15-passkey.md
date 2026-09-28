# 15: Passkey

GitHub: #16
Status: ready-for-human (merged in PR #36; open for iOS and Android device checks, needs the deployment)
Blocked by: #3

## Parent

#1

## What to build

After signing in via Google, the owner adds a passkey in their profile, signs out, and signs back in with it alone from their phone. Uses ASP.NET Core Identity's built-in .NET 10 support and the standard browser `navigator.credentials`.

## Acceptance criteria

- [ ] Passkey registration and sign-in work on Android Chrome and desktop. iOS dropped by the owner on 2026-09-28.
- [x] `IdentityPasskeyOptions.ServerDomain` matches the deployment domain. On 2026-09-28 `POST https://taxes.blonskyi.dev/api/auth/passkey/login/options` returned `rpId: taxes.blonskyi.dev`.
- [x] API test rejecting an invalid attestation (`PasskeyTests.An_invalid_attestation_is_rejected`).

## Blocked by

- #3 (Google sign-in and the interface shell)
