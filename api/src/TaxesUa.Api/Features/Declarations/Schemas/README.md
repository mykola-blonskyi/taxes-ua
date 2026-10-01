# Vendored DPS schemas

`F0103309.xsd` and `common_types.xsd` describe the group 3 single tax declaration, form F0103309
(C_DOC F01, C_DOC_SUB 033, C_DOC_VER 9), MinFin order No. 578 of 19.06.2015 as amended by order No. 57
of 31.01.2025. `F0133109.xsd` describes its annex 1, the ESV for oneself (C_DOC F01, C_DOC_SUB 331,
C_DOC_VER 9), same order, and includes the same `common_types.xsd`. Every file the app generates is
validated against its form's schema before the owner can download it (ADR-016).

The official register (tax.gov.ua, reestr-form) refuses automated fetches, so the files come from a
mirror and the owner confirms by hand that they match the register.

| File | SHA-256 |
| --- | --- |
| `F0103309.xsd` | `46346d1ffc5f6ef8d32e6b577dbc065a60da58d28926cde8cfafb89953b50a59` |
| `common_types.xsd` | `f53226747bda74c793f2fd9e9b55d55e5073219f866b2cd29c10e60ba340fef1` |
| `F0133109.xsd` | `80233d595bf03ebec9c11963c910efa467e8047c90bd043161b448dfc9fbaed5` |

`F0103309.xsd` and `common_types.xsd`:

- Source: https://github.com/NadozirnySvyatoslav/l10n_ua, branch `19.0`, path
  `l10n_ua_tax_F0103309/tests/schemas/`.
- Commit that last changed both files: `26c0abf5627029f8565a5e17763a2e1738269821` (2026-07-15).
- Branch head when fetched: `4ad207206ca29b3833892bbf9429fb2c2064188b`.
- Fetched: 2026-09-30.

`F0133109.xsd`, which that mirror does not carry:

- Source: https://github.com/lzeal/tax-fop-3rd, branch `master`, path `src/templates/F0133109.xsd`.
- Commit that added it: `2acd485fa4e3038a7b5bdf70d599212ffdc4c27f` (2025-10-24).
- Branch head when fetched: `c4ad51294a37d9891d3ca6d0ff93d5236fa9c97e`.
- Fetched: 2026-09-30.
- Why it is trusted: the same repository's `F0103309.xsd` is byte for byte the copy above from the
  other mirror (same SHA-256), so it carries the DPS files unaltered. This one fixes C_DOC_SUB 331 and
  C_DOC_VER 9, which the Cabinet's form list shows for the annex (checked by hand on 2026-09-30), cites
  order No. 57 of 31.01.2025 and ends with the DPS developer's note like the others.

The files are byte-for-byte as published: windows-1251 with CRLF line ends. `.gitattributes` keeps git
from normalizing them. Replace a form's schema together with its writer, record the new source and
hashes here, and rerun the golden-file tests.
