# 02: Connect Telegram for reminders

GitHub: #106
Status: ready-for-agent
Blocked by: none
Parent: #104

## What to build

A Telegram channel. With TELEGRAM_BOT_TOKEN configured, settings offers "Connect Telegram": a deep link `https://t.me/<bot>?start=<one-time code>`. A hosted service long-polls getUpdates, matches the code on /start, stores the chat id and replies with a confirmation; anything else gets a short reply. The owner can switch the channel on and off, send a test message, and disconnect. Delivery failures retry 3 times with backoff and then show on the channel; a 403 (bot blocked) disables it with a message. Without the token the channel shows as unavailable and nothing polls.

## Acceptance criteria

- [ ] Fake Telegram (HTTP handler stub) tests: link code flow, expired or reused code rejected, test message, retries, 403 disables, polling offset persists across restarts.
- [ ] Token only in configuration and never logged; docs/deploy.md documents creating the bot with BotFather and the variable.
- [ ] ADR: long polling instead of a webhook.
- [ ] Settings proved in a real browser at 375 px in uk and ru; linked once to a real test bot by the owner.
