# 16: Change log

GitHub: #17
Status: ready-for-agent
Blocked by: #9

## Parent

#1

## What to build

Every create, edit and delete of a transaction, payment, settings entry and year config is recorded with before/after snapshots. A record has a "history" panel where the owner sees what and when they changed.

## Acceptance criteria

- [ ] Creating, editing and deleting a transaction yields three log entries with correct snapshots.
- [ ] Changing settings and year parameters is also logged.
- [ ] The history panel is readable on a phone.
- [ ] API test for the log's content after a create-edit-delete scenario.

## Blocked by

- #9 (Budget payments and balances)
