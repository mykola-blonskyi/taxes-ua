# Future Work

## High Priority

Stage 2. Automation (roughly 6–8 days).

- [ ] Import monobank CSV/XLSX statements. Dedupe by transaction ID.
- [ ] Auto-classification: income, own transfer, currency sale. Manual confirmation.
- [ ] monobank personal API: encrypted token, a request queue limited to 1 request per 60
      seconds, statements in 31-day windows. FOP accounts are visible (confirmed 2026-09-28).
      Read only, no payment creation. #71.
- [ ] Reminders: a Telegram bot and email, each toggleable independently. At 7 days, 1 day, and
      on the deadline itself.
- [ ] Export deadlines to .ics.

---

## Medium Priority

Stage 3. Documents (roughly 5–7 days).

- [ ] Clients with details.
- [ ] English-language PDF invoice: IBAN, SWIFT, numbering. Link to a receipt, "paid" status.
- [ ] F0103309 XML declaration per the current DPS schema, for import into the Electronic
      Cabinet.
- [ ] Yearly archive, a reminder to keep documents for 3 years.

---

## Low Priority

- [ ] PrivatBank: CSV/XLSX import and the Autoclient API. Deferred until the owner opens a FOP
      account there (decided 2026-09-28).
- [ ] Multi-user mode: remove the allowlist, self-registration, data isolation.
- [ ] PWA offline mode.
- [ ] Sentry free tier.
