-- Current schema, as defined in Supabase
create table receipts (
  id uuid primary key default gen_random_uuid(),
  receipt_no text,
  date timestamp not null,
  employee text,
  customer text,
  type text default 'Sale',
  amount numeric(10,2) not null,
  payment_method text check (payment_method in ('CASH','GCASH')),
  paid boolean default true,
  paid_date date,
  notes text,
  created_at timestamp default now()
);

-- TODO: tighten RLS before going beyond local dev.
-- alter table receipts enable row level security;
-- create policy "Authenticated users can read their branch receipts"
--   on receipts for select
--   using (auth.role() = 'authenticated');
