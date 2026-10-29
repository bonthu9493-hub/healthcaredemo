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
- **Admins** create doctor accounts, list users, and review the audit log. Admins cannot read clinical records.
- **Real time:** when a patient books, the doctor's screen shows a toast and refreshes instantly. When a doctor completes or cancels, the patient is notified (SignalR WebSockets).
- **Audit log** of logins, failed logins, bookings and every read/write of medical records.

## Project layout

```
api/   ASP.NET Core Web API (Controllers, Data, Dtos, Hubs, Services, Migrations)
web/   Angular frontend (core/ = auth, api, realtime; pages/ = screens)
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

## Main API endpoints

| Method | Route | Who |
|--------|-------|-----|
| POST | `/api/auth/register`, `/api/auth/login` | public (rate limited) |
| GET | `/api/doctors` | any signed-in user |
| GET/POST | `/api/appointments` | own data only (POST: patients) |
| PATCH | `/api/appointments/{id}/status` | participants only |
| GET | `/api/records` | patient (own) / doctor (own patients) |
| POST | `/api/records`, GET `/api/patients` | doctors |
| POST | `/api/admin/doctors`, GET `/api/admin/users`, `/api/admin/audit` | admins |
| WS | `/hubs/notifications` | signed-in users |

## Security

See [SECURITY.md](SECURITY.md) for the controls implemented and the audit results.

## Database changes

```bash
cd api
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name>
```
