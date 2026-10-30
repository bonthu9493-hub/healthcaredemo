# Security

Last audited: 2026-10-29. Automated checks and manual testing against the running app.

## Controls implemented

| Area | Implementation |
|------|----------------|
| Secrets / API keys | None in source. Loaded from user-secrets (dev) or environment variables (prod). The API **fails to start** if `Jwt:Key` (32+ chars), connection string or CORS origins are missing. `.gitignore` blocks `.env`, `secrets.json`, `*.key`, `*.pfx`. |
| Env variables | Template in `.env.example` (placeholders only). |
| Authentication | JWT (HS256), issuer + audience + lifetime validated, 30 min expiry, 30 s clock skew. |
| Password hashing | BCrypt, work factor 12. Password policy: 10+ chars with upper, lower, digit. |
| Brute force | Account lockout (5 failures → 15 min) plus per-IP rate limit on `/api/auth/*` (10/min). Unknown emails take the same time as wrong passwords; errors are generic (no account enumeration). |
| Authorisation | Fallback policy requires login on every endpoint unless `[AllowAnonymous]`. Admin routes: `[Authorize(Roles = Admin)]`. Angular route guards mirror this (UX only; the API is the authority). |
| Users access own data | All queries are scoped by the caller's id/role. A patient sees only own appointments/records; a doctor only patients with whom they have an appointment; admins cannot read clinical records; nurses get read-only access to appointments and nothing clinical (least privilege). Out-of-scope ids return 404/403, not data. |
| Input sanitising | DataAnnotations validation (length, format, regex) on every DTO; `[NoHtml]` rejects `<` and `>` in free-text fields; EF Core parameterises all SQL (no string-built queries). |
| XSS | Angular escapes all interpolation and the app never uses `innerHTML`; server rejects markup; strict CSP in the SPA (`script-src 'self'`) and `default-src 'none'` on API responses. |
| Rate limiting | Global 120 req/min per IP, auth endpoints 10/min; returns 429. |
| CORS | Explicit allow-list from config (no wildcard), limited methods and headers. |
| Security headers | `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, `Content-Security-Policy`, `Cross-Origin-Resource-Policy`, `Cache-Control: no-store`, HSTS + HTTPS redirect outside Development. Server header removed. |
| No debug mode | No developer exception page; errors are generic ProblemDetails with no stack traces in any environment. EF SQL command logging is off. |
| Database | EF parameterised queries, length limits and unique indexes, FK `Restrict` deletes, encrypted connection (`Encrypt=True`), Windows authentication locally (no DB password). Use a least-privilege SQL login in production. |
| Attendance & reports | Staff can only record their own check-in/out (identity comes from the token, never the request). Reports are admin-only, limited to a 366-day range, and every CSV export is audit-logged. CSV cells that start with `=`, `+`, `-` or `@` are prefixed with `'` so spreadsheets can't run injected formulas. No-show can't be set on a future appointment, and finished appointments can't be edited. |
| Real-time data | The SignalR hub requires a valid token and addresses each user by id, so a notification or vitals update is delivered only to its intended recipients. The queue signal carries no data: clients re-fetch the board through the normal authorised endpoint. Notifications are filtered by the caller's own id on every read and update. |
| Vitals | Severity is computed on the server from fixed thresholds (the client can't set it), ranges are validated, and access is scoped: patients read only their own readings, doctors only patients they have an appointment with, nurses may record for any patient, admins have no access. Reads and writes are audit-logged. |
| Code blue | Doctors and nurses only, location text validated (no markup), rate-limited to 5 per minute per IP, and audit-logged. |
| Patient lookup | Nurses and admins get a name-only search (min 2 chars, max 20 results) so they never receive dates of birth or phone numbers. |
| Search and paging | Search text is passed to SQL as a parameter (no string-built queries), trimmed and capped at 100 characters, and wildcard characters are treated as plain text. Searches run on top of the access filter, so searching can never reveal rows the user couldn't already list. Page size is capped at 100. |
| Automated checks | CI builds and tests every push, fails on known-vulnerable NuGet/npm packages, scans the full git history for secrets (gitleaks), and runs with a read-only token. The test database password is generated per run, never stored. Dependabot proposes dependency updates. |
| Containers | Images run as a non-root user with all Linux capabilities dropped and `no-new-privileges`. The database port is not published. Secrets come from a git-ignored `.env`. The web image sends the same CSP and security headers from nginx. |
| Production build vs CSP | The production build used to inject an inline script (for the stylesheet) which our CSP blocks. This is turned off (`inlineCritical: false`) so the strict CSP works in production, not only in development. |
| Clinical data | Patient profile, prescriptions, lab results, invoices and admissions are each filtered by the caller's own id and role on every query; admins cannot read clinical records or prescriptions. Nurses and doctors get a bedside summary that omits phone number and date of birth. Everything read or written is audit-logged. |
| Medication safety | A prescription that matches a recorded allergy is refused (409) until the doctor deliberately overrides, and the override is audit-logged. Expired medicines can be neither prescribed nor dispensed. |
| Money and stock integrity | Dispensing, paying, voiding, discharging and entering a lab result are single guarded SQL updates inside a transaction, so a double click or two staff at once can't dispense twice, take stock below zero, pay twice, or bill twice. A paid invoice is frozen. Failed steps roll everything back. Invoice numbers come from a database sequence. |
| Beds | The database has a unique rule (one active admission per bed and per patient), so a race can't put two patients in one bed. |
| PDF receipts | Generated on the server with escaped text (a patient name can't inject PDF commands); a patient can only download their own; no external service or font files involved. |
| Staff roles | Pharmacists and lab technicians can only be created by an admin through a role allow-list that cannot produce an Admin or Doctor. |
| CORS methods | Only GET, POST, PUT and PATCH from the web app's origin (a test fails if the app needs a method that's blocked). |
| Password hashing cost | BCrypt work factor is always 12 in production; only the "Testing" environment may lower it, so thousands of test accounts don't take minutes. |
| Audit trail | Logins, failed logins, bookings and all medical record reads/writes are logged with user and IP. |
| Exposed files | The API serves no static files or directory listings. Only source files are tracked in git. |

## Audit results (2026-10-29)

- `dotnet list package --vulnerable --include-transitive`: **0** vulnerable. A transitive `System.Security.Cryptography.Pkcs` 9.0.11 advisory was fixed by pinning the patched version.
- `dotnet list package --outdated / --deprecated`: none.
- `npm audit`: **0** vulnerabilities.
- Unused packages: unused dev dependency (`prettier`) removed; remaining packages are used.
- Secrets in git history: scanned all commits; no keys, passwords or connection secrets.
- Manual tests: unauthenticated → 401; patient on admin route → 403; admin on clinical records → 403; cross-patient data → empty; double booking → 409; `<script>` payload → 400; 6 bad logins → lockout; burst of logins → 429.

## Known limitations / before production

- **Docker files are unverified locally.** Docker isn't installed on the development PC, so the Dockerfiles and compose file were written but never built or run there. The CI "Docker images build" job builds the images; run `docker compose up --build` once and fix anything it reports.
- The compose file connects to SQL Server as `sa` for simplicity. Create a least-privilege database login for anything beyond a demo.

- The JWT is kept in `sessionStorage` (cleared when the tab closes). CSP mitigates XSS, but the stronger design is an `HttpOnly; Secure; SameSite` cookie with refresh-token rotation.
- SignalR sends the token as a query-string parameter (browser WebSocket limitation); keep access logs from recording query strings.
- Rate-limit state is in memory; use a shared store behind multiple instances, and configure forwarded headers behind a reverse proxy so the client IP is correct.
- No email verification, MFA or password reset yet.
- Serve the SPA over HTTPS and send the CSP as an HTTP header (set `connect-src` to the real API origin).
- Enable SQL Server TDE and encrypted backups for PHI; review HIPAA / local health-data regulations before handling real patient data.
- Re-run the audit commands above regularly (or enable Dependabot).
