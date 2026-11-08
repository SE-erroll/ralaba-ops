# Loyverse import and collections

Extends the existing React/.NET app. The importer only sends GET requests to Loyverse. It never edits receipt comments or payments there.

## Staff workflow

Use the entire receipt comment for one marker (case insensitive):

- `PAID`: actual payment at the time of sale. Cash/GCash tender breakdown must equal the receipt total.
- `UNPAID`: no initial money collected, even if Loyverse reports a Cash tender for the receipt.
- `PARTIAL 100 CASH` or `PARTIAL 100 GCASH`: the amount actually collected at sale. Must be greater than zero and less than the total.
- Blank/other/contradictory comments: Needs review. Never default to paid. Confirm initial money against the logbook/Excel, then add later payments separately.

Initial payment uses the original receipt's Philippines calendar date. Later payments have their own collection date, method, amount, staff identity and reference note. Collections are grouped by collection date, not transaction date. Imported sales are not added a second time to collections. Existing legacy receipt records remain separate and are not included in this new report.

Refunds, cancellations and financial changes made in Loyverse after first import require reconciliation; they are not automatically treated as new sales/collections. No refund workflow is implemented. Collections explicitly disclose incomplete coverage when receipts need review.

## Owner setup

1. Apply reviewed `docs/loyverse-schema.sql` to the existing Supabase project. Tables live in `ralaba_private`, with RLS enabled and no browser grants. The script also revokes direct browser access to legacy `public.receipts`; server access continues through the staff-protected API.
2. Set `Loyverse__AccessToken` in Render only. Never set this in Vercel, Vite, GitHub or a browser config. A personal token can have broad permissions even though this code uses only reads.
3. Set `Loyverse__Enabled=true`, `Loyverse__StartAt=2026-11-04T00:00:00+08:00`, `Loyverse__IntervalSeconds=300`.
4. The existing `ConnectionStrings__DefaultConnection`, `PGSSLROOTCERT` and certificate file are required. Database connection is still the existing privileged server login; a dedicated restricted role is a future hardening task.
5. Deploy backend before frontend. Render's Docker build runs unit/access tests before publishing. Vercel runs frontend tests/build. Open **Loyverse & payments**, verify sync status, counts and samples against Back Office.

## Import guarantees and limits

- Starts with receipts created on/after Nov 4 Philippines time and catches up through the present. Date grouping uses `receipt_date` (Loyverse's business report date).
- Fixed API origin, redirects disabled, capped retries, 250-row cursor pagination, 25,000-row safety cap per run, four-minute run deadline.
- Merchant identity and PHP currency checked; switching tokens to another business stops sync.
- Unique merchant + receipt number. Transaction-scoped database lock serializes sync workers and staff-triggered syncs across processes.
- Each run commits receipt changes and its checkpoint together. Partial failures roll back. Ten-minute overlap catches boundary changes without duplicate imports. New receipts are initially fetched by creation time; later runs use update time.
- First payload/initial classification stays fixed. Updates preserve local payments and flag financial differences for review.
- Later payment writes lock the receipt, reject overpayments and accept idempotent request retries. Identical later payments with a new request ID require a distinct reference note. Zero-amount initial reviews retain actor, evidence and request ID too.
- On free Render hosting, background polling stops while the service sleeps. Opening the app wakes it; the worker catches up from the saved checkpoint. This is not guaranteed continuous five-minute syncing while the app is closed. Always-on hosting or an external scheduler is needed for that guarantee; no paid service was added.
- Open tickets are not imported. Historical receipts without the agreed markers need manual review. The app does not automatically import later Excel/logbook collections.
- Corrections/reversals and reconciliation of changed/refund receipts are not yet implemented. Do not enter test payments into live customer records.

Endpoints under `/api/loyverse` require the existing staff allowlist. Health checks remain public but contain no customer data. Raw API payloads and tokens are not returned in diagnostic errors.

References: https://developer.loyverse.com/docs/ and https://render.com/docs/free
