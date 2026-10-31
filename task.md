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
- [ ] **You decide:** your personal Gmail address is the author of the very first commit
      (`04749ba`); every later commit uses the GitHub no-reply address. It becomes public with the
      repo. Removing it means rewriting history and force-pushing `main` (every commit id
      changes). Alternative: leave it.
- [x] **Claude:** hidden-character check as a repo check: `tools/check-hidden.ps1` runs in
      `build.bat` and in the pre-commit hook (`.githooks/pre-commit`; each clone turns it on with
      `git config core.hooksPath .githooks`, already on here)

## 3. Make the community gallery live

Steps are in `server/README.md`.

- [ ] **You:** create the Supabase project (free tier)
- [ ] **You:** run `server/schema.sql` in the SQL editor
- [ ] **You:** Authentication: turn off sign-ups; set Site URL and redirect URLs to
      `https://whoissaaif.github.io/desk-widgets`
- [ ] **You:** deploy the `invite` Edge Function and set `SITE_URL` (Supabase CLI)
- [ ] **You:** invite yourself and run the two SQL lines that make you the owner
- [ ] **You:** send Claude the project URL and the **anon** key (never the service key)
- [ ] **Claude:** put them in `docs/config.js` and `src/Community.cs`, rebuild
- [ ] **Claude** (after you confirm, since it's public): turn on GitHub Pages for `docs/`
- [ ] **Claude** (after you confirm): publish a GitHub Release with `DeskWidgets.exe`
- [ ] **Claude:** live test end to end: upload `samples/HelloWidget.cs` on the website, approve it
      on `admin.html`, Get from Explore (website and app), update to a version 2, uninstall

## 4. Smaller follow-ups

- [ ] **You decide, Claude builds:** emoji in Calendar event titles show as "□□" (GDI+ can't
      draw emoji). Option A: strip them (quick). Option B: render text with emoji support
      (bigger).
- [ ] **Claude:** live-test light mode (library, menus, dialogs). Needs you to switch Windows to
      light mode, or OK for Claude to switch it and back.
- [ ] **Claude:** cache Explore previews on disk (`%LOCALAPPDATA%\DeskWidgets\previews`) so
      they aren't downloaded every time the window opens
- [ ] **Claude:** right-click test on a community widget that sits on the desktop (its own menu
      items, e.g. Hello's "Say Hi"); blocked last time by an app window covering it

## Done (for reference)

- Apple design language everywhere; `DESIGN.md` retired; menus app-drawn (section 17)
- Community steps 1 to 5: local plugins, Supabase schema (36 policy checks), website,
  `deskwidgets://` install with hash check, Explore and Installed pages, update checks
- Live testing on the real app: menus (mouse, keyboard, edges, dismissal), URL hand-off,
  install, Show Source, uninstall, crash isolation; fixed the Appearance submenu and click bugs
