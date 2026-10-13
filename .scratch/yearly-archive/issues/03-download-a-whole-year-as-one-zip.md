GitHub: #286
Status: ready-for-agent
Blocked by: #285

# Download a whole year as one ZIP

## Parent

#283

## What to build

From the archive screen the owner downloads the year as one ZIP: invoice PDFs, declaration XMLs and annexes, the receipts statement, the payments register, and a table of contents in the owner's language with the keep-until date. It streams on request and is never stored; its name carries the year and the download date.

## Acceptance criteria

- [ ] The ZIP holds exactly the archive screen's items plus the table of contents; an API test opens it and checks entries and names.
- [ ] It streams without holding the whole year in memory, and is owner-scoped.
- [ ] A Playwright owner flow opens the archive and downloads the ZIP.

## Blocked by

- #285
