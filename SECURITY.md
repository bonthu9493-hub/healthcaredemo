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

- The JWT is kept in `sessionStorage` (cleared when the tab closes). CSP mitigates XSS, but the stronger design is an `HttpOnly; Secure; SameSite` cookie with refresh-token rotation.
- SignalR sends the token as a query-string parameter (browser WebSocket limitation); keep access logs from recording query strings.
- Rate-limit state is in memory; use a shared store behind multiple instances, and configure forwarded headers behind a reverse proxy so the client IP is correct.
- No email verification, MFA or password reset yet.
- Serve the SPA over HTTPS and send the CSP as an HTTP header (set `connect-src` to the real API origin).
- Enable SQL Server TDE and encrypted backups for PHI; review HIPAA / local health-data regulations before handling real patient data.
- Re-run the audit commands above regularly (or enable Dependabot).
