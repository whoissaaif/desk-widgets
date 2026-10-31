# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & test

```
build.bat                              # builds bin\DeskWidgets.exe (C# 5, .NET Framework 4, no NuGet)
bin\DeskWidgets.exe                    # run (adds itself to Start menu on first launch)
bin\DeskWidgets.exe --background       # launch without opening the library window (Start with Windows mode)
cd server\test && npm ci && npm test   # database schema tests (PGlite, Node)
```

There is no solution/csproj file. `build.bat` calls the .NET Framework csc.exe that ships with
Windows and compiles `/recurse:src\*.cs` into one exe. If the exe is locked (app is running), the
build fails on the output file, not on a compile error — kill the process first.

For the website, serve `docs/` locally (e.g. `python -m http.server -d docs`) and check both
light and dark themes.

Pre-commit hook: `git config core.hooksPath .githooks` (runs `tools\check-hidden.ps1` to reject
characters that make code read differently from how it compiles).

## Language constraints

**C# 5 only.** The compiler is the one built into Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).
No string interpolation (`$"..."`), null-conditional (`?.`), `nameof`, expression-bodied members
(`=>`), or anything from C# 6+. No NuGet — the exe has zero dependencies.

## Design rules

One design language for everything: `design/apple-design.md`. Read it before changing any UI
(widgets, the library window, dialogs, right-click menus, the website or design mockups) and
follow it. Section 17 maps it onto this app; when a change needs a rule that isn't there, add it
to section 17 in the same change. In code, colors, type and shape come from `src/Theme.cs`
(widgets: the palettes in `CardWidget`), never from hex values scattered in UI code.
`design/DESIGN.md` is retired; don't follow it or add rules to it.

Only the window title bar and scrollbars are drawn by Windows. Everything else, right-click menus
included, is drawn by the app in the Apple style.

## Architecture

### The app (`src/`)

Single-process, single-exe WinForms app. No designer files — all UI is hand-coded GDI+.

- **`App.cs`** — `ApplicationContext` subclass; owns the tray icon, the widget list, wallpaper
  watcher, calendar events, session lock/unlock. The singleton `App.Current` is the backbone.
- **`WidgetWindow.cs`** — public abstract base for every widget. Per-pixel-alpha layered window
  (`UpdateLayeredWindow`) pinned to the desktop z-order via Progman ownership. Handles
  drag-to-move, right-click menus, render loop, DPI scaling. Community plugins derive from this.
- **`CardWidget.cs`** (in `DeskWidgets.Widgets` namespace) — abstract base for Apple-style card
  widgets. Provides Dark/Light/Vibrant palettes, small/medium/large sizes, frosted backdrop,
  font caching, emoji rendering, drawing helpers. Most built-in widgets extend this.
- **`WidgetRegistry.cs`** — static catalog of `WidgetType` entries grouped by `WidgetCategory`.
  Built-in widgets go in the `BuiltIn` array; community widgets are added/removed at runtime via
  `Register`/`Unregister`.
- **`LibraryWindow.cs`** — the main app window (sidebar + grid of cards). Only exists while open.
  Also defines the painted controls: `SidebarItem`, `WidgetCard`, `ExploreCard`, `PluginRow`, etc.
- **`Plugins.cs`** — community widget lifecycle: compile `.cs` against the running exe, hash-check
  against the reviewed version, crash isolation via a marker file. Installed under
  `%LOCALAPPDATA%\DeskWidgets\plugins\<id>\`.
- **`Community.cs`** — reads the Supabase gallery (public anon key, RLS), downloads previews,
  installs approved versions. Handles `deskwidgets://install/<id>/<version>` URLs.
- **`PluginApi.cs`** — the public API surface that community widgets compile against (API v1):
  `WidgetWindow`, `Section`, `PopupMenu`, `Http`. Treat as frozen; changing a public signature
  breaks every installed widget.
- **`DesktopBackdrop.cs`** — keeps one 1/12-scale copy of the wallpaper; vibrant widgets sample
  a piece of it. Rebuilds asynchronously off the UI thread when the wallpaper changes.
- **`Theme.cs`** — all colors, fonts and shapes for the app's own UI (library, dialogs, menus).
  Follows Windows light/dark. Widgets have separate palettes in `CardWidget`.
- **`Config.cs`** — INI-format settings; `Section` is the public per-widget settings bag.
- **`PopupMenu.cs`** — app-drawn context menus (Apple-style, not native Win32 menus).

### Rendering model

Widgets are `Form` subclasses with `WS_EX_LAYERED`. Each has a `LayeredSurface` (a single
DIB section). `Draw()` paints into it, then `UpdateLayeredWindow` composits it onto the desktop.
No per-frame allocations. Widgets redraw only when something changes; drawing pauses while the
PC is locked. Slow work (network, disk, image decoding) runs on background threads.

### Community widget compile path

`Plugins.Build()` → `CSharpCodeProvider` (the .NET Framework csc) → DLL bytes → `Assembly.Load`
from bytes (no file lock) → find the `[DeskWidget]` class → compiled delegate for its constructor.
The source's SHA-256 must match the reviewed hash from the gallery.

### Server (`server/`)

- `schema.sql` — Supabase Postgres schema (RLS, `explore` view, `versions` table).
- `test/schema.test.mjs` — PGlite tests for RLS and schema rules. Every new DB rule needs a test.

### Website (`docs/`)

Static site served by GitHub Pages from `main`. `docs/assets/site.js` has the shared JS;
individual pages (explore, upload, profile, admin, login, etc.) are self-contained HTML.
`docs/config.js` has the publishable Supabase key.

## Performance expectations

~33 MB memory, ~0.3% CPU with seven widgets on-screen. Measure before and after anything that
runs continuously. Widgets that animate should check `IsCovered()` and skip frames when hidden
behind other windows.

## Widget sizing

Apple's widget specs: small 170 x 170 pt, medium 364 x 170 pt, large 364 x 382 pt. 22 pt corner
radius, 16 pt margins. Sizes scale with DPI via `P(pt)` in CardWidget or `Px(logical)` in
WidgetWindow.

## Adding a new built-in widget

1. Create `src/Widgets/MyWidget.cs` — derive from `CardWidget` (cards) or `WidgetWindow` (custom).
2. Register in `WidgetRegistry.cs` with category, name, description.
3. `build.bat` — it appears in the library automatically.

## Settings & data paths

- Settings: `%APPDATA%\DeskWidgets\widgets.ini` (or `widgets.ini` next to the exe for portable mode).
- Plugin source + DLLs: `%LOCALAPPDATA%\DeskWidgets\plugins\<id>\`.
- Preview cache: `%LOCALAPPDATA%\DeskWidgets\previews\`.
- Widget caches (GitHub, weather): `%APPDATA%\DeskWidgets\cache\`.
- Account token: `%APPDATA%\DeskWidgets\account.dat` (DPAPI-encrypted).
