# 15: Passkey

GitHub: #16
Status: ready-for-agent
Blocked by: #3

## Parent

#1

## What to build

After signing in via Google, the owner adds a passkey in their profile, signs out, and signs back in with it alone from their phone. Uses ASP.NET Core Identity's built-in .NET 10 support and the standard browser `navigator.credentials`.

## Acceptance criteria

- [ ] Passkey registration and sign-in work on iOS Safari, Android Chrome and desktop.
- [ ] `IdentityPasskeyOptions.ServerDomain` matches the deployment domain.
- [ ] API test rejecting an invalid attestation.

## Blocked by

- #3 (Google sign-in and the interface shell)
