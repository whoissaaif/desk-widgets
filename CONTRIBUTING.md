# Contributing

Thanks for helping. There are two ways to add to Desk Widgets.

## A widget of your own: upload it, no pull request needed

Most widgets belong in the community gallery, not in the app. Write one C# file (start from
`samples/HelloWidget.cs`), test it with Installed › Install from File…, then
[request access](https://whoissaaif.github.io/desk-widgets/upload.html). Once you're invited,
upload it from the website or the app. The owner reads every version before it appears in Explore.

## Changes to the app, website or server: a pull request

1. Open an issue first for anything bigger than a small fix, so we agree on the approach.
2. Fork, then branch from `main` (`fix-calendar-timezones`, `add-moon-widget`).
3. Turn on the repo's hooks once: `git config core.hooksPath .githooks`.
4. Make the change, keeping to the rules below, and open a pull request. CI builds the app and runs
   the database tests; both must pass. `main` only changes through pull requests.

### Rules

- **C# 5.** The app compiles with the C# compiler built into Windows (`build.bat`), so no
  `$"..."`, `?.`, `nameof` or `=>` members. No NuGet packages: one exe, nothing to install.
- **Light.** Widgets redraw only when something changes, and slow work runs off the UI thread.
  Measure memory and CPU before and after anything that runs all the time.
- **Design.** All UI follows `design/apple-design.md`; section 17 maps it onto this app. If you need
  a rule that isn't there, add it to section 17 in the same pull request. Colors, type and shapes
  come from `src/Theme.cs` (widgets: the palettes in `CardWidget`), never from hex values in UI code.
  Only the title bar and scrollbars are drawn by Windows; menus and dialogs are drawn by the app.
- **Writing.** Buttons and menu items in title style ("Add to Desktop"); messages are plain
  sentences that say what happened and what to do.
- **Security.** Community code runs on people's PCs, so changes to the install path (`Community.cs`,
  `Plugins.cs`), `server/schema.sql` or the Edge Functions need a test or a clear explanation of why
  they're safe. Every new database rule gets a check in `server/test/schema.test.mjs`.
- **No hidden characters.** `tools/check-hidden.ps1` (run by `build.bat` and the pre-commit hook)
  refuses characters that make code read differently from how it runs.
- **Secrets** never go in the repo. The Supabase publishable key in `docs/config.js` and
  `src/Community.cs` is public by design; secret keys live only in Supabase.

### Checking your change

```
build.bat
bin\DeskWidgets.exe
cd server\test && npm ci && npm test
```

For the website, serve `docs/` (for example `python -m http.server -d docs`) and open it in a
browser, in light and dark.

## Releases (owner)

Tag `vX.Y.Z` on `main` and publish a GitHub release with `bin\DeskWidgets.exe` attached under
exactly that name: the website's Download button points to
`releases/latest/download/DeskWidgets.exe`.
