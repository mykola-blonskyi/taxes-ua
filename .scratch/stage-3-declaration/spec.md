GitHub: #109
Status: ready-for-agent

# Spec: Declaration readiness and the F0103309 XML for the Electronic Cabinet

## Problem Statement

Every quarter the owner files the single tax declaration in the Electronic Cabinet by retyping the app's figures into the form, and every year adds the ESV annex. Retyping invites errors, and the owner has no single place that says whether the quarter's data is complete enough to file: unreviewed imports, pending budget payment candidates, unverified year parameters and unpaid obligations are spread across screens.

## Solution

A Declaration screen per quarter answers "am I ready to file?" with a checklist, and produces the declaration as an XML file in the DPS format that the owner imports into the Electronic Cabinet ("Імпортувати XML з пристрою"), checks, signs with a KEP there and sends. For the annual declaration it also produces the ESV annex. After filing, the owner marks the quarter's declaration as filed, which stops declaration reminders. The app never signs or sends anything.

## User Stories

Readiness

1. As a FOP, I want a Declaration screen for each quarter, so that everything about filing lives in one place.
2. As a FOP, I want a checklist of what must be settled before filing: every receipt of the period reviewed, no pending budget payment candidates, the year's parameters verified, the registration date set, my declaration details complete, so that I file on complete data.
3. As a FOP, I want each unmet item to link to where I fix it, so that the checklist is actionable.
4. As a FOP, I want to see the declaration's figures (cumulative income, tax, tax of the previous period, tax payable, military levy lines) exactly as they will appear in the form, so that I can compare with the Cabinet's preview.
5. As a FOP, I want to see the filing deadline and the payment deadline next to it, so that I know how much time is left.
6. As a FOP, I want unpaid obligations shown as a warning, not a blocker, so that I can file on time even if I pay later.
7. As a FOP, I want to mark the quarter's declaration as filed with the date, so that the app and its reminders know.
8. As a FOP, I want to undo the filed mark, so that a mistaken click is harmless.
9. As a FOP, I want a later change to the quarter's receipts after filing to show a warning that the filed declaration may need a clarifying one, so that I notice.

Declaration details

10. As a FOP, I want to enter my tax office code (the region and district codes shown in the Cabinet as "Код ДПІ"), my KVED codes and my address as it appears in the register, so that the header of the form is complete.
11. As a FOP, I want my name and RNOKPP reused from my invoicing details, so that I enter them once.

XML

12. As a FOP, I want to download the quarterly declaration as an F0103309 XML file named by the DPS rules, so that the Cabinet accepts the import.
13. As a FOP, I want the file validated against the published schema before I get it, so that I never import a file the Cabinet rejects.
14. As a FOP, I want the declaration type chosen correctly (reporting, new reporting, clarifying), with reporting as the default, so that the file matches what I am filing.
15. As a FOP, I want for the annual (fourth-quarter) declaration the ESV annex (F0133109) produced as a second file and linked from the declaration, so that the annual filing is complete.
16. As a FOP, I want a short guide on the screen for importing, checking, signing and sending in the Cabinet, so that the last steps are clear.
17. As a FOP, I want the downloaded files kept as a record of what I filed, with the date, so that I can see later what was in them.

Data

18. As a FOP, I want the filed marks, declaration details and generated files in the backup, so that my filing history survives a restore.
19. As a future second user, I want all of this isolated by owner.

## Implementation Decisions

- Modules: a declaration builder in the engine or next to it (pure: from the year's accruals, payments and parameters to the form's line values); an XML writer for F0103309 and F0133109 (pure, deterministic); a readiness evaluator (pure over the app's state); endpoints and a Declaration screen; declaration details in settings; a filed mark per owner, year and quarter.
- Form and version: F0103309 (C_DOC F01, C_DOC_SUB 033, C_DOC_VER 9), MinFin order 19.06.2015 No. 578 as amended by order 31.01.2025 No. 57 (in force 20.02.2025, applies from 2025). ESV annex F0133109 for the annual declaration, a separate file linked through LINKED_DOCS with the annex flag set.
- The published XSDs (F0103309.xsd, F0133109.xsd and common_types.xsd) are vendored into the repository with their source and date recorded; generated XML is validated against them in tests and before every download. Because the official register (tax.gov.ua reestr-form) refuses automated fetches, the vendored files come from the mirror found in research and the owner confirms once, by hand, that they match the register; the ticket does not merge without that confirmation.
- Encoding windows-1251 with the lowercase XML declaration, element order exactly as the XSD, no whitespace between elements, amounts with exactly two decimals, flags as `1`, dates `ddmmyyyy`. C_DOC_STAN 1, 2 or 3 for reporting, new reporting, clarifying; C_DOC_TYPE 0; PERIOD_TYPE 2, 3, 4 or 5 with PERIOD_MONTH 3, 6, 9 or 12; C_STI_ORIG = C_REG × 100 + C_RAJ. File name per the DPS standard (order No. 729 of 29.11.2013): C_REG, C_RAJ, the TIN padded to 10, C_DOC, C_DOC_SUB, C_DOC_VER, C_DOC_STAN, C_DOC_TYPE, C_DOC_CNT (7 digits), PERIOD_TYPE, PERIOD_MONTH, PERIOD_YEAR, C_STI_ORIG, `.xml`.
- Line mapping for group 3 at 5% without VAT (to be confirmed against the XSD and the order's form): income at 5% cumulative (R006G3), total income (R008G3), tax at 5% (R011G3), total tax (R012G3), tax of the previous period (R013G3), tax payable (R014G3 and R0141G3), military levy section VIII (R023G3 at the year's rate on the income lines, R024G3 the previous period's R023G3, R025G3 the difference); lines for 15%, other groups and corrections left empty; ESV (R021G3) carried from the annex in the annual declaration. The builder reads rates from the year's parameters, never hard-codes them.
- Readiness is computed on read: receipts of the period with ReviewStatus NeedsReview, pending budget payment candidates, the year's verification, registration date, declaration details, and (as a warning) unpaid obligations for the period.
- Filed mark: owner, year, quarter, filed on, declaration type; undo allowed; the reminders spec reads it to stop declaration reminders. A change to the quarter's receipts after the mark raises a warning.
- Generated files are stored per owner, year, quarter and type with the generation time, and included in the backup.
- API: readiness and figures for a quarter; declaration details get and put; generate and download the XML (and the annex for Q4); list stored files; mark and unmark filed. Types generated from OpenAPI.
- Docs: a declaration rule in business-rules.md (what is generated, the filed mark), domain model, an ADR recording that the app prepares the file and the owner signs and sends in the Cabinet, and the vendored-schema decision.

## Testing Decisions

- Good tests check the XML the owner would import: validity against the vendored XSDs, the exact line values for known inputs, the file name, the encoding; and what the Declaration screen shows.
- The builder and XML writer are pure and tested with table and golden-file tests: each quarter, the annual declaration with the annex, a year with refunds, a partial first year after registration, the military levy's previous-period line, the 2026 reference figures already in knowledge/business-rules.md.
- Endpoints at the existing API seam (ApiFixture, fake time); no new seam.
- UI proved in a real browser at 375 px in uk and ru; the owner imports one generated file into the Cabinet (without sending) and confirms it loads.

## Out of Scope

- Signing with a KEP, sending to the DPS, or any Cabinet integration beyond a file the owner imports.
- Clarifying declarations with penalty calculations beyond setting the type.
- Declarations for groups 1 and 2, VAT payers, or the 15% rate.
- Reading the Cabinet's integrated ledger card.

## Further Notes

- Sources (research 2026-09-30): https://zakon.rada.gov.ua/laws/show/z0232-25, https://zakon.rada.gov.ua/laws/show/z0799-15, https://cabinet.tax.gov.ua/help/reporting.html, https://medoc.ua/files/others/042024/f13b2171d12465a45882d3099c576610.pdf (format standard, secondary copy), https://github.com/NadozirnySvyatoslav/l10n_ua (XSD mirror, secondary), https://7eminar.ua/news/5579-nova-deklaraciya-platnika-jedinogo-podatku-fop-3-grupi-instrukciya (line meanings, secondary). tax.gov.ua refused automated fetches; the form register, the F0133109 structure and the tax office directory (SPR_STI) are unverified and must be confirmed by hand.
- The owner finds the tax office code in the Cabinet ("Код ДПІ") or on a previously filed report.
