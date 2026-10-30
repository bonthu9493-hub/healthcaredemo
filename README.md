# MediCare HMS — Hospital Management System

A full-stack hospital management app with **real-time** appointment notifications.

| Layer | Tech |
|-------|------|
| Frontend | Angular 22 (standalone components, signals), SignalR client |
| Backend | ASP.NET Core 10 Web API, EF Core 10, SignalR |
| Database | SQL Server (SQL Server Express locally) |
| Auth | JWT bearer tokens, BCrypt password hashing, role-based access (Patient / Doctor / Admin) |

## Features

- **Patients** register, book appointments with doctors, cancel them, and read their own medical records.
- **Doctors** see their appointments, mark them complete/cancelled, view only the patients they have an appointment with, and write medical records.
- **Nurses** (created by an admin) can view all appointments, read-only. They cannot change appointments or read medical records. They record vitals, issue queue tokens, admit patients, take payments and can search patients by name.
- **Pharmacists** and **lab technicians** (created by an admin) each get their own page and see only what their job needs.
- **Admins** create doctor, nurse, pharmacist and lab technician accounts, list users, and review the audit log. Admins cannot read clinical records.
- **Staff attendance:** doctors and nurses check in and out of their shift (**My attendance** page). Admins get a per-person report for any date range (weekdays present/absent, hours worked, late arrivals, missing check-outs) and can download it as CSV. Hospital time zone, shift start and grace period are configured under `Attendance` in `api/appsettings.json`.
- **Appointment attendance:** doctors mark a past appointment **Attended** or **No-show**. Admins get a per-doctor report (attended, no-show, cancelled, upcoming, not yet marked, attendance rate) with CSV export. Once an appointment is no longer Scheduled it can't be changed, so the history stays trustworthy.
- **Live vitals:** nurses (and doctors for their own patients) record heart rate, blood pressure, oxygen and temperature. The server grades each reading Normal / Warning / Critical and, if abnormal, alerts the patient's doctors and all nurses instantly. Patients can see their own readings and a heart-rate trend.
- **Code blue:** a doctor or nurse raises an emergency with a location. Every other doctor and nurse sees a red alarm banner immediately (rate-limited to prevent spam).
- **Notification centre:** the bell in the top bar keeps every alert (appointments, vitals, code blue, queue). They are stored per user, survive a refresh, and can be marked read.
- **Live queue board:** nurses issue numbered tokens per doctor; doctors call the next patient; everyone's board updates instantly and patients are notified when it's their turn. Patients only see their own name on the board.
- **Doctor schedules and slot booking:** doctors define weekly working windows and slot lengths. Patients pick from free slots only; booked slots disappear, and cancelled ones come back. A doctor with no schedule can still be booked at any time.
- **Real time:** when a patient books, the doctor's screen shows a toast and refreshes instantly. When a doctor completes or cancels, the patient is notified (SignalR WebSockets).
- **Audit log** of logins, failed logins, bookings and every read/write of medical records.

- **Patient profile:** each patient keeps their blood group, allergies, long-term conditions and emergency contact. Doctors and nurses see a bedside summary (never the phone number or date of birth).
- **Pharmacy and prescriptions:** a **Pharmacist** keeps the medicine catalogue (price, stock, reorder level, expiry; low-stock and expiring flags). Doctors prescribe to their own patients, with an **allergy check** that blocks a matching medicine until the doctor explicitly overrides (the override is audit-logged). The pharmacist dispenses: stock is taken, the patient is billed and notified, all or nothing, and never twice.
- **Lab:** doctors order tests from a catalogue; a **Lab technician** enters the result (flagged normal or abnormal); the doctor and patient are notified and the test is billed.
- **Billing and PDF receipts:** charges (consultation fee when an appointment is attended, medicines, lab tests, bed days, manual extras) collect on one open invoice per patient. The front desk (nurse or admin) marks it paid (cash, card or UPI), after which it is frozen. Patients download their own invoice or receipt as a PDF; admins see a revenue and outstanding-balance summary.
- **Wards and beds:** admins set up wards (with a daily rate) and beds. Doctors and nurses admit patients; the database itself prevents two patients in one bed. A doctor discharges with a summary, which frees the bed and bills the stay by started days. The board updates live.
- **Search and paging:** appointments, medical records, users and the audit log are searched, filtered and paged on the server (page size capped at 100), always inside what the signed-in user is allowed to see.

## Project layout

```
api/                 ASP.NET Core Web API (Controllers, Data, Dtos, Hubs, Services, Migrations)
api.tests/           Automated API tests (xUnit, run against a real SQL Server)
web/                 Angular frontend (core/ = auth, api, realtime; pages/ = screens; ui/ = shared pieces)
docker-compose.yml   One-command start: database + API + web
.github/workflows/   CI pipeline (build, test, audit, secret scan)
```

## Running locally

**Prerequisites:** .NET 10 SDK, Node 22+, SQL Server Express (instance `SQLEXPRESS`).

### 1. Configure secrets (never committed)

The API refuses to start if required secrets are missing. For development use .NET user-secrets:

```bash
cd api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "<random string, 32+ chars>"
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost\SQLEXPRESS;Database=HospitalDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
dotnet user-secrets set "Cors:Origins" "http://localhost:4200"
dotnet user-secrets set "Admin:Email" "admin@hospital.local"
dotnet user-secrets set "Admin:Password" "<strong password>"
```

In production use environment variables instead (see [.env.example](.env.example)); `:` becomes `__`, e.g. `Jwt__Key`.
`Admin:*` only seeds the first admin once — remove `Admin:Password` afterwards.

### 2. Start the API (applies migrations automatically)

```bash
cd api
dotnet run --urls http://localhost:5098
```

### 3. Start the web app

```bash
cd web
npm install
npm start          # http://localhost:4200
```

Sign in as the seeded admin, add a doctor, then register a patient from the login page.

## Running the tests

```bash
# API: 180+ tests. They start the real API in memory against a throwaway database
# (created and deleted automatically). Defaults to local SQL Express; use TEST_SQL_CONNECTION for another server.
# Stop the running API first, or the build can't replace its files.
dotnet test api.tests

# Web: unit tests (Vitest)
cd web
npm test -- --watch=false
```

The tests cover the allergy check, all-or-nothing dispensing (including two pharmacists clicking at once), billing and payment rules, PDF structure, bed double-booking races, login and lockout, who may see or change what (patients, doctors, nurses, admins), double-booking, schedules and slots, vitals thresholds, code blue, the queue, attendance reports (including CSV formula protection), security headers, CORS and rate limiting. They have been checked to fail when the code is broken (for example, the bug where a status change returned success but was not saved).

## Docker (one command)

Requires Docker Desktop. The database is only reachable from inside the Docker network.

```bash
cp .env.example .env        # then edit .env: set SQL_SA_PASSWORD, JWT_KEY, ADMIN_EMAIL, ADMIN_PASSWORD
docker compose up --build
```

Open http://localhost:4200. The API is on http://localhost:5098. The Docker setup uses these two ports because the web app's API address and Content-Security-Policy are set to `localhost:5098`; to deploy elsewhere, change `API` in `web/src/app/core/auth.ts`, and `connect-src` in `web/src/index.html` and `web/nginx.conf`.

## Continuous integration

`.github/workflows/ci.yml` runs on every push and pull request: builds and tests the API against a temporary SQL Server (with a random password generated for that run), builds and tests the web app, fails on known vulnerable NuGet or npm packages, scans the whole git history for committed secrets, and builds both Docker images. Dependabot (`.github/dependabot.yml`) opens pull requests for dependency updates.

## Main API endpoints

| Method | Route | Who |
|--------|-------|-----|
| POST | `/api/auth/register`, `/api/auth/login` | public (rate limited) |
| GET | `/api/doctors` | any signed-in user |
| GET/POST | `/api/appointments?q=&status=&page=&pageSize=` | own data only (POST: patients); nurses/admins read all |
| PATCH | `/api/appointments/{id}/status` | participants and admins (not nurses) |
| GET | `/api/records` | patient (own) / doctor (own patients) |
| POST | `/api/records`, GET `/api/patients` | doctors |
| POST | `/api/admin/doctors`, `/api/admin/nurses`, GET `/api/admin/users`, `/api/admin/audit` | admins |
| POST/GET | `/api/vitals` | nurses, doctors (own patients); patients read own |
| POST | `/api/emergency/code-blue` | doctors, nurses |
| GET | `/api/notifications`, POST `/api/notifications/{id}/read`, `/read-all` | own notifications only |
| GET | `/api/queue/board` | any signed-in user (patients see only their own name) |
| POST | `/api/queue/tokens`, `/api/queue/tokens/{id}/skip` | nurses, admins (skip: doctors too) |
| POST | `/api/queue/call-next` | doctors (own queue) |
| GET/PUT | `/api/schedule/me` | doctors |
| GET | `/api/doctors/{id}/slots?date=` | any signed-in user |
| GET | `/api/patients/lookup?q=` | nurses, admins (id and name only) |
| GET/PUT | `/api/profile/me` | patients (own profile) |
| GET | `/api/patients/{id}/summary` | nurses; doctors for their own patients |
| GET | `/api/medicines?q=&low=` | doctors, pharmacists, admins |
| POST | `/api/medicines`, `/api/medicines/{id}/restock` | pharmacists, admins |
| GET | `/api/prescriptions` | patients (own), doctors (own), pharmacists (all) |
| POST | `/api/prescriptions`, `/api/prescriptions/{id}/cancel` | doctors |
| POST | `/api/prescriptions/{id}/dispense` | pharmacists |
| GET | `/api/labs/tests` | doctors, lab technicians, admins |
| POST | `/api/labs/tests` | lab technicians, admins |
| GET | `/api/labs/orders` | patients (own), doctors (own), lab technicians (all) |
| POST | `/api/labs/orders`, `/api/labs/orders/{id}/cancel` | doctors |
| POST | `/api/labs/orders/{id}/result` | lab technicians |
| GET | `/api/invoices`, `/api/invoices/{id}`, `/api/invoices/{id}/pdf` | patients (own), nurses, admins |
| POST | `/api/invoices/{id}/items`, `/api/invoices/{id}/pay` | nurses, admins |
| POST | `/api/invoices/{id}/void` | admins |
| GET | `/api/wards` | doctors, nurses, admins |
| POST | `/api/wards`, `/api/wards/{id}/beds` | admins |
| POST | `/api/admissions` | doctors (own patients), nurses |
| POST | `/api/admissions/{id}/discharge` | doctors |
| GET | `/api/admissions/me` | patients (own) |
| POST | `/api/admin/staff` | admins (Nurse, Pharmacist or LabTech) |
| GET | `/api/admin/reports/billing?from=&to=` | admins |
| GET | `/api/attendance/me` | all staff (own shifts) |
| POST | `/api/attendance/check-in`, `/api/attendance/check-out` | doctors, nurses, pharmacists, lab technicians |
| GET | `/api/admin/reports/staff-attendance[.csv]?from=&to=` | admins |
| GET | `/api/admin/reports/appointment-attendance[.csv]?from=&to=` | admins |
| WS | `/hubs/notifications` | signed-in users |

## Security

See [SECURITY.md](SECURITY.md) for the controls implemented and the audit results.

## Database changes

```bash
cd api
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name>
```
