# 17: PWA and mobile polish

GitHub: #18
Status: ready-for-human (merged in PR #35; open for iOS and Android install checks, needs the deployment)
Blocked by: #3

## Parent

#1

## What to build

The app installs on iOS and Android as a PWA with an icon and its own name, respects safe-area, and every MVP screen is verified at 375px.

## Acceptance criteria

- [x] Lighthouse marks the app installable. Verified 2026-09-28 by installing from Chrome on Android, which applies the same installability check.
- [x] Installation on Android succeeds; the app opens in standalone mode, without the address bar (owner, Galaxy S24 Ultra, 2026-09-28).
- ~~Installation on iOS~~: dropped by the owner on 2026-09-28, iOS is not a target.
- [ ] No screen scrolls horizontally at 375px. Settings → Tax years overflowed on the phone; fixed in #67, pending a Redeploy and a check on the device.

## Blocked by

- #3 (Google sign-in and the interface shell)
