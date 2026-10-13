# Implementation status

Last updated: 2026-10-13

## Checklist

- [x] Repository inspection, plan, architecture and data model
- [x] Backend: domain, EF Core model (Fluent API), initial migration committed
- [x] Plan catalog (single source of truth) + `GET /api/plans`
- [x] Registration with plan-specific fields, server-side validation (Czech messages), privacy confirmation
- [x] Passwordless clinic login: HMAC-hashed 6-digit codes, expiry, attempt limit, atomic consumption, supersession, cooldown
- [x] SMTP via MailKit (Gmail STARTTLS / Mailpit), safe failure handling and retry
- [x] Server-side sessions (HttpOnly cookies), logout/revocation, CSRF (antiforgery header), CORS, rate limiting
- [x] Clinic portal API (`/api/clinic/me`, edit of allowed fields, tenant isolation)
- [x] Administrator: env-based idempotent seeding, password hashing, lockout, reset command
- [x] Admin dashboard (persisted metrics, 7-day series, activity), clinic list (search/filter/sort/paging), detail
- [x] Status changes with audit history, transaction, optimistic concurrency; private admin notes
- [x] Frontend: landing, registration wizard, OTP verification, clinic portal + edit, admin login/dashboard/list/detail
- [x] Responsive design system (custom CSS), accessible forms/dialogs, toasts, empty/loading/error states
- [x] Docker: API + Nginx images, Compose (db, api, web, mailpit profile), health checks, persistent volumes
- [x] README (Czech), `.env.example`, `.gitignore`, setup scripts
- [x] Backend tests (xUnit + Testcontainers), frontend tests (Vitest + RTL)
- [ ] Full browser end-to-end demo against the Docker stack (blocked – see below)

## Verification log (commands actually run)

| Check | Result |
|---|---|
| `dotnet build` (warnings as errors) | ✅ succeeded |
| `dotnet test` (backend) | ✅ **61/61 passed** (~28 s, PostgreSQL 17 via Testcontainers) |
| `npx tsc -b` (strict) | ✅ no errors |
| `npm test` (Vitest) | ✅ **19/19 passed** (4 files) |
| `npm run build` | ✅ succeeded (main chunk 71 kB, vendor chunks split, no size warning) |
| `docker compose config` | ✅ valid |
| `docker compose up --build` | ✅ all four services healthy; migration applied; initial admin created |
| `GET http://localhost:8080/api/plans` through Nginx | ✅ 200 with plan JSON |
| SPA fallback `GET /registrace` | ✅ 200; CSP and security headers present |
| Browser: landing page, plan cards, process steps | ✅ rendered and checked visually (desktop) |
| Browser: registration wizard (validation, steps, summary) | ✅ checked |
| Browser: registration submit | ❌ found bug: antiforgery threw over plain HTTP with `COOKIE_SECURE=true` → **fixed** (antiforgery cookie `SameAsRequest`) + regression test added (passes) |
| Redeploy with the fix, OTP via Mailpit, portal, admin approval in the browser | ⏸ **not yet verified** – the C: drive ran out of space (0 bytes free) during the rebuild; Docker Desktop/WSL stopped responding |

## Open items

1. Free disk space, restart Docker Desktop (`wsl --shutdown` if it reports "unable to start"), then
   `docker compose up --build -d` and run the demo workflow from the README end to end.
2. Optional extensions listed in README → "Předpoklady, omezení a další kroky".
