# 04: Show and write the official KVED name for every class of КВЕД ДК 009:2010

GitHub: #227
Status: ready-for-agent
Blocked by: none

## Problem

#225 fills the F0103309 KVED name (`T1RXXXXG2S`) from a checked-in list of 27 classes of КВЕД ДК 009:2010 in `api/src/TaxesUa.Api/Features/Declarations/Kved.cs` (moved to `Features/Settings/Kved.cs` by #185). The owner reports two gaps:

- Settings → declaration details → КВЕДи shows only the code (for example 62.01). The owner cannot see the name, and had to type the name into the Cabinet by hand.
- A code outside the 27 entries gets an empty name in the XML.

## What to build

1. **Complete classifier.** Replace the 27 entries with every class of КВЕД ДК 009:2010 (NN.NN, about 615) and its official name exactly as Держстат publishes it on kved.ukrstat.gov.ua (`KVED2010/{dd}/KVED10_{dd}_{cc}.html`, the `<p class="Na">` text).
   - A small script fetches the pages and writes the data file. Both the script and the generated data are committed.
   - ASCII apostrophes and "н.в.і.у." without spaces are kept, as Держстат writes them.
   - A test pins 62.01, 63.99, 85.59 and 47.91 and the total count.
2. **API validation.** A KVED code in the declaration details is checked against the classifier. An unknown code is refused with a `ProblemCodes` code (ADR-028) that has uk and ru texts.
3. **Settings UI.** Under each KVED input the form shows the class's official name in Ukrainian, from the API, as the owner types a valid code, and a clear message for an unknown code. Plain code entry with the live name is enough; a searchable picker is optional.
4. **Declaration XML and the Fill-in-the-Cabinet view** carry the name for every code, and the XML still validates against the XSD.

`web/src/data/api/schema.d.ts` is regenerated. Storing only codes keeps the backup schema unchanged; a change to the stored shape follows the backup convention (schema bump, fixture, round-trip test).

## Acceptance

- [ ] The classifier holds every class with Держстат's names; the script that built it is in the repo and reruns.
- [ ] A test pins 62.01, 63.99, 85.59, 47.91 and the count.
- [ ] Saving an unknown code is refused with its own code, worded in uk and ru.
- [ ] The settings form shows the name under each valid code as it is typed, and a message for an unknown code.
- [ ] The XML and the Cabinet view carry the name for every code; the XML validates against the XSD.
- [ ] API tests, component tests in uk and ru, `pnpm test`, `pnpm lint` and the full `pnpm e2e` (layout, axe, touch) pass.
