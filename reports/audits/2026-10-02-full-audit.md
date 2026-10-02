# Audit Report

Date: 2026-10-02

Auditor: six independent read-only reviews coordinated in one session (security, tax-domain correctness, reliability and operations, architecture and code quality, tests and CI, UX/accessibility/i18n)

Audit Type: full application audit

Scope: `main` at the time of writing (after PR #168). Code, docs, CI history and the public git history. Production was touched only with `GET /api/health`. The VPS and Coolify settings were not inspected; findings about them are inferred from the docs and marked as such in the sections below.

---

## Executive summary

No finding loses data silently. No secret, real tax number or personal IBAN is in the public history, and no dependency has a known vulnerability. The engine's constraints hold: it is pure, uses integer money and Kyiv dates, and takes parameters only from `TaxYearConfig`.

The serious findings are about correctness against the law and about what the owner does not see:

| # | Severity | Finding | Area |
|---|----------|---------|------|
| 1 | Critical | Nothing models a FOP whose group 3 registration is not in place. The app taxes 5% + 1% from the registration date even when the DPS register has no single-tax record, which happened to the owner in real life | domain, UX |
| 2 | High | ESV for the registration month is prorated by default. The law has no part-month minimum, and the DPS states the full monthly minimum is due. The Q3 2026 ESV shown is 190.23 instead of 1,902.34, and the annex F0133109 and line 21 are understated | domain |
| 3 | High | No backup leaves the VPS, and no restore has ever been tested. Migrations run on every auto-deploy, and nothing takes a dump first | reliability, tests |
| 4 | High | A monobank sync that stops (rejected token, bank outage) is recorded but never surfaced. The dashboard then shows less income and less tax than the truth | reliability, UX |
| 5 | High | The declaration guide tells the owner to import the XML, but the Cabinet has no import, and the figures cannot be copied line by line | UX |
| 6 | High | No test runs migrations over data from a previous release | tests |
| 7 | High | API tests share one database and owner, so background workers leak between tests (one confirmed flake) | tests |
| 8 | High | The error contract is free English text. The web parses messages with `includes(...)` | architecture |
| 9 | Medium | The documented `X-Requested-With` anti-forgery check does not exist. Bodiless POSTs rely on `SameSite=Lax`, which sibling `*.blonskyi.dev` apps can bypass | security, architecture |
| 10 | Medium | The origin server IP is published in `docs/deploy.md`, which defeats the Cloudflare proxy | security |
| 11 | Medium | The temporary military-levy Treasury account (2026-07-01 to 2026-12-31) never expires in the pay panel, so the Q4 levy paid in February 2027 may go to a closed account | domain |
| 12 | Medium | No post-deploy health check, no uptime alert, and no memory, log or restart limits in compose | reliability |

The full findings from each review follow, unedited apart from section headings. Each one carries file:line references, the failure scenario and a proposed fix.

## Not fixable in code (owner actions)

- Restrict ports 80/443 on the VPS to Cloudflare's IP ranges. The published IP must be treated as public from now on.
- Confirm the Coolify restart policy, log driver and backup retention on the server.
- Pay the ESV difference for September 2026 (1,712.11 UAH on top of the 190.23 already due) by 2026-10-19, unless the DPS confirms proration.
- 93 historical commits carry assistant attribution trailers. Removing them needs a history rewrite and a force-push to `main`. That is not recommended; new commits carry none.

---


---

## taxes-ua: tax-domain correctness audit

Date: 2026-10-02. Scope: FOP on single tax group 3 at 5%, not a VAT payer, registered 2026-09-28.
Repo: /Users/mykola/workspace/taxes-ua (main, 39ce9bd). The audit was read-only.

Primary legal texts were read from zakon.rada.gov.ua: the Tax Code 2755-VI (`/laws/show/2755-17/print`)
and the ESV Law 2464-VI (`/laws/show/2464-17/print`). tax.gov.ua and zir.tax.gov.ua answer 403 to
scripted fetches, so their pages are cited from search snippets. Those citations are marked "(snippet)".

## Summary by severity

| # | Severity | Area | Verdict |
|---|---|---|---|
| 1 | CRITICAL | New FOP without group 3 in place (Q6) | WRONG / NOT MODELLED |
| 2 | HIGH | ESV in the registration month: prorated by default (Q3) | WRONG |
| 3 | MEDIUM | Rule 8 advice text: "general system until the 1st of the following month" (Q6) | WRONG |
| 4 | MEDIUM | Learned military levy Treasury account has no expiry; accounts are temporary 01.07–31.12.2026 (Q2) | RISK / UNVERIFIED for 2027 |
| 5 | LOW | Shift of the EP/VZ payment deadline off a weekend (Q1) | UNVERIFIED, no 2026 impact |
| 6 | LOW | NBU QR category `TAXS/TAXS`, and whether banks accept a budget QR (Q9) | UNVERIFIED (already flagged in Rule 16) |
| 7 | LOW | Rule 2 wording: "NBU does not publish a rate on weekends" (Q7) | Doc inaccuracy; the code is CORRECT |
| 8 | INFO | Comment in TreasuryPayment.cs:79 dates 11011700/11011800 to July 2026 | Comment inaccurate; behaviour CORRECT |
| — | OK | Rates, limit, deadlines, declaration lines, the file only after quarter end, the annex timing, the purpose format, the KBK, the minimum wage | CORRECT |

---

## 1. CRITICAL: no model for a FOP whose group 3 status is not in place

**What the app does.** Everything from `Settings.FopRegistrationDate` is accrued as group 3 at 5% + 1%
(`api/src/TaxesUa.Engine/Accruals.cs:191-275`, `IncomeLedger.ForYear`). No setting records "single tax
payer since". No warning or onboarding step asks whether the group 3 application was filed and accepted.
A grep of `api/src`, `web/src`, `web/messages` and `knowledge/` for any such field or text finds nothing.
The only mention is a prose "advice" paragraph in `knowledge/business-rules.md:271-273`, and the UI does
not show it.

**What the law says.**
- Tax Code 298.1.2, para 2: a newly registered entity that files the group 3 (non-VAT) application
  within 10 days of state registration is a single tax payer from the day of registration.
- Tax Code 298.1.4: anyone else is "a payer of other taxes" and may move to the simplified system only by
  an application filed no later than 15 calendar days before the next calendar quarter. This is allowed
  once a year.
- Until then the FOP is on the general system:
  - PIT at 18% and military levy at 5% on net income (income minus documented expenses);
  - quarterly PIT advances by the 20th of the month after the quarter (Tax Code 177.5.1);
  - an annual property-and-income declaration;
  - ESV at 22% of net income, but not less than the minimum (Law 2464 art. 7 part 1 item 2).

**Impact for this owner.**
- Registered 2026-09-28. The 10-day window ends on 2026-10-08.
- If the application is missed or rejected, group 3 can start no earlier than 2027-01-01 (application by
  2026-12-16). 2026-09-28 to 2026-12-31 is then general system.
- Example: 100,000 UAH of income with no expenses. The app shows 5,000 EP + 1,000 VZ = 6,000 UAH. The law
  wants about 18,000 PIT + 5,000 VZ = 23,000 UAH, under different deadlines and a different return.
- The app would also produce an F0103309 for Q3/Q4 2026 that the FOP must not file.

**Fix direction.** Add a "single tax group 3 since" date, defaulting to the registration date only after
the owner confirms it. Until it is set, warn on the dashboard and on the declaration screen, as Rule 4
already does for "outside group 3". For any period before it, compute nothing, and say that the general
system applies and the app does not compute it.

Sources:
- Tax Code 298.1.2, 298.1.4 (read from zakon.rada.gov.ua/laws/show/2755-17)
- https://zp.tax.gov.ua/media-ark/news-ark/743505.html (snippet)
- https://taxer.ua/uk/kb/perenesennia-stroku-zaiavy-yedynyi-podatok-fop (snippet: "if the application is
  missed … general system; switch only from the next quarter, application 15 days before")

## 2. HIGH: ESV in the registration month is prorated by days (default `Prorated`)

**Where.**
- `api/src/TaxesUa.Api/Features/Settings/Settings.cs:16-17` (default `Prorated`)
- `api/src/TaxesUa.Engine/Accruals.cs:338-350` (`RegistrationMonthBaseKop`)
- `knowledge/business-rules.md:42-48`
- ADR-018 (`docs/decisions.md` ~line 790)
- Tests pin the prorated figures: `api/tests/TaxesUa.Engine.Tests/AccrualsTests.cs:102-106`,
  `EsvAnnexTests.cs:25`

**What the law says.**
- Law 2464 art. 8 part 2 (the 22% rate) and art. 7 part 1 item 3: a simplified-system FOP sets their own
  base, but "сума єдиного внеску не може бути меншою за розмір мінімального страхового внеску". No
  provision prorates the minimum for a part month.
- DPS consultations and the accounting press agree: ESV for the registration month is due in full,
  whatever the registration day.

**Impact for this owner.**
- Registered on 28 Sep, so 3 of 30 days. The app accrues a base of 864.70 and ESV of 190.23 UAH. The law
  requires 1,902.34 UAH, due by 2026-10-19.
- Underpayment: 1,712.11 UAH. It becomes ESV arrears; Law 2464 art. 25 sets a fine and a penalty, and
  martial-law relief may apply.
- The person loses full insurance-record credit for September.
- Annex 1 (F0133109) for 2026 would declare a base of 864.70 instead of 8,647.00 for month 09. Line 21
  would be understated by the same 1,712.11.
- The quarter reserve and the reminders understate by the same amount.

**Fix direction.** Make `FullMonth` the default, or remove `Prorated`. Re-baseline the tests and the
ADR-018 worked numbers.

Sources:
- Law 2464-VI art. 7, 8, 9 (zakon.rada.gov.ua/laws/show/2464-17, read)
- https://7eminar.ua/news/6368-ci-platit-fop-jesv-jedinii-podatok-ta-viiskovii-zbir-u ("ЄСВ в повній
  сумі 1902,34 грн, пропорційної сплати не передбачено", citing Law 2464)
- https://lv.tax.gov.ua/media-ark/news-ark/print-397369.html (snippet, DPS: a FOP registered at month's
  end owes ESV for that month, not less than the minimum)

## 3. MEDIUM: Rule 8 advice states the wrong fallback

`knowledge/business-rules.md:271-273` says that without the group 3 application filed with the
registration, "the general tax system applies until the 1st of the following month".

That is the group 1–2 rule (298.1.2, para 1). For group 3 there are two paths:
- an application within 10 days of registration gives single tax from the registration date;
- otherwise the general system applies until the first day of a quarter, entered by a 298.1.4
  application filed at least 15 days before it.

The doc both understates the window and misstates the consequence. Fix it together with #1.

Source: Tax Code 298.1.2, 298.1.4 (zakon.rada.gov.ua/laws/show/2755-17).

## 4. MEDIUM: military levy Treasury accounts are temporary (01.07–31.12.2026); the learned account never expires

**Verified.**
- KBK **11011700** "Військовий збір, що підлягає сплаті фізичними особами – підприємцями, які перебувають
  на спрощеній системі оподаткування" is correct for a group 3 FOP.
- **11011800** is for group 3 *legal entities*. Some summaries misattribute it, but the DPS texts are
  clear.
- From 01.07.2026 new military levy accounts were opened under Law 4908-IX. They are valid "з 1 липня 2026
  року по 31 грудня 2026 року". Example: `UA338999980313111029000020001`, a regional account.

**Risk.**
- Rule 12 / #98 learns one account per kind from the latest confirmed payment and offers it in the Pay
  panel and the QR with no validity date (`knowledge/business-rules.md:459-482`).
- The Q4 2026 levy is paid by 2027-02-19. The learned 2026 account may already be closed.
- A transfer to a closed budget account is rejected or returned, so the deadline can be missed.
- Which accounts apply from 2027-01-01 cannot be verified yet.

**Fix direction.** Before the Pay panel offers a Learned or Manual levy account in 2027, warn the owner
to re-check the account in the Electronic Cabinet. Or tie the stored accounts to a validity year.

Minor: the comment at `api/src/TaxesUa.Api/Features/Monobank/TreasuryPayment.cs:79` dates
11011700/11011800 to the July 2026 account change. Both codes exist since 01.01.2025. The regex itself is
fine.

Sources:
- https://7eminar.ua/news/3892-yak-fopu-1-2-4-grupi-jep-znaiti-raxunok-dlya
- https://kyiv.tax.gov.ua/media-ark/news-ark/1025408.html (snippet: new accounts from 1 July)
- https://news.dtkt.ua/simple/individual-single-tax/109219-splata-viiskovogo-zboru-2026-perelik-diiucix-kodiv-klasifikaciyi-vid-dps
- https://od.tax.gov.ua/media-ark/news-ark/943367.html and https://rv.tax.gov.ua/media-ark/news-ark/899546.html
  (snippets: 11011800 = legal entities of group 3)

## 5. LOW: shifting the EP/VZ payment date off a weekend (`ShiftTaxPaymentFromWeekend`, default true)

**Where.** `DeadlineCalendar.cs:86-88`, `Settings.cs:23`.

Tax Code 49.20 shifts only the *filing* deadline. Art. 295.3 and section XX 16-1 item 1.11 count payment
"протягом 10 календарних днів після граничного строку подання". No text was found that shifts the payment
date itself; DPS practice varies.

Impact in 2026: none. The payment dates 05-20, 08-19, 11-19 and 2027-02-19 are all business days.

Verdict: UNVERIFIED. The safe choice is not to shift, or to pay the business day before.

## 6. LOW: NBU QR format 003 (Rule 16, `api/src/TaxesUa.Engine/NbuQr.cs`)

**CORRECT:**
- NBU Resolution No. 97 of 19.08.2025, in force since 01.11.2025, introduced format 003: BCD/003/1/UCT
  fields, the start code `https://qr.bank.gov.ua/`, and Base64URL.
- The 17 fields and the `FEFF` lock mask match the app's own reading of Appendix 4.

**UNVERIFIED:**
- `TAXS/TAXS` as the category/purpose code for budget payments.
- Whether bank apps accept a QR for a Treasury account.

Rule 16 already flags both. Nothing found contradicts the app.

Sources:
- https://7eminar.ua/news/12556-qr-kod-nbu-dlya-obminu-rekvizitami-novi-pravila-vze-z
- https://prodavai24.com/tech/nbu-qr-nova-vesia-003

## 7. LOW: currency income (Rule 2, Rule 12)

**CORRECT.** Tax Code 292.5: foreign-currency income is converted at the NBU rate "на дату отримання
такого доходу". The app uses the credit date (`ValueDate`) and fixes the rate.
`NbuRateClient.cs:24` asks NBU for the exact date first and falls back up to 7 days.

**Doc inaccuracy.** `knowledge/business-rules.md:29-30` says NBU publishes nothing on weekends. In fact the
rate set on the last business day is in force on the weekend, and the NBU API returns it for the weekend
date. The code is still right, because it asks for the exact date.

**Currency sale.** The hryvnia from selling one's own currency is not income. Pairing the legs (60 s,
NBU ±5%) is only a suggestion that the owner reviews, so it is CORRECT as a heuristic.

## 8. Items verified CORRECT

### Single tax

- **Rate.** 5% for group 3, non-VAT (Tax Code 293.3 item 2). Seed: `SingleTaxRateBp` 500 in
  `api/src/TaxesUa.Api/Data/Migrations/20260926093157_AddTaxYearConfigAndSettings.cs:71`.
- **Declaration deadline.** 40 calendar days after the quarter (Tax Code 49.18.2), shifted off weekends
  (49.20). During martial law the holiday list is empty, since holidays are not days off under martial
  law. 2026 dates: 05-11, 08-10, 11-09 and 2027-02-09.
- **Payment deadline.** 10 days after the *unshifted* filing deadline (Tax Code 295.3). The seed default is
  `TaxPaymentCountsFromStatutoryDeclarationDate` = true. 2026: 05-20, 08-19, 11-19, 2027-02-19. The DPS
  and the press compute it the same way; dtkt gives "до 19.08.2026 включно" for H1 2026.
  - https://7eminar.ua/news/7920-vazlivi-dati-dlya-platnikiv-jedinogo-podatku-3-grupi-termini-splati
  - https://news.dtkt.ua/accounting/reposts/110364-deklaraciia-z-jedinogo-podatku-dlia-fop-3-grupi-za-pivriccia-2026-roku-instrukciia-iz-zapovnennia-ta-podannia

### Military levy, group 3, 2026

- 1% of income: Tax Code section XX subsection 10 item 16-1, sub-item 1.1(3) and 1.3(3), "1 відсоток від
  доходу, визначеного згідно із статтею 292". This is in force from 2025 and unchanged for 2026.
- Paid within 10 days after the declaration deadline (item 1.11, para 2), together with EP. Declared on
  lines 23–25.
- KBK 11011700. Seed: `MilitaryLevyRateBp` 100.

### ESV, apart from #2

- Minimum wage 2026: 8,647 UAH (Law 4695-IX "Про Державний бюджет України на 2026 рік", 03.12.2025). It
  is unchanged through October 2026. A mid-year rise was dropped.
- Minimum ESV: 1,902.34 UAH (seed `MinWageKop` 864700, `EsvRateBp` 2200).
  - https://ips.ligazakon.net/lawnews/doc/EN252736-derzhbyudzhet-na-2026-rik-pryynyato
  - https://7eminar.ua/news/20686-pidvishhennya-minimalnoyi-zarplati-z-lipnya-2026-roku-novi-rozmiri-ta
  - https://buhplatforma.com.ua/news/128166-minimalna-zarobitna-plata-z-1-zhovtnia-2026-roku-skilky-lyshytsia-pislia-splaty-podatkiv
- Deadline:
  - Law 2464 art. 9 part 8 says "до 20 числа місяця, що настає за кварталом". The DPS and the press read
    this as the 19th inclusive, shifted to the next business day.
  - Q3 2026 is 19.10.2026; Q1 and Q2 shift to the 20th because the 19th is a Sunday.
  - The app's `EsvDeadlineDay` 19 gives 04-20, 07-20, 10-19 and 2027-01-19. This matches dtkt:
    https://news.dtkt.ua/labor/social-protection/114882-jesv-dlia-fop-za-iii-kv-2026-roku-termini-splati-minimalni-sumi-rekviziti-ta-prikladi-platizkok
  - Caveat: some sites write "до 20 включно". The app's earlier date is the safe side.
- Annex F0133109 timing:
  - It is filed only with the annual declaration, or on leaving the simplified system or terminating.
  - The app attaches it to Q4, or to the crossing quarter with H03 (`Accruals.cs:277-303`, ADR-018).
  - CORRECT. Not modelled: termination of the FOP mid-year (low).
  - https://7eminar.ua/news/5579-nova-deklaraciya-platnika-jedinogo-podatku-fop-3-grupi-instrukciya
- Not verified: R081G1 = 6, the payer category, for a simplified-system FOP.

### Income limit and excess

- **Limit.** 1167 × 8,647 = 10,091,049 UAH (Tax Code 291.4 item 3, minimum wage at 1 January). It is not
  prorated for a part year. CORRECT.
- **Excess.** 15% on the excess only (Tax Code 293.4 item 1, 293.8 item 3).
- **Switch.** From the first day of the month after the crossing quarter (298.2.3 item 3). The application
  is due by the 20th of the month after the quarter (293.8). Rule 4 and `Accruals.cs:248-263` match.
  The levy at 1% stays on the whole income, line 23 covering lines 05–07. CORRECT.

### Declaration F0103309

- **Version.** F0103309 (C_DOC_VER 9) is the current form: Order 578 as amended by Order 57 of 31.01.2025,
  in force since 20.02.2025, unchanged in 2026.
  - https://7eminar.ua/news/5579-nova-deklaraciya-platnika-jedinogo-podatku-fop-3-grupi-instrukciya
  - https://news.dtkt.ua/accounting/automation/97501-novi-deklaraciyi-z-jep-dlia-vsix-grup-vze-u-jedinomu-vikni-podannia-elektronnoyi-zvitnosti
- **Lines.** These match dtkt's line-by-line H1 2026 instruction (link in "Single tax" above) and
  `Declaration.cs:86-99`:
  - 06: income at 5%
  - 07: income at 15%
  - 08: total income
  - 09: tax at 15%
  - 11: tax at 5%
  - 12: total tax
  - 13: previous period's tax
  - 14.1 / 14: payable for the period
  - 23, 24, 25: military levy, previous period's levy, levy payable
  - 21: ESV from annex 1
- **File only after the quarter ends.** `Declaration.cs:61-70`. Conservative and correct: a declaration
  covers a reporting period that has ended.

### Payment purpose (Rule 16, `PaymentPurpose.cs`)

- MinFin Order 148 of 22.03.2023: the structured ISO 20022 purpose is mandatory from 01.07.2023.
- Code 101 comes first, then the tax and the period in free form.
- The RNOKPP goes in the payer field, not in the purpose.
- `101 єдиний податок за III квартал 2026 року` is CORRECT.

Sources:
- https://medoc.ua/blog/zatverdzheno-porjadok-zapovnennja-rekvizitu-priznachennja-platezhu-dlja-splati-podatkiv-i-zboriv
- https://yankiv.com/platizhna-instruktsiya-na-podatky-ta-yesv/

### Other rules

- **Rule 1.** Refunds reduce income in the period of the refund. Own transfers and currency sales are not
  income. CORRECT.
- **Rule 7.** Payments settle the oldest debt first (Tax Code 87.9). CORRECT.

## What to do now, for this owner

1. Confirm the group 3 application was filed by **2026-10-08** and appears in the register of single tax
   payers. If it was not, the app's 2026 figures do not apply (#1).
2. Pay ESV for September 2026 as **1,902.34 UAH** by **2026-10-19**, not 190.23 (#2). Switch the setting
   to `FullMonth`.
3. Before paying the Q4 2026 levy in February 2027, re-check the levy account in the Cabinet (#4).

---

## taxes-ua security and privacy audit

Scope: `main` at 39ce9bd, read-only. Single owner, production at taxes.blonskyi.dev, public repo.
Method: code reading of api/ and web/, compose and CI files, `git log --all -p` pattern scans (239
commits), `pnpm audit --prod`, `dotnet list package --vulnerable --include-transitive`.

**Summary.** There are no Critical or High findings. Authentication, owner scoping, secret handling
and output encoding are careful and well reasoned. The most important gap is in the docs: they say
CSRF is blocked by an `X-Requested-With` check, but the code has no such check. Body-less cookie POSTs
are protected only by `SameSite=Lax`. That cookie setting does not stop requests from the owner's
sibling `*.blonskyi.dev` apps, because the browser treats them as the same site.

---

## Critical

None found.

## High

None found.

## Medium

### M1. The documented anti-forgery check does not exist, so body-less POSTs can be forged from sibling subdomains

- **Where:** `docs/architecture.md:313` claims "Anti-forgery for cookie auth via the
  `X-Requested-With` header and SameSite". Nothing in `api/src` or `web/src` sets or checks that
  header (`grep -ri requested-with` only hits plans/specs). Cookie policy: `api/src/TaxesUa.Api/Program.cs:235-240`
  (`SameSite=Lax`).
- **Evidence:**
  - JSON-bodied endpoints are safe in practice. Minimal API binding answers 415 without
    `application/json`, and restore, import and signature check the content type explicitly. A
    cross-origin request with that content type needs a CORS preflight, and the api never grants one.
  - Many state-changing POSTs take no body, so a "simple" cross-origin `fetch(..., {method:'POST', credentials:'include'})`
    is sent without a preflight. Examples:
    - `POST /api/tax-years/{year}/verify` (`TaxYearEndpoints.cs:98`). It marks tax parameters as
      verified.
    - `POST /api/tax-years/{y}/clone-to/{n}` (`:118`)
    - `POST /api/calendar/feed/rotate` (`CalendarEndpoints.cs:40`). It silently breaks the
      subscription.
    - `POST /api/monobank/sync`, `/api/monobank/reserve-jar/refresh`
    - `POST /api/notifications/channels/{telegram,email}/test`, `/channels/email/resend`
    - `POST /api/declarations/{y}/{q}/files`
    - `POST /api/auth/logout`
    - Invoice and candidate actions, which need a GUID.
  - SameSite=Lax blocks other *sites*. `fitness`, `todo`, `hub`, `plane` and `login` on
    `blonskyi.dev` are the *same site*, so an XSS or compromise in any of them can send the owner's
    cookie on these requests. `plane` in particular is a large third-party codebase.
- **Impact (single owner):** An attacker cannot read data, because CORS blocks the responses. They
  can tamper with state or cause nuisance:
  - silently mark unverified tax parameters as verified
  - rotate the calendar feed
  - spam test messages
  - force syncs
  - regenerate declaration files
- **Fix:** Add one middleware after `UseAuthentication`. For POST/PUT/PATCH/DELETE under `/api` with
  a cookie identity, require `Sec-Fetch-Site: same-origin`. If that header is absent, require an
  `Origin` equal to the public origin. Exempt the anonymous webhook route.
  - The web client needs no change, because browsers send these headers on same-origin fetches.
  - Alternatively, implement the documented `X-Requested-With` check and add the header in the
    fetch wrapper.
  - Then fix `docs/architecture.md:313` to match.

### M2. The origin VPS IP is published next to the note that Cloudflare proxies the domain

- **Where:** `docs/deploy.md:13`, introduced in commit `2cb68a4` (2026-09-28, #70).
- **Evidence:** The line says the "proxied `A` record per subdomain, all pointing at `<vps-ip>`" and
  then gives the literal IP. The same file names the shared PostgreSQL container
  (its id, since scrubbed), the MinIO container, the S3 endpoint and the other projects on the
  host.
- **Impact:** This defeats the point of the Cloudflare proxy. Anyone can reach Traefik directly,
  skipping Cloudflare's WAF, rate limiting and DDoS absorption, and scan the VPS. The step 7 check
  shows the api port itself is closed, so this is exposure, not a direct breach.
- **Fix:**
  - Replace the literal IP with `<vps-ip>`. History keeps it, so treat it as public.
  - Optionally firewall 80/443 on the VPS to Cloudflare's IP ranges, or use a Cloudflare Tunnel, so
    the leaked IP is useless.

## Low

### L1. The Development sign-in seam rests on the environment name alone, and the allowlisted email is public

- **Where:** `Program.cs:301-308` (`MapDevelopmentSignIn` only when `IsDevelopment()`),
  `Features/Auth/DevelopmentSignInEndpoint.cs:16`.
- **Evidence:**
  - Production compose hardcodes `ASPNETCORE_ENVIRONMENT: Production` (`docker-compose.yml:25`).
  - Startup refuses Development together with a pinned `ALLOWED_HOSTS` (`Program.cs:49-56`).
  - Development with an *empty* or `*` `ALLOWED_HOSTS` still starts and publishes the seam.
  - The seam's only other gate is the allowlist. The owner's Google address is in the author line of
    every public commit.
- **Impact:** Today this is not reachable in production. It would be if anyone ever deployed the
  local override, or edited the env on the Coolify compose.
- **Fix (defence in depth):**
  - Also require `HttpContext.Connection.RemoteIpAddress` to be loopback or a private range, or
    require an explicit `Auth:DevelopmentSignIn=true` that production never sets.
  - Fail startup if Development runs with a non-empty Google client secret.

### L2. The local stack publishes the dev seam on all interfaces

- **Where:** `docker-compose.local.yml:5` (`"${WEB_PORT:-3000}:3000"`). Local `.env` sets
  `ALLOWED_EMAILS` (value not read).
- **Impact:** On shared Wi-Fi, anyone who can reach the laptop's port can sign in as the allowlisted
  email on the local stack. This matters if real data was ever restored locally.
- **Fix:** Bind to loopback: `"127.0.0.1:${WEB_PORT:-3000}:3000"`.

### L3. The session cookie is not `__Host-` prefixed, and sessions last 14 days, sliding, with no revocation

- **Where:** `Program.cs:237` (`taxesua.auth`). No `ExpireTimeSpan`, so the default is 14 days
  sliding with `isPersistent: true`. There is no security-stamp validator (ADR-009).
- **Impact:**
  - A sibling subdomain can set a `Domain=.blonskyi.dev` cookie named `taxesua.auth` (cookie
    tossing). It cannot mint a valid ticket, so the effect is denial of service or confusion.
  - A stolen cookie lives up to 14 days after the last use. The only fix is rotating the key ring.
    That trade-off was accepted in ADR-009.
- **Fix:**
  - Rename to `__Host-taxesua.auth`. It already has Secure and the default Path=/, with no Domain.
    Update `web/src/proxy.ts:4`.
  - Consider `ExpireTimeSpan` of 7 days or less, given that a bank token sits behind the session.

### L4. The CSP allows `script-src 'unsafe-inline'`, and Permissions-Policy is missing

- **Where:** `web/next.config.ts:12`.
- **Impact:** The CSP gives no XSS mitigation for inline injection. React escaping, plus no
  `dangerouslySetInnerHTML` in `web/src`, are the real defence today.
- **Fix:**
  - Longer term, use nonce-based CSP via Next middleware, for dynamic rendering.
  - Add `Permissions-Policy: camera=(), microphone=(), geolocation=()`. Note that
    `publickey-credentials-*` must stay allowed for passkeys.

### L5. GitHub Actions are pinned by tag, not SHA, and there is no Dependabot

- **Where:** `.github/workflows/ci.yml:19,20,33,81` (`actions/checkout@v7`, `setup-dotnet@v6`,
  `setup-node@v7`, `upload-artifact@v7`). No `.github/dependabot.yml`. Base images are unpinned
  (`node:24-alpine`, `mcr.microsoft.com/dotnet/*:10.0`).
- **Impact:** The deploy job holds `COOLIFY_WEBHOOK_TOKEN`. A moved tag on a first-party action is
  low probability but would run in that job. Without Dependabot, CVE fixes depend on manual bumps.
- **Fix:**
  - Pin actions to full commit SHAs, with a version comment.
  - Add Dependabot for `github-actions`, `npm` (web) and `nuget` (api).
  - Add `persist-credentials: false` to the checkouts.

### L6. The deploy job prints Coolify's response body to a public log

- **Where:** `ci.yml:142` (`head -c 500 response.txt`).
- **Impact:** Coolify's deploy answer carries resource and deployment UUIDs. The CI logs of a public
  repo are world-readable. This is minor infrastructure disclosure. The token is never echoed, only
  its length.
- **Fix:** Print only the HTTP status. On failure, print the body only when `status != 200`, or mask
  it with `::add-mask::`.

### L7. A crafted backup can restore a confirmed Telegram channel

- **Where:** `Features/Notifications/NotificationChannelBackup.cs:29,42-44`.
- **Evidence:** An email channel is restored unconfirmed and disabled. A Telegram channel keeps its
  chat id, `ConfirmedAt` and `Enabled` from the file.
- **Impact:** If the owner is tricked into restoring a tampered backup, reminders go to an attacker's
  chat. That requires social engineering.
- **Fix:** Restore Telegram channels unconfirmed and disabled as well. Linking again is one click.

### L8. Monobank account ids reach logs at Information through default HttpClient logging

- **Where:** `Program.cs:149` (MonobankClient) and `:138` (NBU) register typed clients with default
  logging. Default level is `Information` (`appsettings.json`). Paths are
  `personal/statement/{accountId}/{from}/{to}` (`MonobankClient.cs:105`).
- **Impact:** The bank account ids and the sync timing end up in Coolify logs. The `X-Token` header is
  not logged, because headers are logged only at Trace and are redacted. This is minor personal data
  in logs.
- **Fix:** Use `.RemoveAllLoggers()` on the monobank client as was done for Telegram, or set
  `System.Net.Http.HttpClient` to Warning in `appsettings.json`.

### L9. Secrets at rest are stored in plaintext in the DB and the volume

- **Where:**
  - The data-protection key ring is persisted without an XML encryptor (`Program.cs:90`).
  - The webhook and calendar secrets are stored as drawn (`CalendarFeed.cs:18`,
    `MonobankWebhooks.cs`).
  - The token is AES-GCM without associated data binding it to `UserId` (`TokenEncryptor.cs:68`).
- **Impact:**
  - Anyone who reads the volume can forge session cookies.
  - A DB dump (Coolify backups in MinIO on the same VPS) yields working feed and webhook URLs. It
    does not yield the bank token, because its key lives only in the env.
  - Root on the VPS already implies all of this.
- **Fix:** Optional. Pass `UserId` as AES-GCM associated data. Hash the webhook and feed secrets,
  storing SHA-256 and showing the URL only at creation. Keep the off-VPS backup copy that step 8
  recommends.

### L10. Tax-year parameters are global, not owner-scoped

- **Where:** `Features/TaxYears/TaxYearEndpoints.cs:36-150`. There is no `UserId` filter, and any
  authenticated user can PUT, verify or clone.
- **Impact:** None with one owner. This becomes cross-tenant tampering if the allowlist ever holds a
  second person.
- **Fix:** Gate writes behind an "admin" email or role before adding a second user.

### Informational

- The untracked `reserve-card-ru-375-surplus.png` sits in the repo root and is not ignored. Root
  screenshots from verification runs could be committed by accident with real figures. Add `/*.png`
  to `.gitignore`, or keep evidence under the ignored `.verify/`.
- 93 commits carry `Co-Authored-By: Claude` or "Generated with Claude Code" trailers. That is not a
  security issue, but it contradicts the owner's no-attribution rule.

---

## Checked and fine

- **Google sign-in:**
  - The allowlist plus `email_verified` are checked on the callback (`AuthEndpoints.cs:81-88`).
  - The external, passkey-ceremony and session cookies are all `HttpOnly`, `Secure=Always`,
    `SameSite=Lax`.
  - The Google handler uses state and correlation cookies.
- **Passkeys:** the allowlist is re-checked on every assertion (`PasskeyEndpoints.cs:147`).
  Registration requires a session. The RP ID is pinned (`PASSKEY_SERVER_DOMAIN`, with fail-fast).
- **Open redirect:** `LocalPath` rejects `//`, `/\` and control characters, and uses
  `Results.LocalRedirect` (`AuthEndpoints.cs:137-144`).
- **Host and forwarded-host pinning:**
  - `ALLOWED_HOSTS` is required, with no wildcard, outside Development.
  - A leftover `X-Forwarded-Host` gets 400.
  - The OpenAPI document is served in Development only.
- **Owner scoping:** I spot-checked 13 endpoints, and all filter on `UserId == user.Id`:
  - payments PUT/DELETE
  - candidates confirm
  - transactions PUT/DELETE
  - invoices PDF
  - invoices link/unlink receipt
  - clients PUT/DELETE
  - declarations filing PUT/DELETE and file download
  - audit
  - calendar feed
  - backup and restore, where restore deletes and inserts are scoped and handle id collisions with
    fresh ids
- **Authentication coverage:** every feature group has `.RequireAuthorization()`. The anonymous
  routes are only health, the auth flow, the monobank webhook and the calendar feed.
- **Anonymous secret routes:** both secrets are 256-bit CSPRNG hex, an unknown value gets 404, and
  the routes are excluded from OpenAPI. The webhook never reads the body and can only queue a sync.
  The feed carries no amounts. `Microsoft.AspNetCore.Hosting.Diagnostics` is raised to Warning, so
  request paths holding secrets are not logged.
- **Monobank token:**
  - AES-256-GCM with a random nonce, and the key comes only from env.
  - The key is validated at startup.
  - The token is never returned (`MonobankConnectionResponse`), never in a backup, and never logged.
  - It is validated against the bank before it is stored.
- **Telegram:** the client has `RemoveAllLoggers()`, because the token is in the URL path. Link codes
  are 24 random bytes, stored hashed, used once and valid for 15 minutes.
- **SMTP:** the password is never logged. Credentials are refused over non-TLS to a non-local host.
  Logs carry only the failure kind and the exception type.
- **Email confirmation:** a data-protection-signed token with expiry, confirmed only inside the
  owner's session.
- **Email headers:** MimeKit builds the message. The recipient passes a strict regex and must
  round-trip through `MailboxAddress.TryParse`. The subject comes from fixed texts. The HTML body is
  escaped.
- **Declaration XML:**
  - It is written with `XmlWriter`, which escapes text.
  - Header fields are checked for non-XML and non-cp1251 characters.
  - Schema validation runs with `DtdProcessing.Prohibit` and `XmlResolver = null`, so there is no
    XXE.
  - Download names go through `SetHttpFileName`.
- **PDF:**
  - MigraDoc and PDFsharp are used with embedded fonts.
  - The signature upload is PNG or JPEG only, checked by content type and magic bytes, capped at
    512 KB, with a CORS-preflighted content type.
  - It is served back with `nosniff`.
- **ICS:** RFC 5545 escaping and folding, with no user-supplied text in it.
- **CSV:** formula-injection prefixing for `= + - @ \t \r`.
- **SQL:** EF Core only. The three `ExecuteSqlAsync` calls are interpolated, so they are
  parameterized advisory locks. There is no `FromSqlRaw` or `ExecuteSqlRaw`.
- **Restore and prototype import:**
  - Each requires a JSON content type and is bounded to 8 MB.
  - It checks `schemaVersion` and runs full `Validate`.
  - It runs in a transaction under a per-owner advisory lock.
  - The `JsonDocument` default depth limit applies.
- **Security headers:** HSTS (1y, includeSubDomains), `nosniff`, `X-Frame-Options: DENY`,
  `Referrer-Policy` and a CSP with `frame-ancestors 'none'`, `object-src 'none'` and
  `form-action 'self'` are set on pages. The api adds the same set except the CSP.
- **Next rewrite proxy:** `/api/*` goes only to the internal `api:8080`, with the destination fixed
  at build time. `proxy.ts` is a UX gate only, and the api is the authority.
- **Service worker:** precache only. It does not cache `/api` responses.
- **Audit log:** an opt-in entity list, so Identity rows and tokens never reach it.
- **Dependencies:**
  - `pnpm audit --prod`: no known vulnerabilities (next 16.3.6, react 19.2.8).
  - `dotnet list package --vulnerable --include-transitive`: none in any of the 4 projects.
- **Secrets in git history** (all 239 commits, all refs):
  - No Telegram bot tokens, Google client secrets (`GOCSPX-`) or OAuth client ids.
  - No private keys, AWS/GitHub/OpenAI-style keys or monobank-shaped tokens.
  - No real 32-byte base64 keys. All the hits were lockfile integrity hashes, and the only key is
    the obviously-fake dev key in `docker-compose.local.yml:19`, which is documented as such.
  - `.env` is gitignored and was never committed. Deleted `deploy/postgres/*` held only a throwaway
    test-container password.
- **Personal data in git history:**
  - **RNOKPP:** only `1234567890`, `0011223344` and padded zeros. No real 10-digit tax number was
    found.
  - **Golden XMLs:** use "Іваненко Іван Іванович, м. Київ, вул. Хрещатик, 1".
  - **IBANs:** all are registry examples (`UA213223130000026007233566001`,
    `DE89370400440532013000`), obviously-fake patterns, or public Treasury budget accounts
    (MFO 899998).
  - **Other numbers:** `43141912` and `37993783` are public ЄДРПОУ codes of tax authorities.
    `8900057610` is the published BNY Mellon correspondent account of Universal Bank.
  - **Phones:** none found.
  - **Owner name and email:** they appear only in commit author metadata, in the domain name, and in
    one doc path (`/Users/mykola/...`).
- **CI permissions:** `contents: read` at the top level. Deploy runs only on push to `main`, and fork
  PRs get no secrets. The webhook URL and token are passed via env, not interpolated into the
  script. The token is never echoed.
- **Playwright artifacts** (uploaded on failure, public for 14 days): they cover only the ephemeral
  local stack. It runs with `owner@example.com`, seeded fake data, blank bot and SMTP settings and
  the fake monobank key. Traces include that throwaway session cookie, which is harmless.

---

## taxes-ua reliability and operations audit (read-only, main @ 39ce9bd, 2026-10-02)

Scope: docs, compose, Dockerfiles, CI, Program.cs, all hosted services, clients. Only external call: GET /api/health -> {"status":"ok","database":true}.
Limits: nothing on the VPS was inspected (no ssh), so Coolify settings (restart policy, log driver, recreate vs rolling, backup retention) are inferred from the docs and marked "verify".

## Critical

None found. The data is a single-owner ledger, and nothing in the code path loses data silently. The nearest candidates are High #1 and #2.

## High

### H1. Auto-deploy ships migrations with no pre-migration dump; rollback is a manual laptop procedure
- docs/deploy.md "Rollback" (~L318-340), .github/workflows/ci.yml (deploy job), api/src/TaxesUa.Api/Program.cs:343-346 (`MigrateAsync` at startup).
- Scenario: every green push to main POSTs the Coolify webhook. A release carrying a migration migrates the shared DB immediately. The runbook asks the owner to run `pg_dump` to the laptop "before deploying any release that carries a migration", but nothing enforces it. CI has no migration detector, so the step is skipped by default. Migrations only move forward (no Down relied on).
- Impact: a bad data migration, such as a column rewrite or a backfill like 20260929005033_BackfillMonobankHistory, can only be undone from the daily pg_dumpall. That means up to 24h of loss plus a restore of every project on the instance, or a hand-extracted single database.
- Fix: in the deploy job, fail or require a manual gate when `api/src/TaxesUa.Api/Data/Migrations/*.cs` changed in the push. Better, add a pre-deploy step that dumps `taxes_ua` (`pg_dump -Fc`) to a dedicated MinIO bucket. Either a Coolify pre-deploy command or a startup step could run it. Add a `-- rollback` note to each migration PR.

### H2. Backup is one daily same-host dump; no off-host copy; restore never tested
- docs/deploy.md section 8 (~L262-312), docs/architecture.md:276.
- What backs the DB up: Coolify scheduled `pg_dumpall` of the whole shared-database instance, daily. Targets are local disk on the VPS and the owner's MinIO, which runs on the SAME VPS and disk (docs admit it: "survive a broken database, but not the loss of the server"). Set up 2026-09-28. The "copy coolify-backups off the VPS" item is left as advice, with no implementation.
- Restore test: none documented. The only check is `gzip -t` and `grep -c "connect taxes_ua"`, which proves the file is readable and not that it restores. There is no drill, no script, and no CI job. Retention is not specified (Coolify default; verify).
- Impact:
  - VPS loss, disk failure, provider account problem or ransomware = total loss of the ledger, invoices (Rule 14 numbers already issued to clients) and filed-declaration markers.
  - The only independent copy is whatever JSON backup the owner remembers to download by hand. The app never reminds the owner and nothing schedules it.
- Estimates:
  - RPO: up to 24h, plus the unknown daily time, if the VPS survives. Unbounded back to the last manual JSON download if it does not.
  - RTO: unproven. Restoring a pg_dumpall to extract one DB is manual: roughly 1-3h for an experienced operator, with no rehearsed script. Add reprovisioning Coolify, DNS, Traefik and 9 env vars (the Google OAuth client, the encryption key and so on, held "in the password manager"): half a day to a day.
- Fix:
  1. Add a nightly `pg_dump -Fc taxes_ua` to an off-host target (Backblaze B2 or another VPS via rclone), run by cron on the host or a Coolify scheduled task.
  2. Run a quarterly restore drill into a scratch DB and record it in reports/audits/.
  3. Add an in-app "last backup downloaded N days ago" nudge, or a server-side periodic JSON export.
  4. Pin retention (14 daily + 8 weekly).

### H3. Sync can stop silently; the owner learns only by opening the monobank settings screen
- api/src/TaxesUa.Api/Features/Monobank/MonobankEndpoints.cs:459-461 (status), Features/Dashboard (no reference to LastFailedAt or SyncedThrough, grep empty); MonobankSyncWorker.cs:41-45.
- Scenario: the token is rejected or revoked (RejectedAt set, so nightly sync skips the account forever), the key is lost (SyncFailure.TokenUnreadable), monobank is down for days, or the webhook is disabled by the bank after 3 failures. Failures are written to BankAccount.LastFailure and logged. Nothing is pushed: no Telegram or email alert (the notification channels exist but carry only tax reminders). The dashboard does not flag a stale `SyncedThrough`.
- Impact: income is missing from the dashboard, so tax figures, limit tracking (the group-3 income limit) and reminder amounts are understated. The owner may miss a limit crossing or underpay. This is the main correctness-of-operations risk.
- Fix: add one "health" reminder kind to ReminderSender. If the owner has a confirmed channel and (RejectedAt != null, or LastFailedAt older than 24h, or SyncedThrough older than 3 days), send a once-per-day message and show a banner on the dashboard.

## Medium

### M1. No memory or CPU limits, no log rotation config, no restart policy in compose
- docker-compose.yml (whole file): no `mem_limit`/`deploy.resources`, no `restart:`, no `logging:` block.
- Scenario: a leak or a large backup/restore (up to 8 MB body, buffered whole: BackupEndpoints.cs:23) in .NET or Next can eat the 7.7 GB VPS shared with Coolify, Traefik, MinIO, Postgres and about 6 other projects, so the OOM-killer picks arbitrarily. Docker json-file logs without `max-size` grow unbounded unless daemon.json sets it (verify on the host). Coolify normally adds `restart: unless-stopped`; verify.
- Fix: add `mem_limit: 512m` (api) and `384m` (web), `logging: {driver: json-file, options: {max-size: 10m, max-file: "5"}}`, and `restart: unless-stopped`. Set `DOTNET_gcServer=0` or `DOTNET_GCHeapHardLimit` if needed.

### M2. Connection pool defaults against a shared Postgres
- Program.cs:129-133 (`UseNpgsql(...)`, no pool settings); connection string in deploy.md step 3 sets no `Maximum Pool Size`, `Timeout` or `Command Timeout`.
- Scenario: Npgsql default max pool is 100 per process. The instance is shared by 6 other projects and max_connections is probably 100 (default; verify). Under a burst, or while two api containers overlap during a deploy, taxes-ua can take connections from `fitness`, `plane` and the others. Conversely, nothing bounds query time (the advisory lock `pg_advisory_xact_lock` waits forever if a transaction hangs: MonobankStatementImport.cs:219, BackupEndpoints.cs:219).
- Fix: append `;Maximum Pool Size=10;Minimum Pool Size=0;Timeout=15;Command Timeout=60` and `Include Error Detail=false` to the connection string; use `SET lock_timeout` in lock transactions.

### M3. Deploy has downtime; the old container's healthcheck window is the only gate; double-fire races
- docker-compose.yml:6-12,44-50, ci.yml deploy job, deploy.md "Automatic deploys".
- Not zero-downtime: for a Compose build pack Coolify runs `docker compose up -d --build`, which recreates changed services (stop, then start; verify). Downtime = image build excluded, then api start + migrations + up to start_period 20s. A long or failed migration holds `web` unavailable (web `depends_on` api healthy). A migration that throws crashes the process (init: true makes it exit), the container restarts in a loop, and the site stays down until a fixing commit. On a failed deploy Coolify keeps no known-good container, so there is no automatic rollback.
- Image tags: `build:` only, no `image:` tag. Coolify tags by commit SHA; the previous image may be pruned. Base images are floating (`dotnet/aspnet:10.0`, `sdk:10.0`, `node:24-alpine`), so a rebuild of the same commit can differ. The rollback story is "revert on main and redeploy", i.e. a full rebuild (about 3-6 min).
- Webhook fired twice: the CI `concurrency` group cancels older runs (ci.yml:7-9) so a re-run is the only duplicate. Coolify queues deployments per resource, so the second deploy rebuilds the same commit and redeploys again (repeat downtime and a repeat startup pass of all workers); not harmful. A cancelled CI run after the webhook fired is possible (cancel-in-progress cancels the job after the POST), leaving the older commit deploying while the newer one queues; the final state is still the newest.
- Failed or long migration specifics: EF Core on Npgsql takes an advisory migration lock and runs each migration in a transaction, so a concurrent second api blocks safely, and a failed one leaves the DB at the previous migration. An `ALTER` on a big table holds ACCESS EXCLUSIVE, but the tables are tiny today.
- Fix: add `image: taxes-ua-api:${SOURCE_COMMIT}` where Coolify supports it, pin base images by digest, and add a post-deploy smoke step in CI (`curl /api/health` for 60s with retry) so a bad deploy fails the pipeline, because today the deploy job passes on HTTP 200 from the webhook alone (ci.yml, last line).

### M4. Reminders: claim-then-send is at-most-once; a restart mid-send loses the reminder
- Notifications/ReminderSender.cs:140-171 (comment L10-14 states it).
- Scenario: a claim row is inserted, the process dies (deploy, OOM, VPS reboot) before `channel.SendAsync` returns. The claim stays and the message is never retried, since the unique index (`UserId, Date, Kinds, Offset, Channel`) blocks it. Deploy at about 09:00 Kyiv is the likely window. A Timeout outcome is also deliberately kept as claimed (ADR-019).
- Impact: a missed deadline reminder; and the DayAfter reminder fires on that single day only (Engine/Reminders.cs:106-118), so host downtime across it loses it for good. Both are documented design tradeoffs, but nobody sees the loss.
- Fix: leave `DeliveredAt == null` claims older than 10 min eligible for re-send (delete stale undelivered claims at the start of a pass). Duplicate risk is acceptable for a reminder.

### M5. External calls: NBU has no retry; sequential 7-day probing with a 5s timeout
- Program.cs:136-141, Fx/NbuRateClient.cs:22-33,48-63.
- Scenario: NBU down or slow. Each lookup tries up to 7 days back, but an `Unavailable` result returns at once (good: no 7 x 5s stack). There is no retry, no circuit breaker. The result is a user-visible error when entering a foreign-currency receipt, or `Unavailable` in a monobank import, which leaves the whole window's FX unfetched. The owner sees an error (not silent). Rates are cached per day (FxRates), so only new dates are hit.
- Impact: low data risk; an import window may be marked as failed until NBU returns. Check MonobankStatementImport handles `Unavailable` rates by not committing the window (the cursor not advancing); not verified.
- Fix: one retry with 500ms backoff via `AddStandardResilienceHandler`/Polly (built-in, no new dependency beyond Microsoft.Extensions.Http.Resilience), or accept as is.

### M6. Telegram and monobank state is per process; overlap of two api instances
- MonobankRateGate.cs:12 (in-memory dictionary), MonobankSyncQueue.cs:14-19 (in-memory), TelegramPoller.cs:26 (`_webhookCleared`), ReminderWorker.
- Scenario (rolling deploy, or Coolify keeping the old container until the new one is healthy; verify which applies):
  - Monobank: two gates each think they own the 60s/method slot, so the second call gets 429. Handled: up to 5 attempts with Retry-After capped at 5 min (StatementImport.cs:33-35, 124-135). A transient failure is recorded as `RateLimited` on the account but cleared on the next success. Statement imports under the owner advisory lock plus the unique index (BankAccountId, ExternalId) make double import safe.
  - Telegram: both instances long-poll, so Telegram answers the older poll with 409; the code logs a warning, calls deleteWebhook once and backs off 5..60s (TelegramPollWorker.cs:13-15). Offset is stored in the DB and advanced with the effect, but both can read the same offset and process the same /start code twice (the second gets "code rejected" sent to the owner). Cosmetic.
  - Reminders: the SentReminders unique index prevents doubles.
- Impact: noise and one possible confusing "code rejected" Telegram message. No data corruption.
- Fix: none needed if Coolify recreates (stop, then start). To be sure, set `stop_grace_period: 30s` and verify the deploy mode in Coolify.

### M7. Health check is shallow; web healthcheck probes /login only
- Program.cs:287-292, docker-compose.yml:12.
- `/api/health` checks only `CanConnectAsync`. It cannot see a dead worker (a worker that threw would stop the whole host by default, so it appears as a restart rather than a silent stall; fine). It cannot see stuck sync, a rejected token or a lost key. `web` healthcheck hits `/login` over wget: passes even when the rewrite to api is broken (the api is also required healthy via depends_on, so only at start).
- No external uptime monitor or alert is documented. GET /api/health works today (200) but nothing calls it periodically.
- Fix: add an Uptime Kuma (self-hosted, free) or a GitHub Actions cron probe on `https://taxes.blonskyi.dev/api/health`, alerting to the owner's Telegram.

## Low

### L1. Logs: stdout only, retention unspecified, no alerts
- Program.cs:34 sets only a filter; appsettings.json Default Information. Logs go to the container's stdout (docker json-file via Coolify). Retention = Docker default (unbounded or whatever daemon.json says; verify) and lost when the container is recreated. There are no structured logs, error aggregation (no Sentry) or alert rules anywhere in the repo. Secrets: the monobank webhook secret path is kept out of the request log (good), and Traefik access logs are a manual "keep off" instruction (deploy.md L65-66).
- Fix: `docker logs` rotation (see M1), optional free GlitchTip/Sentry, and the uptime probe in M7.

### L2. Unbounded growth tables are small but never pruned
- AuditLog (append-only trigger, EntityId lookups, 200-row cap in the UI at AuditEndpoints.cs:12), SentReminders (about 4 per deadline, around 100 rows a year), ImportBatches (one per 31-day window per sync; the nightly sync adds one batch per account per night, since `from = min(cursor, now-31d)`: about 365 rows a year per account), Transactions/statements, FxRates (one row per currency-day). All grow linearly at single-owner scale, so 10+ years is under a few hundred MB. No retention or archival exists and none is needed soon.
- Note: the nightly sync re-reads and re-imports a 31-day window and records an ImportBatch each night even with nothing new; consider skipping the batch when the window imported 0 and skipped 0 rows.

### L3. Data-protection key ring: not backed up, not fatal
- docker-compose.yml:36-37,53-55, Program.cs:99-104, ADR-010/011.
- The monobank token is encrypted with a separate AES key from the env var MONOBANK_TOKEN_ENCRYPTION_KEY (TokenEncryptor.cs), NOT the ring, so ring loss does not touch tokens (design goal of ADR-011). Ring loss only signs the owner out, voids pending Google/passkey ceremonies and unconfirmed email links (docs say so).
- The real single point is MONOBANK_TOKEN_ENCRYPTION_KEY, which lives only in Coolify env plus "the password manager". If it is lost, Decrypt throws -> `TokenUnreadable` on the sync, and the owner reconnects with a new token. Recoverable, but silent per H3.
- The ring volume is in no backup. The ring is unencrypted at rest (ADR-10 accepts it).
- Fix: record "key ring loss = re-login" in deploy.md (done partly), and include MONOBANK_TOKEN_ENCRYPTION_KEY and every env var in the off-host secrets inventory.

### L4. What the in-app JSON backup covers and omits
- Backup/BackupDocument.cs:20-40, BackupEndpoints.cs:196-198.
- Covers: settings, clients, transactions (including dismissed), budget payments, bank accounts, import batches, payment candidates, invoicing details, invoices, declaration details, filings and files, treasury accounts, notification channels (without Address, Telegram chat id), reserve jar.
- Omits: monobank connection and token and webhook secret (by design, ADR-011), audit log (history, not state), SentReminders (a restore can re-send reminders already sent: safe), TelegramPollState, NotificationLinkCodes, CalendarFeed secret, Identity users and passkeys, TaxYearConfig and FxRates (shared; TaxYearConfig is seeded by migration, so owner edits to tax parameters and the verified flag are NOT restorable from JSON and only come from the pg dump).
- Restore: one transaction under the owner advisory lock (good), audit gets one `Restore` summary entry, it re-queues the monobank sync.
- Fix: none required; add the tax-year-parameter edit and calendar secret to the "what a backup does not hold" doc for the owner.

### L5. Time zone handling is sound, with one edge
- KyivTime.cs, Dockerfile TZ=Europe/Kyiv + tzdata, MonobankNightlySync.cs:21-26, ReminderWorker, ReminderSender.cs:24-25.
- All "today" values come from `TimeProvider` converted with `Europe/Kyiv`; the nightly 03:00 Kyiv computes the next run from DST-aware offsets. Edge: `InKyiv` for a nonexistent or ambiguous local time (DST transition) uses `GetUtcOffset`; 03:00 is a nonexistent hour on the spring-forward night (03:00 -> 04:00 in Kyiv), which resolves to an offset and fires at about 04:00 local or 00:00 UTC; acceptable. `Task.Delay` with a long TimeProvider delay of at most 24h is fine.
- A nightly worker whose `NextRun` throws would stop the host (the call is outside the try at MonobankNightlySync.cs:32-33). Cosmetic, since the inputs cannot throw.

### L6. Webhook endpoint is anonymous and unthrottled
- MonobankEndpoints.cs:262-281. Path secret = 32 random bytes hex, so guessing is infeasible. Every request does one indexed DB query and enqueues up to a few accounts; the queue is deduplicated (`_waiting`), so a flood cannot grow memory. An unknown secret returns 404 (a cheap query). No rate limit; Cloudflare in front. Fine at this scale.

## Workers: what happens on crash and a downed host

| Worker | Crash/restart | Host down 24h | Notes |
|---|---|---|---|
| MonobankSyncWorker | in-memory queue lost; on start `EnqueueUnfinishedAsync` requeues accounts with `SyncedThrough` null or older than 31 days (MonobankSyncWorker.cs:78-90) | 24h downtime: not requeued at start (only >31 days behind), but the next nightly 03:00 run or webhook/"sync now" covers it, and every sync re-reads at least 31 days; nothing is lost | A statement window is one DB transaction under the owner lock; cursor only moves on commit |
| MonobankNightlySync | recomputes the next 03:00 on start; a 03:00 missed while down is skipped (no catch-up) | covered by the 31-day re-read at the next run | no stored "last run" |
| MonobankWebhooks | `ReconcileStaleAsync` at start | bank disables the webhook after failures; the nightly run resets it | |
| TelegramPollWorker | restarts with the process; offset in DB; backoff 5..60s | Telegram holds updates 24h, so a /start link is not lost within a day | long poll 30s, per-call timeout 45s |
| ReminderWorker | first pass at start, then every 5 min; recomputed from the ledger | catch-up for week-before and day-before until the date; DayAfter only on its own day | see M4 |

## Concurrency and idempotency (data integrity)
- Per-owner `pg_advisory_xact_lock(hashtext(userId))` serializes restore, prototype import, monobank window import, transaction create and payment-candidate confirm (5 call sites: grep `pg_advisory`). The hashtext collision across two users is irrelevant for one owner.
- Unique indexes: Transactions (BankAccountId, ExternalId), BudgetPayments (same), candidates, foreign debits, bank accounts (UserId, Bank, ExternalId), Clients (UserId, Name), Invoices (UserId, NumberYear, NumberSequence), TreasuryAccounts, SentReminders, webhook secret, calendar secret. Dismissed rows act as tombstones so re-sync cannot resurrect them.
- Audit: a SaveChanges interceptor logs 12 entity types; `ExecuteUpdate`/`ExecuteDelete` bypass it and are used on non-audited paths (BankAccount cursor, ImportBatch counts, MonobankConnection) and on the restore's bulk delete (replaced by one Restore summary row). The append-only trigger rejects UPDATE/DELETE. Not audited: ImportBatch, SentReminder, monobank connection, FxRates, DeclarationFile, ReserveJar, MonobankConnection token changes (connect/disconnect/reject are not in the audit log).

## Checked and fine
- Startup guards fail fast on missing config (Program.cs:46-85) and `init: true` makes the container exit instead of staying unhealthy (compose L19-21).
- Migrations: EF Npgsql advisory migration lock, transactional per migration; the api serves nothing until MigrateAsync completes, so health is not green on a half-migrated schema.
- /api/health returns 503 when the DB is unreachable; Dockerfile and compose healthchecks have start_period 20s, retries 3.
- The monobank rate gate, with the Retry-After capped at 5 min and 5 attempts per window, and a window that does not commit on any failure (cursor only advances on commit); the sync is idempotent by external id.
- Timeouts everywhere: NBU 5s, monobank 10s, Telegram 15s/45s long-poll with a linked deadline, SMTP 25s overall; all clients return typed results, not exceptions; transient/permanent failure split; Telegram 429 `retry_after` honored (<=30s).
- Reminder double-send protection across replicas via the unique claim index; Telegram offset persisted in the same save as the effect.
- Tokens, the bot token and SMTP password never logged; the Telegram client has request logging removed; the webhook request line is filtered out.
- Time zone: container TZ and explicit Europe/Kyiv conversion everywhere; reminders are time-zone and DST safe.
- Restore is transactional, lock-protected, preserves issued invoice numbers (refuses a file that drops them), and re-queues the sync.
- CI gates the deploy on tests, e2e (stack with Compose), image builds and compose checks; concurrency cancels stale runs.
- The deploy webhook uses POST with a bearer token from secrets and validates the URL shape.

---

## taxes-ua architecture and code-quality audit (main @ 39ce9bd, 2026-10-02)

Read-only audit. All paths relative to /Users/mykola/workspace/taxes-ua.

## Constraints that HOLD (checked, no violations)
- Engine purity: Engine.csproj has no PackageReference; no DateTime.Now/UtcNow, decimal, double or float in api/src/TaxesUa.Engine. TreatWarningsAsErrors is on via api/Directory.Build.props (applies to all projects, not only Engine).
- Tax parameters: only the 2026 seed in api/src/TaxesUa.Api/Features/TaxYears/TaxYearConfigConfiguration.cs:21-45 (legitimate seed, Rule 9). Engine takes every rate, day count and threshold from TaxYearConfig. No rate/date literals in web.
- Kyiv dates: single helper api/src/TaxesUa.Api/KyivTime.cs; no stray DateOnly.FromDateTime(DateTime.*).
- Money: long kopecks, int RateE4. decimal appears only at the NBU parse boundary (Fx/NbuRateClient.cs:35); double only in PDF layout.
- Web layering: eslint zones in web/eslint.config.mjs plus grep found zero upward or sideways imports and no relative-path bypass.
- API types: web/src/data/api/schema.d.ts is openapi-typescript output; every exported type in web/src/data is an alias of components["schemas"][...].
- Package versions: only in api/Directory.Packages.props; no csproj carries Version.
- Dead code is minimal: all 77 OpenAPI paths are referenced from web; i18n uk/ru both 1131 keys, identical sets, no unused keys found (loose check).

## HIGH

### H1. Documented anti-forgery control does not exist
docs/architecture.md:313 says "Anti-forgery for cookie auth via the X-Requested-With header and SameSite". grep of api/src and web/src finds no X-Requested-With check, no antiforgery middleware, no Origin/Sec-Fetch-Site check. The only defence is SameSite=Lax (Program.cs:240). Lax blocks cross-site POST bodies, but any state-changing endpoint reachable by a top-level GET, or a form-encoded POST from a same-site sibling subdomain, is unprotected.
Fix: add a small middleware on /api/* rejecting non-GET/HEAD requests whose Sec-Fetch-Site is not same-origin/none (or Origin not in ALLOWED_HOSTS), plus a test; or delete the claim from the doc. Decide which, do not leave the claim.

### H2. The "compiler enforces feature boundaries" claim is false; features form a dense cycle graph
docs/architecture.md:183 and ADR-008 (docs/decisions.md:233-290) say `internal` makes the compiler refuse cross-feature reach. `internal` is assembly-wide and the whole api is one assembly, so it enforces nothing. `using TaxesUa.Api.Features.X` scan shows ~70 cross-feature edges and real cycles: Periods<->Payments, Transactions<->Invoices, Transactions<->Monobank, Payments<->Monobank, Settings<->Transactions, Settings<->Monobank, Payments->Settings->Transactions->Payments. Backup/Audit/Dashboard touch 8-11 features each (Backup and Audit by design; Dashboard is not an exception).
Fix: either (a) add a cheap architecture test (NetArchTest-free: a reflection/`using`-scan xunit test with an explicit allowed-edges list) so new edges are a conscious act, and rewrite the doc to say "convention plus test", or (b) split Features into projects. (a) fits the "boring" principle. Also extract the shared pieces that cause cycles (TextRules-style helpers, ClientRules/TransactionsEndpoints.MaxClientNameLength coupling at Transactions/ClientRules.cs:53, PaymentsEndpoints.Apply used from BackupDocument.cs) into a Shared/ folder.

### H3. Error contract is free-text English; the web parses it with substring matching
Of ~73 Results.Problem calls only two (Declarations/DeclarationsEndpoints.cs:283, :309) carry a machine-readable `reason` extension, and nothing in web reads it (grep for QuarterNotEnded/availableFrom in web/src is empty). Validation messages are English sentences, repeated ~20 times with different wording ("must not contain a control character" in Settings/DeclarationDetailsEndpoints.cs:122,149; Settings/InvoicingEndpoints.cs:361; Payments/PaymentsEndpoints.cs:269; Payments/TreasuryAccountsEndpoints.cs:315; Invoices/InvoiceRules.cs:153,264; Transactions/ClientRules.cs:114; Transactions/TransactionsEndpoints.cs:776; TaxYears/TaxYearEndpoints.cs:72; 10 in Backup/BackupDocument.cs). The web translates them by `message.includes("exceed" | "control character" | "must not repeat" | "must start with UA" ...)` in 4 places: web/src/shared/lib/ibanProblem.ts:13-21, web/src/features/settings/components/treasuryErrors.ts:6-10, invoicingFormState.ts:77-93, DeclarationDetailsForm.tsx:31,85. Rewording one server sentence silently breaks a Ukrainian/Russian message. ApiError (web/src/data/api/client.ts:6) drops ProblemDetails extensions.
Fix: one `ApiProblems` helper in api that adds `extensions.code` (closed set: e.g. TooLong, ControlChar, Required, Duplicate, NotFound, QuarterNotEnded) and, for validation, a code per field error; add `code` to ApiError; replace the four includes() tables with a code->i18n-key map. Route all TextRules-based checks through one function that returns the code. Incremental: start with the 3 repeated families (length, control char, required).

## MEDIUM

### M1. Two giant Map*Api methods with inline lambdas
Invoices/InvoicesEndpoints.cs:18-678 (MapInvoicesApi is one 660-line method) and Transactions/TransactionsEndpoints.cs:37-393 (356 lines). Hard to review, handlers cannot be unit-tested or navigated. Fix: one private static method per route (`static async Task<IResult> Issue(...)`) and have Map* just list routes. Do it when next touching those files, not as a standalone refactor.

### M2. Advisory-lock key copy-pasted in 5 places
`SELECT pg_advisory_xact_lock(hashtext({userId}))` at Payments/PaymentCandidatesEndpoints.cs:290, Transactions/TransactionsEndpoints.cs:394, Monobank/MonobankStatementImport.cs:218, Backup/ImportEndpoints.cs:112, Backup/BackupEndpoints.cs:218. Restore/import/sync mutual exclusion is correct only while all five stay byte-identical. Fix: one `OwnerLock.AcquireAsync(db, userId, ct)` in a shared file; the doc comments ("the restore's lock") then point at one symbol.

### M3. `Missing(id)` 404 helper copied 7 times, mixed 404 shapes
Periods:188, PaymentCandidates:292, Payments:220, TaxYears:157, Invoices:744 (+Frozen/MissingReceipt 751-758), Transactions:396, Clients:206 each build a ProblemDetails 404; but Results.NotFound() (empty body) is used in Settings/InvoicingEndpoints.cs:135, Payments/TreasuryAccountsEndpoints.cs:60,101,145, Calendar:99, Declarations:254,269. Web expects a title. Fix: `Problems.NotFound(string what, object id)` and use it everywhere except the two secret-path endpoints (Calendar, monobank webhook), where an empty 404 is deliberate.

### M4. DateTimeOffset.UtcNow in 11 places while the rest injects TimeProvider
Payments/PaymentsEndpoints.cs:79,122,160; TaxYears/TaxYearEndpoints.cs:109; Auth/AuthEndpoints.cs:102; Transactions/TransactionRecorder.cs:50; Transactions/TransactionsEndpoints.cs:193,257,308; Monobank/MonobankEndpoints.cs:331,385. These write CreatedAt/UpdatedAt/VerifiedAt and cannot be driven by the fake clock, so tests cannot assert timestamps and the audit snapshot times differ from `time.GetUtcNow()` used elsewhere. Fix: inject TimeProvider into those handlers (already registered Program.cs:136).

### M5. Docs drift (see section "Docs vs code" below): TODO.md/plans stale, ADR statuses stale, ADR-008 false claim.

### M6. BackupDocument.cs is 1616 lines holding 14 records
Features/Backup/BackupDocument.cs:24-1616. Schema is at version 15 with a chain of UpgradeFromVersionN methods (lines ~98+) in the same file as every record's From/ToEntity/validation. Splitting is warranted: one file per record (TransactionBackup.cs, InvoiceBackup.cs ...) and Upgrade chain in BackupUpgrades.cs. Keep the existing reflection test (BackupEndpointsTests.cs:947) that checks every table is covered. Also drop ticket-number change history from the comment at :39-49; git has it.

### M7. CI does not catch schema.d.ts drift or a stale OpenAPI contract
.github/workflows/ci.yml runs lint/build/tests, but nothing regenerates web/src/data/api/schema.d.ts and diffs it. A handler change that forgets `pnpm gen:api` ships a stale generated type (6624 lines checked in). Fix: add a CI step: start api in Development, run `pnpm gen:api`, `git diff --exit-code src/data/api/schema.d.ts` (or emit openapi.json via a test and compare).

### M8. In-memory single-instance state (bank sync) is a deliberate but undetected limit
MonobankSyncQueue, MonobankRateGate, MonobankClientInfoReader live in process memory (architecture.md bank sync, ADR-021). Nothing fails if a second api replica starts (Coolify scale, rolling deploy overlap): two gates -> bank 429s, double sync. Fix: Compose `deploy.replicas: 1`/`stop_first` update order, plus a startup advisory lock for the hosted workers (pg_try_advisory_lock) so a second instance idles.

## LOW

- L1. Mixed web version pinning in web/package.json: next, react, next-intl, vitest-adjacent pinned exactly; most others `^`. Pick one policy (lockfile already pins).
- L2. Naming drift: API folder `TaxYears` + `TaxYearEndpoints` (singular) vs plural `*Endpoints` elsewhere; `Settings/` folder owns InvoicingEndpoints and DeclarationDetailsEndpoints (invoicing is its own web data module `data/invoicing`); `Transactions/ClientsEndpoints.cs` and `Payments/TreasuryAccountsEndpoints.cs` live in other features' folders; web feature `declaration` (singular) vs `data/declarations` and API `Declarations`. `ReceiptOption` exported from both data/invoices/useInvoices.ts:18 and data/transactions/useTransactions.ts:14.
- L3. Duplicated limits: MaxAddressLength=500 in Settings/DeclarationDetailsEndpoints.cs:13, Settings/InvoicingEndpoints.cs:26, Transactions/ClientRules.cs:55; MaxNameLength 200 duplicated. Move to TextRules.
- L4. Dead bits: Engine NbuQr.MaxBytes (NbuQr.cs:24, no reference); unused exported TS types PaymentMatch (data/payments/usePayments.ts:14), DeclarationDetailField (data/declarations/useDeclarations.ts:14), DeliveryFailure (data/notifications/useNotificationChannels.ts:8). Untracked stray file reserve-card-ru-375-surplus.png in repo root.
- L5. graph/architecture.md and graph/dependencies.md are empty templates. Either fill from this audit's edge list or delete; CLAUDE.md tells readers to use them.
- L6. docs/architecture.md:36 and .claude/CLAUDE.local.md claim "TanStack Query/Table/Form" but only react-query is a dependency (no table, no form). docs/architecture.md:250 lists web `shared/types/` which does not exist; the feature list at architecture.md ~170 omits Calendar, Monobank, Notifications.
- L7. Comment density: Engine 28% comment lines (502/1805), Api features 8%, web 4.5%. Content is mostly "why" (good), but several comments narrate ticket history ("added by #76", "#102") which rot; keep rule text, drop ticket changelogs.

## Large files (>600 lines, non-generated)
| File | Lines | Verdict |
|---|---|---|
| api/.../Backup/BackupDocument.cs | 1616 | split (M6) |
| api/.../Invoices/InvoicesEndpoints.cs | 856 | split handlers out of one method (M1) |
| api/.../Transactions/TransactionsEndpoints.cs | 845 | split: keep routes; move Validate/ApplyAmount/ValidateLinks (lines ~416-780) to TransactionRules.cs |
| api/.../Settings/InvoicingEndpoints.cs | 678 | split Validate/IBAN/signature helpers (349-500) to InvoicingRules.cs |
| api/.../Monobank/MonobankStatementImport.cs | 640 | borderline; cohesive pipeline, fine unless it grows, consider extracting suggestion code (520-606) |
| tests: BackupEndpointsTests 1585, MonobankSyncTests 890, TelegramChannelTests 698, TransactionsEndpointsTests 662, BalancesTests 639, DeclarationsEndpointsTests 605 | | fine (one scenario per test); BackupEndpointsTests could be split by concern |
| generated: schema.d.ts 6624, Migrations/*.Designer.cs, AppDbContextModelSnapshot 1814 | | fine, generated |
No web source file exceeds 600 (largest DraftEditor.tsx 493).

## Docs vs code: 10 claims spot-checked
TRUE: (1) reminders every 5 min (ReminderWorker.cs:13); (2) retries 1/2/4 s, retry_after cap 30 s (ChannelDelivery.cs:45,48), SMTP 25 s deadline (EmailTransport.cs:35); (3) monobank gate 60 s, 31-day window (MonobankRateGate.cs:10, MonobankStatementImport.cs:29); (4) AuditLog append-only trigger (Migrations/20260927092453_AddAuditLog.cs:50-56); (5) OpenAPI served only in Development (Program.cs:301); (6) 2026 seed values equal Rule 3/4/5 (22% x 8,647, limit 1167 min wages, ESV day 19, 40+10 days); (7) data-protection volume (docker-compose.yml:44); (8) calendar feed carries no amounts; (9) reminder moments 09:00 Kyiv (Engine/Reminders.cs:71); (10) no session store / security stamp (ADR-009).
FALSE now:
- architecture.md:313 anti-forgery via X-Requested-With: not implemented (H1).
- architecture.md:183 + ADR-008: `internal` enforces feature boundaries: it does not (H2).
- ADR-002 through ADR-008 (docs/decisions.md:46,75,107,129,164,212,237) still "Status: Proposed" though all implemented; ADR-005's "passkey as second method" is shipped.
- docs/TODO.md and plans/current.md show "#2 In Progress, #3 Review" and "Stage 2 backlog" while main is at #168 with Stage 2/3 features merged.
- architecture.md:36 TanStack Table/Form; :250 shared/types/ (L6).
- architecture.md "Dependencies: ... later the Telegram Bot API, SMTP, bank APIs" (line ~59) is already shipped.

## Five riskiest modules (complexity x importance)
1. Backup/restore/import (Features/Backup, ~2900 lines). Touches 14+ tables, 15-step JSON schema upgrade chain, ExecuteDelete order with RESTRICT FKs, bypasses the audit interceptor by design, relies on the duplicated advisory lock (M2). A new entity that is forgotten silently loses data on restore (guarded only by one reflection test). Any schema change needs 5 coordinated edits.
2. Monobank import and sync (Features/Monobank, ~2200 lines). In-memory queue/gate/cache (M8), per-owner token epochs, webhook-as-signal, nightly sync, 31-day windowing, ChangeTracker.Clear inside a locked transaction (MonobankStatementImport.cs:~225), mixed ExecuteUpdate vs tracked entities (audit bypass for BankAccount by design). Concurrency and external-failure heavy; bugs here create or lose income rows.
3. Money ledger: Engine Accruals.cs (351) + Balances.cs (299) + IncomeLedger + TaxReserve + the API glue Periods/YearAccruals. Rule 3/4/7 interplay (cumulative quarters, limit-crossing, registration-month proration, oldest-debt-first allocation, 1 kopeck order-of-rounding cases). Pure and well tested, but any rule change ripples through dashboard, reminders, declaration, reserve. Highest correctness stakes (tax figures).
4. Reminders and delivery (Notifications ~2000 lines + Engine/Reminders.cs). Claim-before-send log, "possibly sent" vs "released" semantics, per-channel retry classes, quiet restart behaviour, 5-minute worker. Correctness depends on exact classification of failures (EmailTransport.cs:97 maps 4xx to ServerError, a naming trap). Failure mode is silent: a missed or doubled deadline reminder.
5. Transactions + Invoices (Transactions/TransactionsEndpoints.cs 845, Invoices/InvoicesEndpoints.cs 856, InvoiceRules, TransactionRecorder). Largest handlers (M1), FX lookup and rounding, refund-to-receipt links, dismissed tombstones with global query filter (forgetting IgnoreQueryFilters yields wrong totals or duplicate imports), frozen invoice snapshots/numbering, cycles with Monobank/Payments. Most-edited code, most regressions likely.
Honourable mention: Declarations (DpsXml.cs + stale-file rules, Rule 15) - schema-bound XML, tested.

## Dependency hygiene
- API: no outdated majors for runtime packages (Identity/EF/Npgsql/OpenApi 10.0.x; OpenApi 10.0.11 vs 10.0.12 patch lag in Directory.Packages.props:2). Test-only majors behind: coverlet.collector 6.0.4 -> 10.1.0, Microsoft.NET.Test.Sdk 17.14.1 -> 18.10.1, xunit.runner.visualstudio 3.1.4 -> 4.0.0, xunit still v2 (2.9.3). Low priority; bump together with xunit v3 when convenient. All runtime packages are used (MailKit, OpenXml, PDFsharp, Google, Identity); Ical.Net and PdfPig are test-only and referenced only by tests (correct).
- Web: no outdated majors (pnpm outdated shows only patches: next 16.3.6->.8, next-intl, shadcn, react-query minor). `cn` is shadcn's own package (legit), not a squatted name. No unused runtime deps found; esbuild (package.json:45) has no direct import (likely a serwist peer/pin; verify before removing). `shadcn` is a runtime dependency only for `@import "shadcn/tailwind.css"` (globals.css:3), fine.
- Note: this checkout's web/node_modules is incomplete (pnpm reports vitest, playwright etc. "missing"); run `pnpm install` before running tests locally.

---

## Tests and CI audit, taxes-ua @ main 39ce9bd (2026-10-02)

Inventory: Engine 241 tests (20 files), Api 637 [Fact/Theory] methods (1104 cases after theories; 36 IClassFixture classes, one Postgres container each), web 194 vitest cases in 17 files, 9 Playwright specs (workers 1, retries 0).
Method note: coverage map is by test names and keyword greps, not a coverage run. "Unverified" means I found no test by keyword and did not read every file.

## Findings, ranked

### S1 (high) Confirmed flaky API test; root cause is shared DB plus live background workers
- Evidence: SHA ead095bd failed in the pull_request run 36956674918 and passed in the push run 36956673233. Failing test: `MonobankSyncTests.A_token_monobank_rejects_on_prefill_is_a_conflict_so_the_owner_connects_again` (api/tests/TaxesUa.Api.Tests/Features/Monobank/MonobankSyncTests.InvoicingPrefill.cs:159).
- A later commit patched it with a comment (InvoicingPrefill.cs:147-150) saying an unfinished sync from an earlier test leaked into this owner. That fixes one test, not the cause.
- Cause: each test builds its own WebApplicationFactory (ApiFixture.cs:57 `CreateApplication`) against one class-wide database and one owner (`ApiFixture.AllowedEmail`). Hosted sync workers of an earlier app keep running against that database.
- Fix: give every Monobank/Notifications test its own owner email (a unique `user-{guid}@example.com` added to the allowlist), or make `SyncApp.DisposeAsync` await worker drain before the next test starts. Add `[Trait("flaky-watch")]` plus a CI step that reruns the Monobank class 5x nightly to catch regressions.

### S2 (high) No test of the migration path
- api/src/TaxesUa.Api/Program.cs:344 runs `MigrateAsync()` on every start, including prod. 55 migration files. Every test starts from an empty database through the same call, so only "empty to latest" is exercised.
- Nothing tests "previous release schema with data to latest". A bad data migration would hit the owner's real data at deploy, and deploy fires automatically.
- Fix: add a test-only job that restores a pg_dump of the previous release (or the schema at the last tag) with seeded rows, runs `MigrateAsync`, then exercises `/api/backup` and `/api/dashboard`. Also assert `dotnet ef migrations has-pending-model-changes` in CI so the model and the migrations cannot drift.

### S3 (high) The deploy has no post-deploy check and no rollback signal
- .github/workflows/ci.yml:116-144: the job only POSTs the Coolify webhook and checks HTTP 200 (the webhook accepting the job), not that the new build is healthy.
- It cannot deploy an untested commit: `needs` lists all 7 jobs on the same push SHA, `if` restricts to push on main. Good.
- Gaps: (a) `docker-api` and `docker-web` are not in the required status checks (branch protection lists API tests, Web lint & build, Deploy checks, Web tests, End-to-end tests), so a PR can merge with a broken image build and then simply never deploys; (b) `strict:false`, so the PR run tested the branch, not the merge result; the post-merge push run does test the real commit before deploy, which is the saving grace; (c) concurrency `cancel-in-progress` at ci.yml:8-10 can cancel a main run mid-way, so rapid merges skip deploys of intermediate commits (acceptable, but the last one must be confirmed).
- Fix: add a final step polling `https://<prod>/api/health` until the new version reports the commit SHA (needs a version endpoint) and fail the job otherwise. Add `Build API image` and `Build web image` to required checks. Add `concurrency: {group: deploy, cancel-in-progress: false}` on the deploy job.

### S4 (medium-high) Negative waits that pass vacuously, and wall-clock polling
- api/tests/.../Features/Monobank/MonobankSyncTests.cs:325 `Task.Delay(100)` then asserts no further statement calls. If the worker is slow the assertion holds trivially, so a regression that polls late is missed. Same pattern: MonobankSyncTests.Webhook.cs:25,106,195; TelegramChannelTests.cs:206 (`Delay(300)` then "nothing polls"); ReminderTests.Channels.cs:143.
- Positive polls use real deadlines (TelegramChannelTests.cs:184-190, ReminderTests.Channels.cs:133-137, 15 s of wall time on a loaded runner with 36 parallel containers). `Drain` (MonobankSyncTests.cs:662,682) advances the fake clock 5 s per `Delay(10)` and bounds by step count, which is the right design but still ties correctness to scheduler speed.
- Fix: replace negative sleeps with a deterministic barrier (the stub handler counts calls and the test awaits the worker going idle via a `TaskCompletionSource` exposed by the worker), and keep one shared `WaitUntil(condition, FakeTimeProvider)` helper.

### S5 (medium) Tests whose name claims more than they check
- `A_row_dated_today_in_kyiv_is_accepted` (TransactionsEndpointsTests.cs:283-295): no assertion in the body beyond whatever `Create` asserts internally; it also uses the real clock (`TimeProvider.System.TodayInKyiv()`), so it straddles midnight Kyiv. Make it assert 201 and the stored date, and run it on a fake clock.
- `A_receipt_dated_after_today_in_kyiv_is_rejected` (same file :269-280): real clock too. Same fix.
- `Year_total_excludes_non_income_and_pre_registration_rows` (:219) hardcodes `year = 2026` and asserts an exact total in a database shared with other tests in the class; the comment at :287-289 shows the class already relies on every other test avoiding 2026 income rows. Order-independence is by convention, not enforced. Fix: dedicated owner or year (as the 2040/2095 classes do).
- web/e2e/nbu-stub.spec.ts:1-9 tests the stub, not the app. Fine as a guard, but it counts as a "test" with no product behaviour.
- web/e2e/invoice-pdf.spec.ts:23-24 asserts only that the bytes start with %PDF- and end with %%EOF. A blank or wrong invoice passes. Parse the text (the Api project already has PdfPig tests; keep content checks there and say so in the spec name).
- web/src/shared/ui/nbu-qr.test.tsx:69-71 asserts only finder-pattern corners are dark; payload correctness rests on the engine's golden NbuQrTests, which is fine, but the test name should say "draws finder patterns".

### S6 (medium) Real-clock and calendar dependence
- web/e2e/support/api.ts:30-50 `seedRegisteredOwner` derives the year and quarter from the server's today and registers the owner on 1 January of the current year. Specs therefore behave differently by quarter (the first-quarter breakage of #163 is the precedent, e2e/support/years.ts). The suite is green today, but around 1 Jan, 1 Apr and quarter ends it exercises different branches. Fix: let the e2e stack run with a pinned clock (`App:FixedToday` or a TimeProvider env override) and run the suite once per quarter in a nightly matrix.
- e2e/income.spec.ts:13-19 uses `randomInt` so failures are not reproducible from the log; print the seed.
- Web side is sound: dates.test.ts:75-92 pins instants across DST in America/Los_Angeles, which is exactly right. XmlFile.test.tsx:82 and copy-field.test.tsx:65-70 compute from `Date.now()` under fake timers, so they are deterministic.
- PayDebtButton.test.tsx:61-90 steps the fake clock 1 ms up to 200 times inside a `settle` loop; it works but is a polling loop in disguise. Prefer `await vi.waitFor` with fake timers off for the fetch part.

### S7 (medium) Business-rule coverage gaps (see map below)
- Rule 4 "Exceeded does not depend on warn thresholds" is covered; 85 percent boundary is covered (LimitMonitorTests).
- Q4 deadlines shifting against the quarter year's holidays (Rule 5, "to confirm"): `Q4_deadlines_fall_in_the_following_year` exists but I found no test with a holiday in the following January, which is exactly the case where the two readings differ. Add one, so the owner's decision flips a red test, not silence.
- Rule 12: 429 handling has a helper (MonobankSyncTests.cs:869) and a 429 on the gate (InvoicingPrefill.cs:111-112), but I found no test named for the 5-minute `Retry-After` cap or the "five 429s in a row stops the sync" rule. Unverified; check MonobankSyncTests.cs near 860-890.
- Rule 12 "startup re-queues followed accounts whose cursor is missing or older than 31 days": no test found by keyword (resync/on_start). Unverified.
- Rule 12 FxSale matching (60 s window, 5 percent, maximum matching, reordering across windows): covered by Candidates/Review files but only by example tests; this is the one place a property/randomised test (as BalancesTests already does with `Random_ledgers...`) would pay.
- Rule 10: no engine-level test for a bank UTC timestamp at 23:30 to the next Kyiv day by name (the web has it). Unverified in Api tests.
- Backup restore: schemaVersion 1 and 15 are tested; versions 2 to 14 are not. Add one fixture file per version bump (cheap, JSON).
- Rule 14 to 17: files exist for each (InvoicesEndpoints/InvoicePayments/InvoiceMatcher/InvoicePdf; F0103309 + Declarations + EsvAnnex + quarter-end; PaymentDetails/NbuQr/PaymentPurpose; ReminderPlan/ReminderTests/Telegram/Email). Not read line by line.

### S8 (medium) CI efficiency and structure
- Durations (run 36991905088, all green): API tests 7m25s wall (`dotnet test` 6m32s for 1104 tests, Engine 0.7 s), e2e 2m50s, Deploy checks 1m38s, Web tests 32 s, Web lint & build 43 s, docker-web 47 s, docker-api 40 s, deploy 5 s. Critical path = API tests, then docker-api, then deploy, about 8 min.
- Duplicate runs: `on: push` plus `pull_request` (ci.yml:3-5) runs everything twice per commit on a PR branch (different concurrency groups). Halve the load: `push: branches: [main]`. Side effect to know: the S1 flake was only visible because of this duplication.
- No caching anywhere: NuGet (setup-dotnet has no `cache: true`; needs packages.lock.json or `cache-dependency-path: api/Directory.Packages.props`), pnpm disabled in three jobs (`package-manager-cache: false`, ci.yml:36,56,76; use `actions/cache` on the pnpm store or `pnpm/action-setup`), Playwright browsers (ci.yml:80 re-downloads chromium), Docker layers (api image built by docker-api, deploy-checks and the e2e stack; web built three times; add `docker/build-push-action` with `cache-from: type=gha`).
- Parallel and sharding: the API project is already parallel across classes (xunit 2.9.3 default, no xunit.runner.json) with 36 Postgres containers started at once, which costs memory and contributes to S1/S4 timing. The long pole is the single class `MonobankSyncTests` (partials, about 2,700 lines, serial inside the class, with Task.Delay drains). Options: (1) split it into 3 to 4 classes by partial file (cheap, parallelises automatically); (2) matrix shards `dotnet test --filter "FullyQualifiedName~Monobank"` vs the rest; (3) set `maxParallelThreads` in xunit.runner.json to limit container storms. Expect 7 min to about 3.5 min.
- Typecheck: no `pnpm typecheck` step. It happens implicitly in `pnpm build` (next build type-checks the tsconfig include, which covers *.test.tsx and e2e/*.spec.ts). So tests are typechecked, but only inside the 43 s lint-and-build job and only after a successful `next build` type pass. `LayoutProps<"/">` is used at src/app/layout.tsx:41 and src/app/(app)/layout.tsx:6; this global type is generated into `.next/types` by Next, so a bare `tsc --noEmit` on a clean checkout fails (and locally also needs node_modules). Fix: add `pnpm exec next typegen && pnpm typecheck` as a CI step so type errors report separately from build errors. My local `tsc` run was noise (missing vitest/playwright installs), not a CI finding.
- e2e job: `timeout-minutes: 15`, globalTimeout 12 min, retries 0 (a deliberate choice documented at playwright.config.ts:26). Good. Report uploaded on failure only. Keep.
- API test job has no `timeout-minutes` (default 6 h) and no test result artifact (trx) on failure; the S1 failure had to be recovered from raw logs. Add `--logger "trx"`, upload on failure, and `timeout-minutes: 20`.
- Required checks (main protection, strict false): API tests, Web lint & build, Deploy checks, Web tests, End-to-end tests. Missing: the two image builds.

### S9 (low-medium) Mocking and assertion quality in web tests
- Over-mocking is mild: component tests use a typed `stubFetch` against the generated schema with real catalogs (web/src/test/README.md), and fail on unplanned requests. This is a strong design. Risks: README itself notes that shared fixtures bypass the deep-partial type check, so a renamed API field can leave component tests green while e2e is the only gate.
- `vi.spyOn(HTMLAnchorElement.prototype, "click")` in XmlFile.test.tsx:42 and `navigator.clipboard` spy in copy-field.test.tsx:76 stub browser behaviour; acceptable, but the download itself is only proven by e2e/declaration-xml.spec.ts.
- Weak assertions seen: `expect(hasCode()).toBe(true)` pattern (PayDebtButton.test.tsx:73,86) hides which element is missing; use `expect(await screen.findByRole("img", ...)).toBeVisible()`.
- Server Components are untestable in Vitest (ADR-020) and covered only by e2e; the 375 px layout gate (layout.spec.ts, 279 lines) is the strongest e2e test. Good.

## Coverage map (business-rules.md -> tests)

| Rule | Engine | Api | Web / e2e | Gaps |
|---|---|---|---|---|
| 1 Income | IncomeLedgerTests | TransactionsEndpointsTests (future date, refunds, totals) | income.spec | real-clock tests (S5) |
| 2 FX rate | MoneyTests (rounding) | FxEndpointsTests, NbuRateClientTests, CurrencyReceiptsTests | nbu-stub.spec | weekend fallback covered in Fx tests; manual rate marker not checked |
| 3 Rates, ESV | AccrualsTests (FullMonth vs Prorated, exempt, cumulative) | SettingsEndpointsTests, PeriodsEndpointsTests | TaxYearTable.test | good |
| 4 Limit, crossing | LimitMonitorTests, LimitCrossingTests, LimitCrossingAcrossYearsTests | LimitCrossing*EndpointsTests | ReserveCard.test | strong; the best covered rule |
| 5 Deadlines, calendar | DeadlineCalendarTests | CalendarFeedTests, IcsDocumentTests, PeriodsEndpointsTests | none | Q4 with a holiday in following January (S7) |
| 6 Payment modes | MonthlyAdvancesTests | MonthlyAdvancesEndpointsTests | none | good |
| 7 Balances | BalancesTests (incl. random ledgers), PaymentsOutsideGroup3Tests | PaymentsEndpointsTests, PeriodsEndpointsTests | TaxYearTable.test | good |
| 8 Registration | IncomeLedgerTests (transitive refunds) | TransactionsEndpointsTests | none | good |
| 9 Year parameters | none | TaxYearEndpointsTests (VerifiedAt) | TaxYearTable.test | fine |
| 10 Money, dates | MoneyTests | partial | dates.test, money.test | Kyiv 23:30 UTC import boundary unverified in Api |
| 11 Disclaimer | none | none | language.spec only | trivial |
| 12 Bank import | InvoiceMatcherTests (not this rule) | MonobankSyncTests.* (8 partials), MonobankNightlySyncTests, MonobankRateGateTests, MonobankClientInfoReaderTests, TokenEncryptorTests, TreasuryPaymentTests, TreasuryAccountsEndpointsTests | PaymentCandidates, EmailChannel, ReserveJarSection tests | Retry-After cap, five 429s, startup re-queue (unverified); FxSale matching has no randomised test; flaky class (S1) |
| 13 Reserve | TaxReserveTests, TaxReserveCoverTests | TaxReserveEndpointsTests(.Jar), DashboardEndpointsTests | ReserveCard.test, ReserveJarSection.test | good |
| 14 Invoicing | InvoiceMatcherTests | InvoicesEndpointsTests, InvoicePaymentsTests, InvoicePdfTests, InvoicingEndpointsTests | InvoiceSuggestion.test, invoice-pdf.spec (weak, S5) | PDF content only checked in Api |
| 15 Declaration | DeclarationTests, EsvAnnexTests, DeclarationFileAvailabilityTests | F0103309Tests (golden + xsd), DeclarationFilesEndpointsTests, DeclarationFileQuarterEndTests, DeclarationsEndpointsTests | XmlFile.test, declaration-xml.spec | good |
| 16 Paying | NbuQrTests, PaymentPurposeTests | PaymentDetailsEndpointsTests, PaymentsEndpointsTests | pay-panel.spec, PayDebtButton, PeriodSelect, nbu-qr, pay-panel tests | good |
| 17 Reminders | ReminderPlanTests | ReminderTests (+Channels/Content/Email), TelegramChannelTests, EmailChannelTests, SmtpEmailTransportTests | NotificationsSection, EmailChannel tests, settings-channels.spec | wall-clock negative waits (S4) |
| Backup/import (ADR) | none | BackupEndpointsTests (1585 lines), ImportEndpointsTests | none | versions 2-14, migration path (S2) |
| Auth | none | AuthEndpointsTests, PasskeyTests, EmailAllowlistTests, GoogleClaimMappingTests, DevelopmentSignInTests | sign-in-gate.spec | OK |

## Missing test types worth adding (ranked by value per cost)
1. Migration upgrade test (S2): previous-release dump to latest, plus pending-model-changes check. High value, one afternoon.
2. Backup compatibility fixtures for schemaVersions 2 to 14 and a "backup from prod-like data then restore into fresh DB then compare dashboard/periods" round trip. Medium cost.
3. Post-deploy smoke against production (S3) and a nightly e2e with pinned clock per quarter (S6).
4. A rerun-5x flake-watch job for the Monobank and Notifications classes (S1), until the leak is fixed.
5. Property tests for the engine ledger (already one in BalancesTests; extend to FxSale matching and limit crossing with refunds): FsCheck is a dependency, so only if the owner accepts one more; plain seeded `Random` as BalancesTests does is enough.
6. Mutation testing (Stryker.NET on TaxesUa.Engine only, 241 fast tests, runs in minutes): worthwhile as a one-off to find assertions that don't bite; not as a CI gate.
7. Load testing: skip. Single-owner app.
8. Accessibility check (axe via Playwright on the 375 px pass): cheap add to layout.spec.

## History (last 40 runs)
31 success, 5 failure, 4 cancelled; zero reruns (all attempt 1). Failures: 4 were deliberate or in-progress on feature branches (the TEMPORARY wide-element proof for #154 and the #153/#154 e2e work), 1 real flake (S1, SHA ead095bd). Cancelled runs are concurrency cancellations, not failures. Main has been green on every push in the sample.

---

## UX / a11y / i18n / PWA audit (code-only; app not run)

Method: static read of web/src, messages, manifest, service worker. No stack was started, so contrast and tap sizes below are computed from tokens and class names, not measured in a browser.
Line numbers are for web/ paths on main at 39ce9bd.

## High

1. Declaration guide tells the owner to import XML, which the Cabinet cannot do.
   - Where: messages/{uk,ru}.json `declaration.xml.hint`, `.annexHint`, `.guide.import` (uk.json ~1266-1286); src/features/declaration/components/XmlFile.tsx.
   - Problem: the owner fills the Cabinet in by hand. The copy says "Імпортуйте файл кнопкою «Імпортувати XML з пристрою»", and the XML card is the main feature of the screen.
   - Fix: make the manual path primary.
     - Rewrite the guide as "enter these lines in the Cabinet form".
     - Demote the XML card to "file for your records / for other software".
     - Or verify the Cabinet behaviour and drop the claim.

2. Declaration figures cannot be copied (screen: Declaration).
   - Where: Figures.tsx:80-112. The table is plain text "1 234,56 ₴".
   - Problem: for hand entry the owner needs plain digits (`1234.56`) per line, and the Cabinet rejects a space and the "₴" sign. They must retype every line on a phone.
   - Fix: reuse `CopyField` or a per-row copy button with `formatPlainAmount`, plus the line code. Add a "step through lines in order" hint, and show on the screen the Cabinet's real field names and sections: Section I, Appendix 1, "Додаток ЄСВ".

3. A disconnected or rejected monobank is visible only inside Settings.
   - Where: MonobankConnectionSection.tsx:68,127 (`tokenRejected`). DashboardScreen.tsx shows no sync-health banner.
   - Problem: after a token is rejected, sync stops silently. Income stops arriving, the dashboard shows an optimistic "AllDone" / smaller debt, and the owner pays too little. Webhook and sync failures also live only in Settings.
   - Fix: expose `tokenRejected` and `lastFailure` in the dashboard response. Show a `role="alert"` banner at the top: "monobank від'єднано з <дата>, нові надходження не імпортуються", with a button to Settings > monobank > "Замінити токен". Add the same line to the Declaration readiness list.
   - Also: the recovery text in `tokenRejected` is good, but "Синхронізувати зараз" is disabled with no explanation next to it.

4. The app silently assumes the user is a group 3 single-tax payer.
   - Where: FopSettingsForm.tsx and messages `settings.fop.*` have no field for single-tax-payer registration date, group, or confirmation. Header/title/manifest hardcode "ФОП 3 групи" (uk.json:3-4, manifest.json). The only group UI is `backOnGroup3`.
   - Problem: if the tax office has not registered group 3, every number is shown with the same confidence as a confirmed one. This is the known failure.
   - Fix:
     - Add a settings field "Дата реєстрації платником єдиного податку (3 група)" and a "Підтверджено в Електронному кабінеті" checkbox.
     - While unconfirmed, show a persistent banner on Dashboard, Payments and Declaration: "Групу 3 не підтверджено, суми орієнтовні".
     - Block "Mark filed" and the XML download until confirmed.
     - Do not key the single tax off the FOP registration date alone.

5. Form errors are not exposed to assistive tech.
   - Where: shared/ui/fields.tsx:28-48 (`FieldWrapper`). Errors are a bare `<ul>` with no `role="alert"`, no `aria-live`, no `aria-invalid` on the input, no `aria-describedby` for hint or error. Only 15 files use role/aria-live, and `aria-invalid` is used nowhere in features.
   - Impact: affects every form (payments, transactions, invoices, settings, pay amount).
   - Fix: generate ids in `FieldWrapper`. Pass `aria-invalid` and `aria-describedby={hintId errorId}` to the child. Give the error list `role="alert"`. Cloning the child or passing a render prop works.
   - Also: pay-panel.tsx:101-110 shows `amountInvalid` as soon as the panel opens with an empty amount, which is aggressive. Show the error only after the field has been touched.

## Medium

6. Warning stack pushes "what to pay" down the dashboard.
   - Where: DashboardScreen.tsx:34-40. Up to 4 banners (limit crossing, review, declaration due, overdue invoices) render before `HeroCard`.
   - Problem: on 375 px the hero with the date and amounts can fall below the fold. Four of these banners are plausible in the week before filing.
   - Fix: put the hero first. Collapse the secondary notices into a single "Потребує уваги (N)" list under it. Keep only `limitCrossing` and the bank-disconnected alert above.

7. Touch targets under 44 px on the phone.
   - Where: button.tsx variants: default `h-8` (32), sm `h-7` (28), xs `h-6` (24), icon `size-8`, icon-sm `size-7`, icon-xs `size-6`. There are 93 uses of xs/sm/icon-xs/icon-sm. The header toggles (LanguageToggle, ThemeToggle, SignOut) are `size-8`. The CopyField button is 32 px. The "Відкрити Електронний кабінет" link (HeroCard.tsx:~75) is h-8. Inline text links ("Перейти до налаштувань") are 20 px tall. The pay QR "enlarge" button is `sm`.
   - Fix: add a `pointer-coarse:` variant in `buttonVariants` (`pointer-coarse:h-11 pointer-coarse:min-w-11`). Make text CTAs `inline-flex min-h-11 items-center`. Keep the compact size on desktop.

8. Focus ring and badge contrast.
   - `--ring: oklch(0.708 0 0)` at `ring/50` on white is about 1.6:1. The inputs and buttons' `focus-visible:ring-3 ring-ring/50` plus `border-ring` (about 2.7:1) is under the WCAG 1.4.11 3:1 minimum for the focus indicator. In dark, `--ring: oklch(0.556)` at 50% is also faint. Fix: raise `--ring` to about `oklch(0.55 0 0)` in light and use `ring-ring` without `/50`, or add a 2 px solid outline.
   - The "Підключено" badge is `text-emerald-600` on `bg-emerald-500/15` at 12 px (MonobankConnectionSection.tsx:~103). It computes to about 3.4:1 and fails AA. Use `text-emerald-700 dark:text-emerald-400`, as the dashboard already does.
   - The bottom-nav label is `text-[11px]` (AppNav.tsx:27), below comfortable size. Use 12 px minimum.
   - Not verified: dark `muted-foreground` and `destructive/10` banners. Verify in a browser.

9. Loading and error states are not announced and have no retry.
   - Where: DashboardScreen.tsx:20-26, DeclarationScreen.tsx:41-52. Plain `<p>` with no `role="status"`. `loadFailed` has no retry button. Only AuthGate has retry. Around 30 components render a bare `loadFailed`.
   - Fix: a shared `<LoadState>` with `role="status"`/`role="alert"` and `refetch`. After a mutation succeeds or fails, announce the result (a toast or a live region).

10. Copy inconsistent for plurals and for ru.
   - `dashboard.confirmMarkPaid` has only `one`/`other` in uk and ru. uk 21 payments would read "21 платежі", though max 3 kinds today so it is latent. Add `few`/`many`.
   - ru `declaration.figures.*` (l06, l07, l12, l13, l14_1, l23-25, l21) is in Ukrainian ("Військовий збір", "Обсяг доходу…"). It is probably deliberate, because they mirror the Cabinet form, but it is a mixed-language screen with no note. Add a short hint "Назви рядків — як у формі кабінету (українською)", or use the Russian wording with the official name in parentheses.
   - Other identical uk=ru strings (`dashboard.quarter` "кв.", `payments.quarter`, `audit.filingPeriod`, `transactions.row.source.*`, `reserveJar.balance`) are fine (templates and proper nouns). 25 identical long strings in total; the rest are those.
   - Keys: uk and ru have identical key sets (1365 lines each). No missing keys.
   - Hardcoded strings: a grep of tsx for Cyrillic literals and for hardcoded aria-label/title/placeholder found none. `Intl.*` is used with the active locale everywhere (money.ts, dates.ts).
   - `src/shared/lib/dates.ts:24` (`en-CA`) is intentional for ISO format.

11. Year onboarding is thin.
   - When the year has no parameters, the dashboard shows "Баланс недоступний" plus a link to `/settings` (the default tab), not the tax-year tab, and the card title gives no reason.
   - A "clone from previous year" exists (TaxYearTable.tsx:354), but the dashboard CTA does not offer it.
   - Fix: deep link to `/settings?tab=years&year=N`, text "Задайте параметри {year} року (скопіювати з попереднього)", and show the warning in the first 2 weeks of January. Mark values "не перевірено" distinctly on the dashboard until verified (`TaxYearVerificationWarning` exists, so check it renders on the dashboard).

12. Mark-paid on the dashboard is a one-click "paid today" record.
   - HeroCard.tsx `MarkPaid` records payments with `todayInKyiv()` and the computed amounts. The confirm text says it, but there is no way to set the actual date or amount in that dialog. If the owner paid yesterday, or paid a different amount (the QR panel lets them edit the amount), the ledger is wrong.
   - Fix: carry the amount from the pay panel into mark-paid, or offer a date field and amount in the confirm step.

13. Dialog and sheet behaviour (positive plus gaps).
   - Radix Dialog gives the focus trap, Esc, and focus return. `Sheet` has a title, a description and a labelled close button. Good.
   - Gaps:
     - The nested QR enlarge dialog uses `aria-describedby={undefined}`, which is fine.
     - The sheet has no visible drag handle.
     - The overlay `bg-black/50` gives no focus-ring contrast issue.
     - The enlarged QR sits on forced `bg-white`, correct for scanners.
   - Fine as is.

## Low

14. PWA.
   - Manifest (public/manifest.json): `purpose: "any maskable"` combined on the same icons. Split into separate `any` and `maskable` entries, otherwise the icon is cropped or shows a wrong safe zone. Missing `id`, `screenshots` (needed for the richer Android install UI), `categories`. `theme_color: #0a0a0a` and `background_color: #fff` disagree with each other and with the light theme, though layout.tsx sets per-scheme `themeColor` meta. The manifest is static Ukrainian (`lang: uk`) even when the user is in ru.
   - Service worker (service-worker/sw.ts): only precache and `skipWaiting`/`clientsClaim`, with no `runtimeCaching`, no navigation fallback, no offline page. Offline, the first navigation shows the browser error page; with a loaded shell, API calls fail and the screens show a bare `loadFailed`. For a money tool this is arguably correct (no stale amounts), but add an offline page that says "Немає мережі, дані не оновлено" and, optionally, a read-only cache of the last dashboard response with a visible "на {time}" stamp.
   - Update flow: `skipWaiting` plus `clientsClaim` activates the new worker under an open tab with no prompt, and the open page keeps old JS chunks (and a possible hashed-chunk 404) until reload. Serwist's `registerSW` with a "Доступна нова версія, оновити" toast avoids that, and the toast must not interrupt a half-filled form.
   - apple-touch-icon exists. `appleWebApp.capable` is set.

15. Smaller items.
   - Heading levels: `<h1>` is the app name in the header, page titles are `<h2>`, and section titles are `<h3>`. That is consistent, but the `h1` text is the same on every page. Give `<title>` per route (only the root metadata exists), so tab and history entries are distinguishable and screen readers announce the page.
   - AuthGate returns `null` while pending, which gives a blank white screen on a cold start. Show a skeleton or spinner.
   - Language and theme toggles are icon-only, with a label but no indication of the current value in the name. Add the current value to the `aria-label`.
   - LimitBar and the quarter progress: confirm they have text alternatives (not checked).
   - `DeclarationScreen` "Next" link is hidden for a future quarter, so the user can't preview. Intentional (#163), fine.
   - The hero shows no total across kinds by design (Rule 7). That is coherent, but "what to pay this week" with 3 amounts means 3 separate bank payments, and the copy does not say so. Add "3 окремі платежі" to the hero.
   - Pay panel: the QR hint relies on `qrForTreasuryAccounts = true` (#100), so an unverified scan result is shipped. Keep the manual copy fields prominent, which they are (above the QR).

## What looks good
Plural categories are complete in uk and ru (except item 10). The dashboard hero (date, days left, amounts, pay button, cabinet link) is a fast "what to pay" answer. The pay panel is a proper dialog with copy buttons that announce via `role="status"`. All inputs have visible labels (`FieldWrapper`). No hardcoded UI strings were found.
