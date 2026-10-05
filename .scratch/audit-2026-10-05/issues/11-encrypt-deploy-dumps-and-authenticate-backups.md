GitHub: #248
Status: closed
Blocked by: #247

# Encrypt deploy dumps and authenticate backups

## Parent

#237

## What to build

`MigrationDump.cs:131-150` keeps up to 10 unencrypted full dumps, bypassing ADR-031. The weekly restore check picks the newest file by name (`backup.sh:318`), rejects only files older than 48 h (`:327`) and restores as the `postgres` superuser (`:341-343`), so a forged future-dated object from a leaked bucket key runs as superuser in a sidecar holding `DATABASE_URL` and the S3 keys.

## Acceptance criteria

- [ ] Pre-migration dumps are encrypted to the backup recipient; retention is 3.
- [ ] The restore check skips future-dated files and restores as a non-superuser role.
- [ ] `.gitignore` covers `*.dump`, `*.dump.age` and root images; the restore runbook names a working directory outside the repo.

## Blocked by

- #247

Details: reports/audits/2026-10-05-full-audit.md, Security N1, N2, N4.
