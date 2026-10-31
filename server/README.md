# Community backend (Supabase)

Everything the Explore gallery, uploads and reviews need. The design is in
`design/community-plan.md`. Every permission is enforced by row level security in `schema.sql`,
which is why the website and the app can ship the public anon key.

## How accounts work

1. Anyone types their email on the Upload page (Request Access). It goes into `access_requests`
   through `request_access()`; nobody but the owner can read it.
2. The owner sees it under Access Requests on `admin.html`. **Accept** calls the `invite`
   function, which has Supabase email an invite; **Reject** just records it.
3. The invite link opens `account.html` signed in. There the person picks a username (permanent,
   part of their widget ids), a password, a bio and a photo. The profile row is what lets them upload.
4. From then on they sign in with username and password on `login.html` or in the app. The
   `login` function looks up the email for the username (emails are never public) and returns a
   session. "Forgot your password" emails a one-time link back to `account.html`.

## Set up once

1. Create a Supabase project (free tier is fine).
2. **SQL editor:** run `schema.sql`. It's safe to run again after changes.
3. **Authentication > Sign In / Providers:** keep Email on, and turn **off** "Allow new users to
   sign up". Only people you invite get accounts.
4. **Authentication > URL Configuration:** set the Site URL to
   `https://whoissaaif.github.io/desk-widgets` and add `https://whoissaaif.github.io/desk-widgets/**`
   to the redirect URLs.
5. **Edge Functions** (no CLI needed): Edge Functions > Deploy a new function > Via Editor, once
   per folder in `functions/`, with the folder's name:
   - `invite`: paste `functions/invite/index.ts`, deploy.
   - `login`: paste `functions/login/index.ts`, deploy, then in its Settings turn **off** "Verify
     JWT" (people call it before they have a session; it checks the password itself).

   Then Edge Functions > Secrets: add `SITE_URL` = `https://whoissaaif.github.io/desk-widgets`.
   Supabase provides the keys to the functions itself; they never go in the repo. (With the CLI
   instead: copy the folders to `supabase/functions/` first, since the CLI looks there, then
   `supabase functions deploy invite --project-ref <ref>` and
   `supabase functions deploy login --no-verify-jwt --project-ref <ref>`.)
   Supabase's built-in email sender allows only a few emails an hour; for more invites, add your
   own SMTP under Authentication > Emails.
6. **Make yourself the owner** (SQL editor, once): invite your own email from the dashboard
   (Authentication > Users > Invite), open the link to pick your username and password on
   `account.html`, then make that profile the owner:
   ```sql
   insert into public.admins (user_id) select id from public.profiles where username = 'your-name';
   ```
   (Before `account.html` existed, owners were made by hand:
   ```sql
   insert into public.profiles (id, username) select id, 'your-name' from auth.users where email = 'you@example.com';
   insert into public.admins (user_id) select id from public.profiles where username = 'your-name';
   ```
   An owner made that way sets a password with "Forgot your password" on `login.html`.)
7. Put the project URL and the public key into `docs/config.js` (website) and
   `src/Community.cs` (app). Use the **publishable** key (`sb_publishable_...`) or, on older
   projects, the **anon** key (`eyJ...`); both are public by design and both work. Never the
   secret / service_role key.
8. **Authentication > Emails > Templates:** paste the three templates from `email/`, each with the
   subject written at its top, so each email says what it's for:

   | Template | File | Button opens |
   |---|---|---|
   | Invite user | `email/invite.html` | `account.html`: choose a username and password |
   | Magic link | `email/sign-in-link.html` | the page the person asked to sign in to |
   | Reset password | `email/change-password.html` | `password.html`: choose a new password |

   "Forgot your password" on `login.html` sends the Reset password email; "Sign in with an email
   link instead" sends the Magic link email. A password link that lands on any other page (the
   dashboard's own "Send password recovery" goes to the Site URL) is moved to `password.html`.

The website is served by GitHub Pages from `docs/` on `main`. On a free GitHub account Pages only
serves public repositories, so the repo has to be public (or on GitHub Pro) before the site works,
and release downloads are only reachable by visitors from a public repo.

## Tests

`test/schema.test.mjs` runs `schema.sql` in Postgres 16 (PGlite, no server needed) with a stub of
Supabase's `auth` schema and its default grants, then makes 68 checks: who can read, insert and
approve what, id ownership, immutability, version numbering, the hash check, profiles and access
requests.

```
cd server/test && npm install && npm test
```
