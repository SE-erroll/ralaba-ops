# Dashboard and payment verification

This change extends the existing React/.NET app. No production migration is needed. No production data or hosting settings were changed, and no deployment was made.

## Reporting rules

- `GET /api/dashboard?from=YYYY-MM-DD&to=YYYY-MM-DD` requires the existing staff authorization policy.
- Sales use original receipt dates in Asia/Manila. Valid, unchanged SALE receipts count even when their initial payment status is unknown. Changed sales are excluded and their original amount is disclosed separately. Cancelled/refund/other receipts are counted separately and excluded from sales.
- Cash and GCash collections use payment collection dates. Every recorded payment remains included after source edits/cancellation; affected money is also flagged for reconciliation. Refund payouts are not supported or inferred.
- Confirmed outstanding balances are CURRENT across all imported dates and all recorded payments, regardless of the selected reporting range. Unknown/changed sale balances are disclosed separately, never treated as zero.
- Unpaid customer count uses distinct merchant/customer IDs. Unlinked unpaid receipts are counted separately. Customer names are not identifiers.
- All figures and preview lists come from one database statement, so they share a snapshot and are not limited to the receipt page's 100-row limit.
- The oldest three confirmed unpaid receipts and up to three review receipts link to the existing payment screen. Its new optional `number` query parameter retrieves the exact receipt, including older receipts beyond the first page.
- Loading/errors never render zero-valued dashboard cards. Date changes cancel obsolete requests. Sync configuration, last success, current worker error and stale-data warning are visible. Worker errors still reset after restart; this change does not add a durable sync history.
- No profit, expense, payroll, new payment method or refund workflow has been added.

## Local verification

Backend: 44 tests passed with no skips, including six tests using a disposable PostgreSQL 17 database and fake Loyverse/Auth HTTP responses. Tests exercise the real authenticated controllers, importer and SQL:

1. UNPAID → partial Cash → final GCash on different dates; retries, changed retry payload, identical duplicate and repeat imports.
2. Source edits and cancellation preserve recorded collections while flagging uncertainty.
3. Concurrent payments cannot overpay, and concurrent retries save once.
4. Initial unknown status requires evidence; zero initial review is repeatable; invalid dates and overpayments fail.
5. More than 100 receipts, Philippine date boundaries, customer IDs versus names, unlinked customers, unknown/cancelled/refund receipts and direct receipt lookup.
6. Empty data and invalid reporting ranges.

Frontend production build and eight existing Node tests pass. Four Playwright browser checks use explicit sample API responses (not production data): mobile/laptop rendering, authenticated requests, logout, date filtering, receipt links, and loading/error states. These browser checks and database tests verify the two sides separately; they are not a production acceptance test.

### Repeat backend tests

Start a disposable PostgreSQL instance listening only on localhost. Then, from the repository root:

```powershell
$env:RALABA_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Password=local-test-only'
dotnet test tests/RaLaba.UnitTests/RaLaba.UnitTests.csproj
```

The database tests reject remote hosts and require the `postgres` administrative database. Each test creates a random `ralaba_test_*` database, applies the existing integration schema, and drops only that database afterward. The local cluster must allow creation of test roles/databases. Without the environment variable, the six tests are explicitly skipped; a skip is not evidence they passed.

### Repeat browser tests

```powershell
cd frontend
npm ci
npx playwright install chromium
npm run test:ui
```

The browser test server uses fake public configuration and intercepted sample responses. No real credentials are needed. To use an existing Chrome installation, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to its executable path before running tests.

## Before rollout

Deployment remains subject to owner approval. Keep the earlier security follow-ups: restricted backend database role, removal of the dormant permissive receipts policy, owner/staff separation, and password protection. The dependency audit also reports existing React Router advisories; a major-version update needs a separate reviewed compatibility change. Payment correction/refund handling remains future work. The dashboard's collections are recorded receipts of money, not a reconciliation against bank/GCash statements or a profit figure.
