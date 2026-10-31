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
5. **Edge Function** (no CLI needed): Edge Functions > Deploy a new function > Via Editor, name it
   `invite`, paste `functions/invite/index.ts`, deploy. Then Edge Functions > Secrets: add
   `SITE_URL` = `https://whoissaaif.github.io/desk-widgets`. Supabase provides the service key to
   the function itself; it never goes in the repo. (With the CLI instead: copy the folder to
   `supabase/functions/invite` first, since the CLI looks there, then
   `supabase functions deploy invite --project-ref <ref>`.)
6. **Make yourself the owner** (SQL editor, once): invite your own email from the dashboard
   (Authentication > Users > Invite), then
   ```sql
   insert into public.profiles (id, username) select id, 'your-name' from auth.users where email = 'you@example.com';
   insert into public.admins (user_id) select id from public.profiles where username = 'your-name';
   ```
7. Put the project URL and the public key into `docs/config.js` (website) and
   `src/Community.cs` (app). Use the **publishable** key (`sb_publishable_...`) or, on older
   projects, the **anon** key (`eyJ...`); both are public by design and both work. Never the
   secret / service_role key.

The website is served by GitHub Pages from `docs/` on `main`. On a free GitHub account Pages only
serves public repositories, so the repo has to be public (or on GitHub Pro) before the site works,
and release downloads are only reachable by visitors from a public repo.

## Tests

`test/schema.test.mjs` runs `schema.sql` in Postgres 16 (PGlite, no server needed) with a stub of
Supabase's `auth` schema and its default grants, then makes 36 checks: who can read, insert and
approve what, id ownership, immutability, version numbering and the hash check.

```
cd server/test && npm install && npm test
```
