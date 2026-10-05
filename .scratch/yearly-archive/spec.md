GitHub: #283

## Problem Statement

The owner must keep the documents behind each tax year for at least 1095 days after the declaration that covers them was filed, or after its deadline if it was not (Tax Code art. 44.3), and longer while limitation periods are suspended under martial law. Today the app produces those documents (invoices, declaration files, the receipts statement, budget payments) but scatters them across screens, never says until when they must be kept, and gives no single place to take a year's records away for safekeeping.

## Solution

Each tax year gets an archive: one screen that lists the year's documents with a download for each, a single ZIP with the whole year, and a keep-until date shown on the year and in the archive. After the year's last declaration is filed, the owner gets one reminder to download the archive and keep it until that date. The archive holds only documents the app itself makes; nothing is ever deleted by the app.

## User Stories

1. As the owner, I want to see until when a year's documents must be kept, so that I never discard them early.
2. As the owner, I want the keep-until date to follow the actual filing date of each declaration, or its deadline when not filed, so that it matches the law.
3. As the owner, I want the date marked as extended while martial law suspends limitation periods, so that I do not rely on a date that does not apply yet.
4. As the owner, I want one screen per year listing its issued and cancelled invoices, its declaration files with their filing marks, its receipts statement and its budget payments, so that I find any record quickly.
5. As the owner, I want to download each document on its own, so that I can send one to an accountant or the DPS.
6. As the owner, I want to download the whole year as one ZIP, so that I can store it off the app.
7. As the owner, I want the ZIP to contain a table of contents with the keep-until date, so that the file explains itself years later.
8. As the owner, I want the ZIP named by year and date, so that several downloads do not overwrite each other.
9. As the owner, I want one Telegram and email message and a dashboard notice after the year's last declaration is filed, so that I remember to download and keep the archive.
10. As the owner, I want that reminder sent once, so that it stays meaningful.
11. As the owner, I want the archive and reminder in Ukrainian and Russian, following my language switch.
12. As the owner, I want the archive usable on a phone at 375 px.
13. As a maintainer, I want the keep-until rule written in the business rules with its legal sources, so that it can be checked.

## Implementation Decisions

- **Keep-until date.** It is a pure engine function over the year's quarters: for each quarter in group 3 with a declaration, the filing date if filed, else the statutory deadline, plus 1095 days; the year's date is the latest. Martial-law suspension is a parameter (start date, optional end date), not code; while it has no end date, the date is reported as "extended while martial law lasts". The current legal basis is verified against zakon.rada.gov.ua before coding and cited in the business rules, next to the existing Rule 14 retention paragraph.
- **Archive listing.** One read endpoint per year returns the documents the app already has: issued and cancelled invoices (PDF rendered from the frozen snapshot), declaration files and annexes per quarter with their filing marks, the receipts statement, and the budget payments register. Each item links to an existing or new download route; nothing new is stored.
- **ZIP.** It streams on request and is never stored. It contains the same items plus a table of contents (uk or ru by the owner's language) with the keep-until date. Its size is bounded by one owner's year.
- **Reminder.** It reuses the incident/reminder delivery with once-per-key dedup (key per year), fired when the year's last group 3 declaration gets its filing mark; plus one dashboard notice that respects the one-banner rule (ADR-029) until the owner downloads the ZIP or dismisses it.
- **Out of scope by owner decision 2026-10-06:** uploads of the owner's own files.

## Testing Decisions

- Test external behaviour at the highest seam: the engine for the keep-until arithmetic (filed vs deadline, last quarter wins, suspension open and closed), the HTTP API through the shared fixture for the listing, downloads and ZIP contents, component tests through the shared harness for the screen and the notice, and one Playwright owner flow (open the archive, download the ZIP) plus the 375 px layout check.
- Prior art: deadline calendar and limit tests in the engine; declaration file, invoice PDF and export tests in the API; reminder dedup tests; the dashboard notice tests.

## Out of Scope

- Uploading or storing the owner's own documents (contracts, acts, Cabinet receipts, bank statement PDFs).
- Any automatic deletion of records.
- Keeping documents outside the app (the owner stores the ZIP).
- Income-book or other registers the app does not already produce.

## Further Notes

- Tickets follow in dependency order. The keep-until date and the archive screen can start at once.
