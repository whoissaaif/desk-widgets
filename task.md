# Tasks

What's next after the Apple redesign and community widgets (steps 1 to 5 of
`design/community-plan.md` are built and tested). In order. **You** = needs your accounts or a
decision; **Claude** = can be done in a session.

## 1. Commit today's work (do first)

Everything is uncommitted, and `CLAUDE.md` was already rolled back once (OneDrive or another
session). Another session (F1 widgets) works in the same folder; its files go in its own commits.

- [ ] **Claude:** create a branch (e.g. `community-widgets`) and commit in pieces:
  - [ ] Design system: `design/apple-design.md` (section 17), `CLAUDE.md`, `src/Theme.cs`,
        `src/Controls.cs`, the mockup accent in `design/essential-widgets.html`, `README.md`
  - [ ] Menus: `src/PopupMenu.cs` (app-drawn Apple menus), menu label changes in widgets
  - [ ] App UI: `src/LibraryWindow.cs`, `src/AlertDialog.cs`, `src/InputDialog.cs`
  - [ ] Local plugins: `src/PluginApi.cs`, `src/Plugins.cs`, `src/WidgetRegistry.cs`,
        visibility changes, `samples/HelloWidget.cs`
  - [ ] Server: `server/` (schema, invite function, tests)
  - [ ] Website: `docs/`
  - [ ] App online: `src/Community.cs`, URL protocol and hand-off in `src/App.cs`
- [ ] **Claude:** leave F1 files (`src/Widgets/F1*.cs`, `design/f1-widget.html`, its
      `CardWidget` / `WidgetRegistry` / `Http.cs` changes) to the F1 session
- [ ] **You:** check `CLAUDE.md` stays on the "right-click menus are app-drawn" version; if it
      reverts again, look at OneDrive version history or the other sessions

## 2. Security review before the repo goes public

- [ ] **Claude:** scan the whole git history for secrets (tokens, keys, private calendar links,
      `widgets.ini`)
- [ ] **Claude:** review the trust chain: upload → approve (hash pinned) → `deskwidgets://` →
      fetch → hash check → prompt → compile; plus RLS in `server/schema.sql`
- [ ] **Claude:** confirm only the anon key appears anywhere public; the service key never

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
