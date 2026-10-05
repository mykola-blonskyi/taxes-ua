GitHub: #258
Status: ready-for-human
Blocked by: none

# Restrict the VPS's published ports

## Parent

#237

## What to build

Host firewall hardening, owner action on the VPS. The specifics were checked over ssh on 2026-10-05 and are kept off this public tracker; the owner has them in the session notes.

## Acceptance criteria

- [ ] Only Cloudflare's ranges reach 80/443, enforced where Docker-published ports cannot bypass it.
- [ ] No other container port is published on a public interface.
- [ ] The site still loads through Cloudflare; a direct request to the origin times out.
- [ ] `docs/deploy.md` records the rule without the IP.

## Blocked by

None
