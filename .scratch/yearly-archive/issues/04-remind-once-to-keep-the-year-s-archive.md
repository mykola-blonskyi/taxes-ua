GitHub: #287
Status: ready-for-agent
Blocked by: #284, #286

# Remind once to keep the year's archive

## Parent

#283

## What to build

When the year's last group 3 declaration gets its filing mark, the owner gets one Telegram and email message and one dashboard notice: download the year's archive and keep it until the keep-until date. Sent once per year (dedup key per year), following the owner's language; the notice respects the one-banner rule (ADR-029) and goes away once the ZIP is downloaded or dismissed.

## Acceptance criteria

- [ ] Marking the last declaration filed claims one reminder per channel per year; marking again or a restart sends nothing more (tests).
- [ ] The dashboard notice links to the archive and clears after a download or a dismissal.
- [ ] Messages in uk and ru, the date formatted for Kyiv.
- [ ] Business rules (Rule 17) describe the reminder.

## Blocked by

- #284
- #286
