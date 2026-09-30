# Glossary

## Terms

### FOP
Fizychna osoba-pidpryiemets — a sole proprietor / individual entrepreneur in Ukraine.

### Group 3 single tax
Simplified taxation system. Rate: 5% of income, no VAT. Expenses are not deductible. Annual
income limit: 1,167 minimum wages.

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
on the form.

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
