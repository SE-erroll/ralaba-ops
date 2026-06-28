# RaLaba Ops

A production-style Retail Operations & Cash Reconciliation platform built to replace an Excel-based laundry business workflow, and to serve as a portfolio project demonstrating Clean Architecture in a real-world .NET + React application.

## Stack

- **Frontend:** React, TypeScript, Vite, Tailwind CSS
- **Backend:** ASP.NET Core Web API (.NET 8), Clean Architecture
- **Database:** PostgreSQL (hosted on Supabase)
- **Deployment:** Vercel (frontend), Render (backend)

All tooling used is open source.

## Solution Structure

```
ralaba-ops/
├── src/
│   ├── RaLaba.Domain          # Entities, enums, repository interfaces
│   ├── RaLaba.Application     # DTOs, services, use-case interfaces
│   ├── RaLaba.Infrastructure  # EF Core, Postgres, repository implementations
│   └── RaLaba.API             # Controllers, DI wiring, middleware
├── tests/
│   └── RaLaba.UnitTests
├── frontend/                  # React + TS + Vite + Tailwind app
├── docs/
└── RaLaba.sln
```

## Getting Started

### Backend

```bash
cd src/RaLaba.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your-supabase-postgres-connection-string>"
dotnet restore
dotnet run
```

API runs at `https://localhost:5001` (Swagger at `/swagger` in Development).

### Frontend

```bash
cd frontend
npm install
cp .env.example .env   # set VITE_API_BASE_URL
npm run dev
```

Runs at `http://localhost:5173`.

### Tests

```bash
dotnet test
```

## Database

Current schema (`receipts` table) lives in Supabase. See `docs/database-schema.sql` for the DDL.

> ⚠️ RLS on `receipts` is currently set to "Allow All" as a temporary measure. Lock this down before exposing the API publicly — see `docs/roadmap.md`.

## Roadmap

See `docs/roadmap.md` for the phased build-out plan (Receipts → Cash Flow/Reconciliation → Attendance/Payroll → Auth/Reports → Loyverse integration/multi-branch).
