# Community widgets: accounts, uploads, Explore

Status: steps 1 to 5 are built. What's left needs accounts only the owner has: create the
Supabase project, run `server/schema.sql`, deploy the invite function, put the URL and anon key in
`docs/config.js` and `src/Community.cs`, turn on GitHub Pages for `docs/`, and publish a release
(`server/README.md` has the steps).

### What was built

- **Step 1, local plugins:** API v1 (`PluginApi.cs`; `WidgetWindow`, `Section`, `PopupMenu`, `Http`
  public), `[DeskWidget]`, registry as a list with `Changed`, compile and cache (`Plugins.cs`), hash
  check, crash marker and both exception handlers, install from a `.cs` file, update, uninstall,
  turn off / try again. Sample: `samples/HelloWidget.cs`.
- **Step 2, Supabase:** `server/schema.sql` (tables, RLS, `approve` / `reject`, the `explore` and
  `authors` views) and the `invite` Edge Function. `server/test/schema.test.mjs` runs the schema in
  Postgres 16 (PGlite) and checks 36 permission rules.
- **Step 3, website** (`docs/`): Explore, widget page with source and history, upload, admin review
  with a line diff and hash-pinned approval, invites.
- **Step 4, app online** (`Community.cs`): `deskwidgets://install/<id>/<version>` registered per
  user, handed to the running copy with `WM_COPYDATA`, parsed strictly, then fetched, hash-checked
  and confirmed. Explore and Installed pages in the library window.
- **Step 5, updates:** checked 30 s after start and daily; Installed shows "Update", never applied
  on its own.

### Decisions made while building

- Local installs get the id `u.local.<file-name-slug>` and no category (they show under Installed
  only); the hash of the text you confirmed counts as the reviewed hash. Installing the same file
  name again makes it the next version.
- csc takes the assembly name from the output file, so it compiles to `<id>_<hash8>.dll` and the
  bytes are saved as `widget.dll`; `build.key` holds the source hash and host exe hash.
- `WidgetWindow` members only the app needs (`MoveTo`, `Reposition`, `SetPaused`, `Snapshot`,
  `BackdropChanged`) are internal; `Dpi` is read-only to plugins. `PopupMenu.AddBold` is gone
  (Apple menus have no bold items); `AddDestructive` is new.
- A plugin that throws outside a guarded call is turned off once the current window message has
  finished, not from inside it.
- Previews are base64 PNG text (`preview text`), not `bytea`: PostgREST returns `bytea` as hex,
  which neither the site nor the app can use directly. Decoded in the app with a size cap.
- Versions are numbered by a trigger; uploaders can't choose them. The policies on `widgets` and
  `versions` read each other through `security definer` helpers to avoid policy recursion.
- The admin page calls the `invite` Edge Function directly rather than through an RPC.
- Not built: a disk cache for Explore previews (they're fetched each time the window opens).

## Goal

Anyone can browse an Explore gallery and install widgets into Desk Widgets with one click, without
an account. Only people the owner selects (uploaders) can sign in and upload widgets they wrote.

Decisions already made:

- Widgets are real C# code (same power as the built-in ones), not a declarative format.
- Backend is Supabase (auth and Postgres), free tier.
- The repo is public and the website is on GitHub Pages.
- Explore exists in two places: the website and an Explore tab inside the app.

## Who can do what

| | Browse and install | Upload | Approve, invite |
|---|---|---|---|
| Anyone (no account) | yes | no | no |
| Uploader (invited by owner) | yes | yes | no |
| Owner (admin) | yes | yes | yes |

- Accounts exist only for uploaders. Supabase sign-ups are disabled; the owner invites uploaders by
  email from `admin.html` and sets their username at invite time.
- Reading approved widgets needs no account: the `anon` role can read approved rows and nothing else.
  Pending and rejected uploads are visible only to their author and the owner.
- Sign-in (uploaders and owner, website only) is email magic link. (GitHub OAuth with sign-ups
  disabled needs identity linking for invited users; revisit later.)
- The Supabase URL and anon key are committed in the public repo; that's expected and safe because
  every permission is enforced by RLS. The service key never goes in the repo.
- Since the repo is public, people get the app itself from GitHub Releases (a built
  `DeskWidgets.exe` attached to each release).

## Threat model

A C# widget runs inside DeskWidgets.exe with the user's full rights: it can read files, read the
DPAPI-protected secrets (GitHub token, calendar links), or install malware. .NET Framework 4 has no
sandbox worth relying on. Installs are now open to strangers, so a bad widget reaches people the
owner doesn't know. The realistic attackers are a compromised uploader account, a widget that changes
after it was reviewed, and any web page firing the install link. So the whole design rests on one
guarantee:

**The bytes compiled on a PC are exactly the bytes the owner read and approved.**

1. **A widget version is one C# source text, stored in Postgres.** No zips, no DLLs, no Storage for
   code. Max 512 KB. This removes zip-slip, hidden files, duplicate entries and upload-after-review.
2. **Nothing is installable until the owner approves it.** New versions land as `pending`.
3. **Approval pins the hash in the database.** The admin page renders the source it fetched and
   calls `approve(widget_id, version, sha256)` with the hash of that text. The RPC (security
   definer, admin-only) recomputes `sha256(source)` in Postgres and fails if it differs, so the
   approved hash always matches what the owner saw.
4. **Versions are immutable.** Uploaders can only insert pending rows; they have no UPDATE or DELETE
   on versions. A change is a new version that goes through review again.
5. **The app compiles locally**, after checking `sha256(source)` against the approved hash, with the
   C# compiler that ships with Windows (`CSharpCodeProvider`, same csc as build.bat).
6. **Install always asks.** A native confirm dialog shows author, version and a "View source" button
   that shows the verified local text (not a web page). Cancel is the default button.

## Plugin API (in the app)

### Public surface, "API v1"

Today every type in `src` is internal, so nothing compiled outside the exe can use them. Make a small,
deliberate set public and treat it as frozen:

- `WidgetWindow` (constructor, `Draw`, `BuildMenu`, size/settings helpers plugins need)
- `Section` (`Get`, `GetInt`, `GetFloat`, `GetBool`, `GetText`, `Set`, `SetText`), `Values` stays internal
- `PopupMenu` (what `BuildMenu` takes)
- `Http` (`Get`, `GetBytes`)

`Secrets`, `App`, `Config` and `WidgetRegistry` stay internal. `DeskWidgets.PluginApi.Version = 1`
is a public constant; each widget declares the API version it targets and the app refuses newer ones.

### Shape of a plugin

```csharp
// The id is NOT in the code. It comes from the approved row: u.<username>.<slug>
[DeskWidget(Name = "Moon phase", Description = "Tonight's moon")]
public sealed class MoonPhase : WidgetWindow { public MoonPhase(Section s) : base(s) { ... } }
```

- Exactly one `[DeskWidget]` type per source, or the compile result is rejected.
- Id, category and API version come from the database row, never from the code.
- Ids must match `u.[a-z0-9-]+.[a-z0-9-]+`, max 64 chars. They're also folder names, so this check
  happens everywhere an id enters the app (URL, API response, disk).
- A plugin can never replace an existing registry entry, built-in or not.

### Registry

`WidgetRegistry.Types` becomes a list (built-ins + plugins) that raises `WidgetsChanged` when
plugins are installed or removed, so the library window refreshes.

### Loading and caching

- Cache: `%LOCALAPPDATA%\DeskWidgets\plugins\<id>\<version>\` holding `source.cs` and `widget.dll`.
- Cache key is source hash + host exe hash. On mismatch, recompile; a failed compile disables the
  plugin with a message ("Moon phase needs an update for this version of Desk Widgets").
- Each build gets a unique assembly name (`<id>_<hash8>`) and is loaded with
  `Assembly.Load(File.ReadAllBytes(dll))`, so the file is never locked and a new version can load in
  the same session. (.NET Framework can't unload; the old one stays in memory until restart.)

### Crash isolation

Plugin code can't be fully contained, so the app detects crashes and disables the culprit:

- **Crash marker.** Before running plugin code that can crash the process (constructor, first
  `Render`, library `Snapshot`), write `loading=<id>` to `plugins\crash.marker`; delete it after.
  On startup, if the marker exists, disable that plugin and tell the user why.
- `OpenAll()` and `Snapshot()` wrap each plugin in try/catch; a throwing plugin is closed and disabled.
- Add `Application.ThreadException` and `AppDomain.UnhandledException` handlers. They record the
  plugin from the exception's stack (assembly name) and disable it before exit.
- Uncatchable failures (stack overflow, endless loop on the UI thread) are covered by the marker
  on the next start.

### Uninstall

Close its windows, remove its sections from widgets.ini, unregister the type, delete the folder
(retry on next start if anything still holds it).

## Backend: Supabase

All SQL lives in `server/schema.sql` (tables, policies, RPCs), applied by hand in the SQL editor.

```sql
profiles (id uuid pk = auth.users.id,
          username text unique not null check (username ~ '^[a-z0-9-]{2,24}$'),  -- immutable
          created_at)
admins   (user_id uuid pk -> profiles)
widgets  (id text pk check (id ~ '^u\.[a-z0-9-]+\.[a-z0-9-]+$'),
          owner uuid not null -> profiles, slug text, created_at)
versions (widget_id text -> widgets, version int,               -- 1, 2, 3...
          name, category, description text, api_version int,
          source text not null check (octet_length(source) <= 524288),
          preview bytea check (octet_length(preview) <= 102400),   -- small PNG
          status text not null default 'pending' check in ('pending','approved','rejected'),
          sha256 text, reviewed_at timestamptz, created_at,
          primary key (widget_id, version))
```

Name, category, description and preview live on each version, so they're reviewed with the code.
The preview is a column rather than a Storage file, so the same row policy covers it: there is no
bucket, and an unapproved preview can never be fetched by guessing a URL.

Policies (RLS on everywhere):

- Read `profiles`: everyone, `anon` included, but only `username` (via a view), to show authors.
- Read `versions`: everyone, `anon` included, sees approved rows; uploaders also see their own rows
  in any status; admins see all.
- Read `widgets`: everyone sees rows with an approved version; uploaders their own; admins all.
- `anon` has SELECT only, no INSERT, UPDATE, DELETE or RPC grants.
- Insert `widgets`: `owner = auth.uid()` and `id = 'u.' || <your username> || '.' || slug`, compared
  with `=`, never `LIKE`. Usernames are immutable, so the prefix can't be reclaimed.
- Insert `versions`: only for widgets you own, `WITH CHECK (status = 'pending' and sha256 is null
  and reviewed_at is null)`.
- No UPDATE or DELETE grants on any table for `authenticated`. Changes go through RPCs:
  - `approve(widget_id, version, sha256)` and `reject(widget_id, version)`: admin only.
  - `invite(email, username)`: admin only (calls the Supabase admin API from an Edge Function,
    since inviting needs the service key, which lives only in Supabase's Edge Function secrets).
- Only `profiles` rows exist for invited users, and the insert policies above require one, so a
  stray `auth.users` account (if sign-ups were ever re-enabled by mistake) still can't upload.

## Website: GitHub Pages from the public repo (`docs/` folder on `main`)

Static HTML/JS calling Supabase with the anon key. Lives at `whoissaaif.github.io/desk-widgets`.

- `/` Explore grid: search, category filter, preview, author. No sign-in.
- `/widget.html?id=` details, source viewer, version history, Install button. No sign-in.
- "Get Desk Widgets" link to the latest GitHub Release, for visitors who don't have the app yet.
- `/upload.html` (uploaders, signed in) paste or pick a `.cs` file, fill
  name/category/description, optional preview; inserted as a pending version.
- `/admin.html` (owner only) pending queue: source with a diff against the previous approved
  version, Approve/Reject, and Invite a friend.
- Install button opens `deskwidgets://install/<id>/<version>`.

## The app's URL protocol

The app never signs in: browsing and installing only read approved rows, which `anon` can do. That
removes sign-in, tokens and session storage from the app entirely.


- On first run the app registers `deskwidgets://` under HKCU (no admin rights), next to the Start
  menu entry it already creates.
- **Getting the URL to the running app.** The second process started by the browser sends the URL
  to the running instance with `WM_COPYDATA` to its message window, then exits. (Today it can only
  signal "show library" through an `EventWaitHandle`, which carries no data.)
- **Every URL is untrusted**, since any web page can open one. The app validates id/version format,
  re-fetches status, hash, author and source from Supabase itself, and always shows the confirm
  dialog. A URL alone never installs anything.
- `install` is the only URL the app accepts; anything else is ignored.

## In-app Explore tab

- New "Explore" and "Installed" entries in the `LibraryWindow` sidebar, below the categories.
- Explore lists approved widgets from Supabase REST (`Http` on a background thread, JSON via
  `JavaScriptSerializer`, already referenced), previews cached on disk.
- Install: fetch the version row, check `sha256(source)`, confirm dialog, compile, register. The card
  then appears in its category and can be dragged onto the desktop like any built-in.
- No sign-in in the app. Uploading happens on the website; the app's "Upload a widget" link opens it.

## Updates

On startup (and daily) the app checks for newer approved versions of installed plugins and shows
"Update available" under Installed. Updates are never applied silently, since a new version is
new code.

## Build order

1. **Local plugins, no network.** Public API v1, `[DeskWidget]`, registry as a list, compile and
   cache, crash marker and handlers, install from a local `.cs` file, uninstall.
2. **Supabase.** `server/schema.sql` with tables, policies and RPCs; invite Edge Function.
3. **Website.** Sign-in, Explore, details, upload, admin queue.
4. **App online.** `deskwidgets://` protocol, `WM_COPYDATA` hand-off, Explore and Installed tabs.
   Publish the exe on GitHub Releases.
5. **Updates.**

## Out of scope for v1

Download counts, ratings/comments, GitHub sign-in, sandboxing, automatic updates.
