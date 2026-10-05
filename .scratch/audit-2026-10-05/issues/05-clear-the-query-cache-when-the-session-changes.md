GitHub: #242
Status: ready-for-agent
Blocked by: none

# Clear the query cache when the session changes

## Parent

#237

## What to build

Sign-out and passkey sign-in remove only the `me` query (`useSignOut.ts:15`, `usePasskeyCeremony.ts:135-139`). Another allowlisted user signing in in the same tab sees the previous user's cached data for up to 60 s. There is also no global handling of a 401 from an expired session.

## Acceptance criteria

- [ ] Sign-out and sign-in clear the whole query cache.
- [ ] A 401 from any query sends the user to sign-in once.
- [ ] Tests for both.

## Blocked by

None (can start immediately)

Details: reports/audits/2026-10-05-full-audit.md, Web M1 and the 401 Low.
