# 02: Download the quarterly declaration as F0103309 XML

GitHub: #111
Status: ready-for-agent
Blocked by: #110
Parent: #109

## What to build

Download the quarterly declaration as an F0103309 XML file (C_DOC F01, C_DOC_SUB 033, C_DOC_VER 9, MinFin order No. 578 as amended by No. 57 of 31.01.2025) for import into the Electronic Cabinet. The XSDs (F0103309.xsd, common_types.xsd) are vendored with their source and date; every generated file is validated against them before download. windows-1251 with the lowercase XML declaration, exact XSD element order, no whitespace between elements, amounts with two decimals, dates ddmmyyyy, C_DOC_STAN from the chosen declaration type, C_DOC_TYPE 0, PERIOD_TYPE and PERIOD_MONTH by quarter, C_STI_ORIG = C_REG × 100 + C_RAJ, file name per DPS standard No. 729. The screen shows a short guide: import in the Cabinet, check, sign with the KEP, send. Each generated file is stored with its time and in the backup.

## Acceptance criteria

- [ ] Golden-file tests for each quarter; every file validates against the vendored XSD; file name and encoding checked byte for byte.
- [ ] The owner confirms by hand that the vendored XSDs match the DPS register (reestr-form) before merge.
- [ ] The owner imports one generated file into the Cabinet (without sending) and confirms it loads with the expected figures.
- [ ] ADR: the app prepares the file; the owner signs and sends in the Cabinet; vendored schemas.
- [ ] Download proved in a real browser.
