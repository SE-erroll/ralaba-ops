# Payment entry and corrections — local implementation

Release approved by the owner on November 10, 2026. The rollout includes the private audit-table schema update, backend and frontend. Deployment status must be verified in Render and Vercel; approval alone is not proof that the release is live.

## Behavior

- Receipt-day confirmation now displays the fixed date as explanatory text rather than an unclickable field. Staff enter zero when nothing was received on that day, then record the later collection separately.
- Additional collections accept the actual collection date, including the original receipt date when recording an omitted collection after confirming zero. Existing entries must not be entered again.
- Payment history offers correction of amount, date and Cash/GCash. A reason and an explicit confirmation are required in the UI.
- A zero correction voids an incorrectly recorded entry; it does not represent money refunded to a customer. The row remains in history. Refunds remain out of scope.
- Every correction saves the full before/after payment, staff ID, reason and time in a private audit table in the same transaction as the correction. No HTTP endpoint edits or deletes audit history.
- Corrected values feed the existing dashboard and collection totals. Original receipt sales remain unchanged. Moving an initial payment to a later date removes it from the receipt-day initial amount and records it as an additional collection.
- Initial zero declarations have no monetary payment row to correct. An omitted collection can be added with its actual date. Editing the original evidence text is not included.
- Receipt locks, revision checks and request IDs protect against concurrent edits, overpayment and duplicate retries. Changed, cancelled and refund receipts require separate reconciliation and cannot be corrected through this flow.

## Scope and access

Uses the existing approved-staff authorization policy. Owner-only correction permissions are not implemented; role separation remains a prerequisite before inviting staff who should not correct payments. Audit history is protected from browser/Data API access by revoked grants and RLS. The existing privileged backend database owner remains a separate security follow-up.

## Rollout order (requires owner approval)

1. Review the selected changes against deployed commit `f91827b21d19155499eada67bc96fc19a320f4da`; the local checkout also contains earlier published work.
2. Apply the **Payment corrections** section at the end of `docs/loyverse-schema.sql` to production. It adds the revision column and private audit table, and permits zero-valued voided entries. It does not alter existing payment values. New backend reads require this schema first.
3. Deploy the backend, then the frontend. Check health, staff-only access and real read-only receipt/history responses. Do not create test payments in production.
4. Review a real correction with the owner before making any financial record change.

## Local verification

48 backend tests passed, including nine isolated PostgreSQL tests covering payment entry, dashboard totals and corrections. New cases exercise movement from initial to later payment, amount/method correction, void/re-entry, before/after audit and staff attribution, retries, invalid input, cross-receipt IDs, source changes, overpayment and concurrent edits.

Frontend production build, eight existing Node tests, four dashboard browser checks and two payment browser checks passed. Browser checks use sample data and mocked authentication/API responses, not production credentials. The local database tests use real PostgreSQL and fake external Loyverse/Auth services; they are not a live production acceptance test.
