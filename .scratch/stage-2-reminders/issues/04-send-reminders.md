# 04: Send deadline reminders to connected channels

GitHub: #108
Status: ready-for-agent
Blocked by: #106
Parent: #104

## What to build

Reminders are sent. A pure planner computes, for a moment in time, the reminders due for an owner from the deadline calendar, the allocated balances and settings: each deadline with something to do at 7 days, 1 day, the day itself (09:00 Kyiv) and once the day after if still owed; kinds sharing a date in one message; no payment reminder when nothing is owed; the amount owed at sending time; monthly advances in that mode. A hosted worker runs every few minutes, sends what is due to every enabled channel, and records each in a sent log keyed by owner, date, kinds, offset and channel, so nothing is sent twice across restarts, and a reminder missed during downtime is sent late if its deadline has not passed. Messages use the owner's locale and link to the app (the pay panel for that kind once #99 exists). Declaration reminders go until the deadline passes; they stop earlier once the declaration-filed mark from the declaration spec exists.

## Acceptance criteria

- [ ] Planner table tests: quarters, weekend and holiday shifts, advances, paid and partly paid, shared dates, overdue day, locale.
- [ ] Worker tests with fake time and fake channels: exactly once per channel, restart mid-send, duplicate tick, downtime catch-up, disabled channel skipped.
- [ ] Domain model and a reminders rule updated; the old pre-scheduled Reminder sketch replaced.
- [ ] One real reminder received in Telegram by the owner on a test date (fake clock in a local stack is fine).
