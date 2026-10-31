# Tasks

What's next after the Apple redesign and community widgets (steps 1 to 5 of
`design/community-plan.md` are built and tested). In order. **You** = needs your accounts or a
decision; **Claude** = can be done in a session.

## 1. Commit today's work

Done on branch `community-widgets` (`main` is untouched), four commits:
app (design system, menus, plugins, app online), server, website, design docs. The app
changes share files, so they're one commit rather than seven; each commit builds.

- [x] **Claude:** create the branch and commit (F1 work kept out; HEAD builds without it)
- [ ] **You / F1 session:** commit the F1 work, still uncommitted on purpose: `src/Widgets/F1*.cs`,
      `design/f1-widget.html`, and its changes in `src/WidgetRegistry.cs` (Sports, f1-hub,
      f1-timing), `src/Widgets/CardWidget.cs` (`CardSize`, Large) and `src/Http.cs`
      (`Get(url, bearer)`, `PostForm`). `git diff` on those three files shows only F1 lines.
      The F1 session ended before it could be told.
- [ ] **You:** merge `community-widgets` into `main` (or open a PR) when you're happy with it
- [ ] **You:** check `CLAUDE.md` stays on the "right-click menus are app-drawn" version; if it
      reverts again, look at OneDrive version history or the other sessions

## 2. Security review before the repo goes public

- [x] **Claude:** review the branch (trust chain upload → approve → `deskwidgets://` → fetch →
      hash check → prompt → compile, RLS, invite function, website XSS, paths, parsing, secrets in
      the diff). One finding, fixed: hidden characters ("Trojan Source") could make reviewed code
      run differently from how it looked. Refused in the database, upload page and app; shown as
      markers in review views; the repo itself now has none (it had 26, written by tooling).
- [x] **Claude:** scan the whole git history for secrets: all 10 commits on every branch, added
      and removed lines and messages, 11 token patterns (checked against fake tokens). No secrets,
      no `widgets.ini` ever committed, no deleted files.
- [x] **You decided:** keep the first commit's author email as it is.
- [x] **Claude:** hidden-character check as a repo check: `tools/check-hidden.ps1` runs in
      `build.bat` and in the pre-commit hook (`.githooks/pre-commit`; each clone turns it on with
      `git config core.hooksPath .githooks`, already on here)

## 3. Make the community gallery live

Steps are in `server/README.md`.

Blocker found: the repo is private, and on a free GitHub account Pages only serves public repos
(release downloads also need a public repo). So the site can't go live until:

- [x] **You:** made the repo public and merged PR #1 into `main`
- [x] GitHub Pages serves `docs/` from `main`: https://whoissaaif.github.io/desk-widgets/ (all pages
      load and point at the project)
- [x] Release v1.0.0 with `DeskWidgets.exe` (SHA-256 `728cbe29...`): the site's Download link
      resolves to it and an anonymous download is byte-identical

- [x] Supabase project `desk-widgets` (ref `vtgsbgkjleyqlxgjdvqy`, Sydney, free tier), created
      with the database password typed by you; "expose new tables" off
- [x] `server/schema.sql` run and checked live: RLS on all tables, 6 policies, anon read-only,
      `approve` closed to anon, hidden-character constraint active
- [x] Sign-ups off; Site URL and redirect URL `https://whoissaaif.github.io/desk-widgets`
- [x] `invite` Edge Function deployed (byte-identical to the repo) with the `SITE_URL` secret
- [ ] **You, if needed:** the function's "Verify JWT with legacy secret" is still on. If inviting
      an uploader fails with "Invalid JWT", turn it off (Edge Functions > invite > Settings); the
      function verifies the caller and checks `admins` itself
- [x] Owner: `saif` (0syedsaifuddin@gmail.com), invited and made admin. **You:** accept the
      invite email to set up the sign-in
- [x] Project URL and publishable key in `docs/config.js` and `src/Community.cs`; the app reads
      the live (empty) gallery
- [x] The function reads `SUPABASE_SECRET_KEYS` (legacy key only as a fallback)
- [ ] **Claude:** redeploy the `invite` function with that change (dashboard editor; needs the
      browser connection)
- [ ] **Claude:** live test end to end: upload `samples/HelloWidget.cs` on the website, approve it
      on `admin.html`, Get from Explore (website and app), update to a version 2, uninstall

## 4. Smaller follow-ups

- [x] Emoji in widget labels (calendar, reminders) draw in Segoe UI Emoji on the text baseline
      instead of boxes
- [ ] **Claude:** live-test light mode (library, menus, dialogs). Needs you to switch Windows to
      light mode, or OK for Claude to switch it and back.
- [x] Explore previews are fetched once per approved version and kept in
      `%LOCALAPPDATA%\DeskWidgets\previews` (7 checks: disk hit, no-preview marker, tampered
      file refetched, prune)
- [ ] **Claude:** right-click test on a community widget that sits on the desktop (its own menu
      items, e.g. Hello's "Say Hi"); blocked last time by an app window covering it

## Done (for reference)

- Apple design language everywhere; `DESIGN.md` retired; menus app-drawn (section 17)
- Community steps 1 to 5: local plugins, Supabase schema (36 policy checks), website,
  `deskwidgets://` install with hash check, Explore and Installed pages, update checks
- Live testing on the real app: menus (mouse, keyboard, edges, dismissal), URL hand-off,
  install, Show Source, uninstall, crash isolation; fixed the Appearance submenu and click bugs
