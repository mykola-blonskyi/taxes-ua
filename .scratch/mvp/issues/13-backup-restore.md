# 13: JSON backup and restore

GitHub: #14
Status: closed, done (PR #50). Its own verification found the two 500s fixed by #53 (PR #58).
Blocked by: #9

## Parent

#1

## What to build

The owner downloads a full backup of all their data as JSON and restores it with a confirmation, ending up with an identical state. Restoring replaces the user's data inside a single database transaction.

## Acceptance criteria

- [ ] API test: backup, wipe, restore yields identical data across every MVP entity.
- [ ] Restoring an invalid file changes nothing and returns 400.
- [ ] The screen requires an explicit confirmation before replacing data.

## Blocked by

- #9 (Budget payments and balances)
