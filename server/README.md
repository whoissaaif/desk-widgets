# Community backend (Supabase)

Everything the Explore gallery, uploads and reviews need. The design is in
`design/community-plan.md`. Every permission is enforced by row level security in `schema.sql`,
which is why the website and the app can ship the public anon key.

## Set up once

1. Create a Supabase project (free tier is fine).
2. **SQL editor:** run `schema.sql`. It's safe to run again after changes.
3. **Authentication > Sign In / Providers:** keep Email on, and turn **off** "Allow new users to
   sign up". Only people you invite get accounts.
4. **Authentication > URL Configuration:** set the Site URL to
   `https://whoissaaif.github.io/desk-widgets` and add `https://whoissaaif.github.io/desk-widgets/**`
   to the redirect URLs.
5. **Edge Function:** with the Supabase CLI,
   `supabase functions deploy invite --project-ref <ref>` from this folder, then
   `supabase secrets set SITE_URL=https://whoissaaif.github.io/desk-widgets --project-ref <ref>`.
   The service key stays in Supabase; it never goes in the repo.
6. **Make yourself the owner** (SQL editor, once): invite your own email from the dashboard
   (Authentication > Users > Invite), then
   ```sql
   insert into public.profiles (id, username) select id, 'your-name' from auth.users where email = 'you@example.com';
   insert into public.admins (user_id) select id from public.profiles where username = 'your-name';
   ```
7. Put the project URL and the **anon** key into `docs/config.js` (website) and
   `src/Community.cs` (app). Both are public by design.

## Tests

`test/schema.test.mjs` runs `schema.sql` in Postgres 16 (PGlite, no server needed) with a stub of
Supabase's `auth` schema and its default grants, then makes 36 checks: who can read, insert and
approve what, id ownership, immutability, version numbering and the hash check.

```
cd server/test && npm install && npm test
```
