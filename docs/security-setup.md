# Security fixes — local review, not deployed

Based on upstream main commit 0d0081feb628f04248a00685079d64702343c7c0.
This folder is a source snapshot obtained through the GitHub connection, with a local
baseline commit for review. It is not a clone of the remote history. The review branch
preserves the upstream commit as its parent. Publishing it does not deploy these changes.

## What changed

- Supabase password sign-in in React; no registration or anonymous sign-in UI.
- Access tokens stay in memory, expire, and are sent only as Bearer headers.
  Refreshing/closing the page signs out locally. Sign-out also requests local-session
  logout from Supabase; issued access tokens can remain valid until expiry.
- .NET verifies each token using the configured Supabase /auth/v1/user endpoint.
  It does not trust browser state, unverified JWT content, or user-editable metadata.
- Both default and fallback authorization require an explicitly approved Auth user ID.
  Empty StaffUserIds denies everyone. Restart the API after changing the list.
  All approved staff retain the existing list/create/delete capabilities; owner-only
  deletion is a separate business-policy change and is not introduced here.
- Invalid, expired, anonymous, and unverifiable sessions fail closed. Valid accounts
  outside the allowlist receive 403. Auth outages temporarily prevent API access.
- The standalone Daily Sales viewer still lists receipts, now through the protected
  API after sign-in. It uses textContent instead of interpolating customer HTML.
  runtime-config.js contains only public configuration. Both viewers require setup.
- Removed committed connection defaults and the certificate-trust bypass.
- Added Vite type declarations and changed the broken lint command to type-checking.

## Isolated setup

Use a separate TEST Supabase project/database with artificial receipts. Do not use
production credentials for these checks. No live settings, users, grants, or data
have been modified by this change.

1. Install the .NET 8 SDK and Node.js. Use the SDK, not only the runtime.
2. Configure backend user-secrets from src/RaLaba.API, or server environment variables:
   - ConnectionStrings__DefaultConnection: restricted application database credentials;
     use SSL Mode=VerifyFull and a valid CA configuration for hosted PostgreSQL.
   - SupabaseAuth__Url: the HTTPS test-project origin, with no path.
   - SupabaseAuth__PublishableKey: its public publishable key (legacy anon also works).
   - SupabaseAuth__StaffUserIds__0: the UUID of an explicitly approved test staff user.
   - AllowedOrigins__0: the exact frontend origin.
3. Create/invite test staff through Supabase separately. Never put a service-role key,
   database password, or Loyverse token into VITE_ variables or runtime-config.js.
4. Review docs/security-review.sql against the test database first. It proposes a
   restricted ralaba_api database role and revokes direct anon/authenticated/PUBLIC
   receipts access. Set the role password privately, then use it in the backend.
   Do not use postgres or a BYPASSRLS role. The script was NOT run, even locally.
5. Inspect existing grants, inherited roles, table ownership, views and RPC functions.
   A view or SECURITY DEFINER function can provide another route to the same data.
   Check that ralaba_api is not a table owner and has no privileged memberships.
6. Copy frontend/.env.example to frontend/.env and set only the test public values.
   Configure runtime-config.js likewise if using the legacy static viewer.
7. Build and test:
   - dotnet test tests/RaLaba.UnitTests/RaLaba.UnitTests.csproj
   - cd frontend; npm install; npm run lint; npm run build; npm test
   Review and commit the generated frontend package-lock.json after a successful
   dependency install. No verified lockfile could be generated in this environment.
8. Start the test backend/frontend and confirm approved sign-in, 403 for unapproved
   users, 401 for missing/invalid tokens, expiration and sign-out removing receipts,
   and customer text such as <img src=x onerror=alert(1)> rendering literally.
9. In the TEST database, verify anon/authenticated cannot SELECT/INSERT/DELETE receipts,
   and the restricted backend role can perform existing receipt operations. Confirm
   no broad view/function grants bypass these rules. Run Supabase security advisors.

The integration tests use fake Auth responses and a strict mocked receipt service;
they make no production calls. Run the explicit test project because the existing
solution lacks Build.0 entries and may skip project builds in solution-wide runs.

## Verification in this environment

Node session tests pass. .NET is not installed, and the official SDK installer
download failed with DNS resolution unavailable. Frontend dependencies are not in
the local npm cache, so React/TypeScript builds and .NET tests could not be run.
No production security issue is claimed resolved until the reviewed changes are
tested and separately rolled out. Existing public database access remains unchanged.

## References

- https://supabase.com/docs/reference/javascript/auth-getuser
- https://supabase.com/docs/guides/api/securing-your-api
- https://learn.microsoft.com/aspnet/core/security/authentication/?view=aspnetcore-8.0
