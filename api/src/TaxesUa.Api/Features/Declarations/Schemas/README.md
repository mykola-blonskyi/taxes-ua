# Vendored DPS schemas

`F0103309.xsd` and `common_types.xsd` describe the group 3 single tax declaration, form F0103309
(C_DOC F01, C_DOC_SUB 033, C_DOC_VER 9), MinFin order No. 578 of 19.06.2015 as amended by order No. 57
of 31.01.2025. Every declaration file the app generates is validated against them before the owner can
download it (ADR-016).

The official register (tax.gov.ua, reestr-form) refuses automated fetches, so the files come from a
mirror and the owner confirms by hand that they match the register.

| File | SHA-256 |
| --- | --- |
| `F0103309.xsd` | `46346d1ffc5f6ef8d32e6b577dbc065a60da58d28926cde8cfafb89953b50a59` |
| `common_types.xsd` | `f53226747bda74c793f2fd9e9b55d55e5073219f866b2cd29c10e60ba340fef1` |

- Source: https://github.com/NadozirnySvyatoslav/l10n_ua, branch `19.0`, path
  `l10n_ua_tax_F0103309/tests/schemas/`.
- Commit that last changed both files: `26c0abf5627029f8565a5e17763a2e1738269821` (2026-07-15).
- Branch head when fetched: `4ad207206ca29b3833892bbf9429fb2c2064188b`.
- Fetched: 2026-09-30.

The files are byte-for-byte as published: windows-1251 with CRLF line ends. `.gitattributes` keeps git
from normalizing them. Replace both together, record the new source and hashes here, and rerun the
golden-file tests.
