-- Additive integration storage. Existing public.receipts records are untouched.
-- API-only schema, never exposed to the Supabase browser/Data API.
CREATE SCHEMA IF NOT EXISTS ralaba_private;
REVOKE ALL ON SCHEMA ralaba_private FROM PUBLIC, anon, authenticated;
CREATE TABLE IF NOT EXISTS ralaba_private.loyverse_state (
  id integer PRIMARY KEY CHECK (id = 1),
  merchant_id text,
  checkpoint timestamptz,
  last_success timestamptz,
  payment_types jsonb NOT NULL DEFAULT '[]'::jsonb
);
INSERT INTO ralaba_private.loyverse_state(id) VALUES (1) ON CONFLICT DO NOTHING;
CREATE TABLE IF NOT EXISTS ralaba_private.loyverse_receipts (
  merchant_id text NOT NULL,
  receipt_number text NOT NULL,
  receipt_date timestamptz NOT NULL,
  source_updated_at timestamptz NOT NULL,
  customer_name text,
  initial_received numeric(12,2),
  initial_status text NOT NULL DEFAULT 'NEEDS_REVIEW',
  initial_review_id uuid,
  initial_review_note text,
  initial_review_by text,
  review_reason text,
  source_changed boolean NOT NULL DEFAULT false,
  first_payload jsonb NOT NULL,
  payload jsonb NOT NULL,
  imported_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (merchant_id, receipt_number)
);
CREATE TABLE IF NOT EXISTS ralaba_private.receipt_payments (
  id uuid PRIMARY KEY,
  merchant_id text NOT NULL,
  receipt_number text NOT NULL,
  amount numeric(12,2) NOT NULL CHECK (amount > 0),
  collected_date date NOT NULL,
  method text NOT NULL CHECK (method IN ('CASH','GCASH')),
  origin_key text NOT NULL,
  recorded_by text NOT NULL,
  note text NOT NULL DEFAULT '',
  recorded_at timestamptz NOT NULL DEFAULT now(),
  FOREIGN KEY (merchant_id, receipt_number) REFERENCES ralaba_private.loyverse_receipts(merchant_id, receipt_number),
  UNIQUE (merchant_id, receipt_number, origin_key)
);
CREATE INDEX IF NOT EXISTS receipt_payments_date_idx ON ralaba_private.receipt_payments(collected_date);
ALTER TABLE ralaba_private.receipt_payments ENABLE ROW LEVEL SECURITY;
CREATE INDEX IF NOT EXISTS loyverse_receipts_date_idx ON ralaba_private.loyverse_receipts(receipt_date DESC);
ALTER TABLE ralaba_private.loyverse_state ENABLE ROW LEVEL SECURITY;
ALTER TABLE ralaba_private.loyverse_receipts ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON ALL TABLES IN SCHEMA ralaba_private FROM PUBLIC, anon, authenticated;
-- No permissive policy: only the existing server database owner can access these tables.
-- The deployed frontend now uses the authenticated backend, not direct table access.
REVOKE ALL ON public.receipts FROM PUBLIC, anon, authenticated;

-- Payment corrections: keep the current value and an append-only before/after history.
-- Zero means an entry was voided as a recording error, never a refund payout.
ALTER TABLE ralaba_private.receipt_payments ADD COLUMN IF NOT EXISTS revision integer NOT NULL DEFAULT 0;
ALTER TABLE ralaba_private.receipt_payments DROP CONSTRAINT IF EXISTS receipt_payments_amount_check;
ALTER TABLE ralaba_private.receipt_payments ADD CONSTRAINT receipt_payments_amount_check CHECK (amount >= 0);
CREATE TABLE IF NOT EXISTS ralaba_private.payment_corrections (
  request_id uuid PRIMARY KEY,
  payment_id uuid NOT NULL REFERENCES ralaba_private.receipt_payments(id),
  before_value jsonb NOT NULL,
  after_value jsonb NOT NULL,
  reason text NOT NULL CHECK (length(trim(reason)) BETWEEN 1 AND 500),
  corrected_by text NOT NULL,
  corrected_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS payment_corrections_payment_idx ON ralaba_private.payment_corrections(payment_id,corrected_at);
ALTER TABLE ralaba_private.payment_corrections ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON ralaba_private.payment_corrections FROM PUBLIC, anon, authenticated;
