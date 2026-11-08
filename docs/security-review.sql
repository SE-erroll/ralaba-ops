-- REVIEW ONLY. Not applied by this change. Test in an isolated database first.
-- Backend-only receipts access: browser clients must use the protected .NET API.
-- First inspect existing roles, grants, policies, views, RPCs, and dependent apps.
begin;
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'ralaba_api') then
    create role ralaba_api login nosuperuser nocreatedb nocreaterole noinherit nobypassrls;
  end if;
  if exists (select 1 from pg_roles where rolname = 'ralaba_api'
             and (rolsuper or rolbypassrls or rolcreaterole or rolcreatedb)) then
    raise exception 'ralaba_api must be a restricted application role';
  end if;
end $$;
-- Set its password privately; do not put a password in source control.
grant usage on schema public to ralaba_api;
revoke all privileges on table public.receipts from public, anon, authenticated;
alter table public.receipts enable row level security;
grant select, insert, update, delete on table public.receipts to ralaba_api;
drop policy if exists ralaba_backend_receipts on public.receipts;
create policy ralaba_backend_receipts on public.receipts
  for all to ralaba_api using (true) with check (true);
commit;
-- Inspect all other policies and SECURITY DEFINER functions separately.
-- Existing views/RPCs can create alternate paths; this script alone does not audit them.
