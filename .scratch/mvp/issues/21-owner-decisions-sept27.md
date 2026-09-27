# 21: Owner decisions: prorated ESV default, rename the total column, warn on pre-registration payments, reject future receipts

GitHub: #48
Status: closed, done (PR #56)
Blocked by: #9

## What to build

Four small owner decisions made on 2026-09-27.

1. **Prorated ESV becomes the default.** The owner confirmed that
   `EsvRegistrationMonthPolicy.Prorated` (ESV for the registration month proportional to active
   days) is the intended behaviour. Make it the default for new settings. Record the decision in
   Rule 3 and remove the "to confirm" note. Existing settings rows keep their stored value.
2. **Rename the pooled column.** On the Periods quarters table, rename "Разом" / "Итого" to
   "Нараховано всього" / "Начислено всего". It is a sum of accruals across kinds, not a balance.
3. **Soft warning for a payment dated before registration.** A budget payment whose `PaidOn` is
   before `FopRegistrationDate` is still saved and credited. The payments list shows a soft
   warning on that row ("дата оплати раніше дати реєстрації, перевірте").
4. **Reject receipts dated in the future.** A transaction whose `ValueDate` is after today in Kyiv
   is rejected with a 400 field error. The form prevents picking such a date. Income arises on the
   credit date, so a real receipt cannot have a future date. This also removes the
   provisional-NBU-rate case from #6.

## Acceptance criteria

- [x] Each of the four behaves as described, with api tests for 1, 3 and 4.
- [x] `knowledge/business-rules.md` updated (Rule 3, and the receipt date rule).
- [x] uk and ru messages.
- [x] Verified live at 375 and 1280.

## Blocked by

- #9
