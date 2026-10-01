# Future Work

## High Priority

Stage 2. Automation (roughly 6–8 days).

- [ ] Import monobank CSV/XLSX statements. Dedupe by transaction ID.
- [ ] Auto-classification: income, own transfer, currency sale. Manual confirmation.
- [ ] monobank personal API: encrypted token, a request queue limited to 1 request per 60
      seconds, statements in 31-day windows. FOP accounts are visible (confirmed 2026-09-28).
      Read only, no payment creation. #71.
- [ ] Reminders: a Telegram bot and email, each toggleable independently. At 7 days, 1 day, and
      on the deadline itself. Spec #104, tickets #105-#108.
- [x] Export deadlines to .ics, and a private subscription feed. Ticket #105.

---

## Medium Priority

Stage 3. Documents (roughly 5–7 days).

- [ ] Clients with details. Spec #89, ticket #90.
- [ ] Bilingual EN/UK PDF invoice: IBAN, SWIFT, numbering. Link to a receipt, "paid" status.
      Spec #89, tickets #91-#94.
- [ ] F0103309 XML declaration per the current DPS schema, for import into the Electronic
      Cabinet. Spec #109, tickets #110-#112.
- [ ] Yearly archive, a reminder to keep documents at least 1095 days after the declaration that
      covers them (Tax Code 44.3), extended by any martial-law suspension.

---

## Low Priority

- [ ] PrivatBank: CSV/XLSX import and the Autoclient API. Deferred until the owner opens a FOP
      account there (decided 2026-09-28).
- [ ] Multi-user mode: remove the allowlist, self-registration, data isolation.
- [ ] PWA offline mode.
- [ ] Sentry free tier.
