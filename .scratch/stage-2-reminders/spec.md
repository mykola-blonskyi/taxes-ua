GitHub: #104
Status: ready-for-agent

# Spec: Deadline reminders in Telegram and email, and a calendar feed

## Problem Statement

The app knows every deadline and how much is owed, but only when the owner opens it. A missed ESV, single tax or declaration deadline costs a fine and late-payment interest, and the dates move with weekends and holidays. The owner wants to be told in time, where they already look (Telegram, email, their calendar), with the amount and a way straight to paying.

## Solution

The owner connects a Telegram bot chat and/or an email address in settings. The app sends a reminder 7 days before, 1 day before and on the day of each deadline that still has something to do (a payment still owed, a declaration not yet marked filed), and once the day after if it became overdue. Each message names the kind, the period, the amount owed right now and the date, and links to the app. The owner can also subscribe their calendar to a private feed of all deadlines.

## User Stories

Channels

1. As a FOP, I want to connect Telegram by opening a link to the bot and pressing Start, so that I never copy a chat id.
2. As a FOP, I want to add an email address and confirm it by a link, so that reminders never go to a typo.
3. As a FOP, I want each channel switched on and off independently, so that I choose where I am reminded.
4. As a FOP, I want a "send a test message" button per channel, so that I know it works before a deadline.
5. As a FOP, I want to disconnect a channel, so that it stops at once.
6. As a FOP, I want the settings to show the last delivery and any failure per channel, so that a broken channel does not fail silently.
7. As a FOP, I want the channels simply unavailable (not broken) when the server has no bot token or SMTP settings, so that a local stack runs without them.

What I am reminded of

8. As a FOP, I want reminders for the ESV deadline, the single tax and military levy payment deadline, and the declaration deadline of each quarter, so that nothing is missed.
9. As a FOP paying monthly advances, I want reminders for each advance's recommended date, so that the advance mode is useful.
10. As a FOP, I want reminders at 7 days, 1 day and on the day, so that I have time and a last call.
11. As a FOP, I want one reminder the day after a deadline if something is still owed, so that an overdue debt is flagged once, not nagged daily.
12. As a FOP, I want no payment reminder when the obligation is already paid, so that I trust every message.
13. As a FOP, I want no declaration reminder once I mark the quarter's declaration filed, so that the reminder stops.
14. As a FOP, I want kinds that share a date (single tax and military levy) in one message, so that I get one message per date.
15. As a FOP, I want the amount in the message to be what is owed at the moment of sending, after payments, so that it matches the home screen.
16. As a FOP, I want reminders sent at 09:00 Kyiv time, so that they arrive at a sensible hour.
17. As a FOP, I want the dates shifted for weekends and holidays exactly as the app shows them, so that the reminder and the screen agree.
18. As a FOP, I want a link in each message to the app's pay panel for that kind (once it exists) or the home screen, so that paying is one tap away.
19. As a FOP, I want messages in my interface language (Ukrainian or Russian), so that they read naturally.

Reliability

20. As a FOP, I want each reminder sent exactly once per channel, even across restarts and redeploys, so that I am not spammed.
21. As a FOP, I want a reminder missed because the server was down sent when it comes back, if the deadline has not passed, so that downtime does not cost me a reminder.
22. As a FOP, I want a failed delivery retried a few times and then shown in settings, so that a transient error is absorbed.

Calendar

23. As a FOP, I want a private calendar feed URL with all upcoming deadlines, so that my phone calendar shows them and updates by itself.
24. As a FOP, I want the feed to show kinds and dates but no amounts, so that a leaked link reveals nothing financial.
25. As a FOP, I want to regenerate the feed URL, so that I can revoke an old one.
26. As a FOP, I want a one-off .ics download too, so that I can import it anywhere.

Data

27. As a FOP, I want channel settings in the backup without secrets (no confirmation codes, no feed secret), so that a restore does not leak them.
28. As a future second user, I want my channels, sent log and feed isolated by owner.

## Implementation Decisions

- Modules: notification channels (Telegram, email) with a delivery adapter each; a reminder planner (pure: from the engine's deadlines and balances, the list of reminders due at a moment); a hosted reminder worker; the calendar feed; settings UI.
- Reminders are computed, not pre-scheduled. The planner takes the owner's settings, the deadline calendar, the allocated balances and the declaration-filed marks, and returns the reminders due now, each with a stable key (owner, obligation date and kinds, offset, channel). A sent log keyed by that stable key makes delivery idempotent; the worker runs every few minutes, sends what is due and not logged, and writes the log in the same transaction as it marks the attempt. Offsets: 7, 1, 0 days and +1 day overdue. Send time 09:00 Kyiv; a reminder whose moment passed while the server was down is sent late if its deadline has not passed.
- Telegram: the owner creates a bot with BotFather; the token is configuration (TELEGRAM_BOT_TOKEN). Linking uses a deep link `https://t.me/<bot>?start=<one-time code>`; the app reads updates by long polling getUpdates in a hosted service (no public webhook needed), matches the code, and stores the chat id. Only /start with a valid code is acted on; everything else gets a short reply.
- Email: SMTP settings are configuration (host, port, TLS, user, password, from). The address is confirmed through a signed, expiring link. Plain-text plus simple HTML messages.
- Both channels: 3 retries with backoff, then a visible failure on the channel; a 403 from Telegram (bot blocked) disables the channel with a message.
- The declaration-filed mark per quarter comes from the declaration spec; until it exists, declaration reminders are sent for every quarter until its deadline passes.
- The calendar feed is an iCalendar document at a per-owner secret path (256-bit secret, rotatable), one all-day event per deadline for the current and next year with alarms at 7 and 1 days, kinds and periods but no amounts. The same document is downloadable once.
- Messages use the owner's locale from settings; amounts formatted as on screen.
- API: channels list, connect Telegram (returns the deep link), email add, confirm and remove, toggles, test send; feed URL get and rotate; .ics download. Types generated from OpenAPI.
- Configuration and deploy docs: the new variables, all optional; ADR for computed reminders with an idempotent sent log and for long polling over a webhook.
- Docs: domain model (NotificationChannel updated, Reminder replaced by the sent log), a reminders rule in business-rules.md.

## Testing Decisions

- Good tests check what the owner receives (message text, time, channel) and what settings show, not internals.
- The planner is a pure function with table tests across quarters, weekends and holidays, monthly advances, paid and partially paid obligations, filed declarations, shared dates, and the overdue day.
- The worker and channels are tested at the HTTP seam with fake time and fake Telegram and SMTP transports registered like the NBU and monobank clients (HTTP handler stub for Telegram, an in-memory SMTP sender). No new seam style.
- Idempotency: restart mid-send and a duplicate worker tick send nothing twice; downtime catch-up.
- The feed is parsed back with an iCalendar parser in tests; the secret path is 404 for a wrong secret.
- UI proved in a real browser at 375 px in uk and ru; Telegram linking proved once against a real test bot by the owner.

## Out of Scope

- Push notifications, SMS, other messengers.
- Reminders for things other than tax deadlines and advances (invoices overdue come with the invoice tickets if wanted).
- Two-way bot commands beyond /start.
- Paid email services; the owner's own SMTP account is used.

## Further Notes

- The domain model already sketches NotificationChannel and Reminder; this spec replaces pre-scheduled Reminder rows with a computed plan plus a sent log, because payments change what is owed between scheduling and sending.
- Deadlines, shifting and amounts come from the engine unchanged (Rules 5 to 7).
