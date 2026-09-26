# 17: PWA and mobile polish

GitHub: #18
Status: ready-for-human (merged in PR #35; open for iOS and Android install checks, needs the deployment)
Blocked by: #3

## Parent

#1

## What to build

The app installs on iOS and Android as a PWA with an icon and its own name, respects safe-area, and every MVP screen is verified at 375px.

## Acceptance criteria

- [ ] Lighthouse marks the app installable.
- [ ] Installation on iOS and Android succeeds; the app opens in standalone mode.
- [ ] No screen scrolls horizontally at 375px.

## Blocked by

- #3 (Google sign-in and the interface shell)
