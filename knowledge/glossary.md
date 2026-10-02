# Glossary

## Terms

### FOP
Fizychna osoba-pidpryiemets — a sole proprietor / individual entrepreneur in Ukraine.

### Group 3 single tax
Simplified taxation system. Rate: 5% of income, no VAT. Expenses are not deductible. Annual
income limit: 1,167 minimum wages.

### Group 3 start (`Group3Since`)
The day the DPS register has the FOP as a group 3 payer from: the registration date when the
application was filed within 10 days of it, otherwise the first day of a later quarter (Rule 8).
Unconfirmed until the owner enters the DPS receipt.

### EP (yedynyi podatok)
Single Tax. In code: `SingleTax`.

### VZ (viiskovyi zbir)
Military Levy. 1% of income for Group 3. In code: `MilitaryLevy`.

### ESV (yedynyi sotsialnyi vnesok)
Unified Social Contribution for oneself. 22% of the minimum wage per month. In code: `Esv`.

### Minimum wage
Set by the state budget law as of January 1. Base for ESV and the income limit.

### Cumulative declaration
Quarterly declaration for a single-tax payer, reporting income cumulatively from the start of the
year. Form F0103309.

### Electronic Cabinet
cabinet.tax.gov.ua. Where the declaration is signed and the taxpayer's integrated ledger card is
visible.

### Filed mark
The owner's record that a quarter's declaration was filed in the Cabinet: the date, the type
(reporting, new reporting, clarifying) and line 08 as it stood. In code: `DeclarationFiling` (Rule 15).

### Declaration readiness
Whether a quarter's declaration can be filed from what the app holds: nothing left to review, the
year verified, the registration date and every declaration detail set, income within the limit.
Unpaid taxes are a warning, not a blocker (Rule 15).

### KVED
The classifier of economic activities (KVED, DK 009). A code such as `62.01`; the first of the
owner's codes is the main activity.

### Tax office code (Kod DPI)
The tax office the declaration is filed with, as its region code (C_REG) and district code (C_RAJ)
on the form, with its name (HSTI) as the Cabinet shows it.

### Declaration file
The quarterly declaration as an F0103309 XML file in windows-1251, named per DPS standard No. 729, for
M.E.Doc and other software that imports it. The Electronic Cabinet has no XML import. In code:
`DeclarationFile` (Rule 15).

### Fill in the Cabinet
The declaration screen's main path: every field of F0103309, and of annex 1 when it applies, in the form's
order, each with the plain value to type and a copy button; the owner enters them by hand in the Cabinet
("Введення звітності" → "Створити"), signs with a KEP and sends. In code: `CabinetField` (Rule 15, ADR-025).

### ESV annex (annex 1)
"Відомості про суми нарахованого доходу застрахованих осіб та суми нарахованого єдиного внеску", form
F0133109: each month's ESV base, rate and ESV for oneself, filed with the year's last group 3 declaration
and linked to it. Its total is the declaration's line 21. In code: `EsvAnnex` (Rule 15).

### Integrated ledger card (taxpayer's ledger)
The taxpayer's account with the State Tax Service (DPS): accrued, paid, owed or overpaid, per
payment type. The app's equivalent is the per-kind balances.

### KEP
Qualified electronic signature (kvalifikovanyi elektronnyi pidpys).

### DPS
State Tax Service of Ukraine (Derzhavna podatkova sluzhba).

### NBU
National Bank of Ukraine. Publishes the official exchange rate.

### Obligation
A computed entity: what is owed, for which period, how much, and by when.

### Next step
The home screen's answer to "what do I pay, by when, how much": what is due or overdue, else the
nearest open deadline, one figure per kind. Computed by `NextStep` in the engine (Rule 7).

### Reminder
A message about one date that still has something to do: a payment owed, an advance, or a declaration
not marked filed, sent 7 days before, 1 day before, on the day and, for a payment, the day after (Rule
17). Computed at each run, never stored ahead. In code: `ReminderPlan`, and `SentReminder` for the log.

### Advance
A voluntary monthly payment under `MonthlyAdvance` mode, credited against the quarter.

### Kopecks, `Kop`
All hryvnia amounts in whole kopecks. `Minor` denotes the minor unit of any currency.

### `RateE4`
The NBU exchange rate, multiplied by 10,000 and rounded to an integer.

### Basis point, `Bp`
One hundredth of a percent. 5% = 500 bp.

### Payment purpose (призначення платежу)
The purpose line of a budget transfer: `101`, the kind in words, the period and `року`, for example
`101 єдиний податок за III квартал 2026 року`. Rule 16.
