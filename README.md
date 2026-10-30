# Desk Widgets

Lightweight desktop widgets for Windows. Native C# on the .NET Framework 4 that ships with every
Windows 10/11 PC, so there is nothing to install. All widgets run in one process (~24 MB RAM, ~0.3% of one CPU core with a ticking clock).

## Build & run (any Windows PC)

```
git clone <this repo>
cd <repo>
build.bat
bin\DeskWidgets.exe
```

## Using it

- **Widget Library**: left-click the tray icon (or launch `DeskWidgets.exe` again). **Drag** a widget
  out onto the desktop, or **double-click** it to drop it mid-screen. The library is freed from memory when closed.
- **On the desktop**: drag a widget to move it. Right-click it for its options (city, size, seconds hand,
  face, opacity, lock, remove).
- **Tray menu**: Widget library, Start with Windows, Exit.

## Settings & portability

- Settings live in `%APPDATA%\DeskWidgets\widgets.ini` (one `[section]` per widget).
- **Portable mode**: if a `widgets.ini` exists next to `DeskWidgets.exe`, that file is used instead
  (handy on a USB stick).
- Positions are stored relative to the monitor (`fx`/`fy`, 0..1). A widget in a corner stays in that
  corner on any resolution, DPI, or machine, and re-snaps when the display changes.
- Sizes are in logical pixels and scale with DPI.

## Why it's light

- Per-pixel-alpha layered window (`UpdateLayeredWindow`) drawing into one reusable DIB, with no per-frame allocations.
- Static parts (background, dial, numbers) are rendered once and cached; each frame only draws the hands.
- Timers wake exactly once per second (or per minute with the seconds hand hidden); drawing pauses while the PC is locked.

## Adding a new widget

1. Create `src/Widgets/MyWidget.cs` deriving from `WidgetWindow` and implement:
   - `Measure()`: pixel size (use `Px()` for DPI scaling)
   - `Draw(Graphics g)`: draw the full frame (the surface is not cleared for you)
   - `NextFrameDelay()`: ms until the next redraw, or `-1` for on-demand only
   - `BuildMenu(items)`: optional right-click options, stored with `Settings.Set(...)`
2. Register it in `src/WidgetRegistry.cs` (name, description, optional preset `Defaults`).
   It shows up in the library automatically.
3. Run `build.bat`.

Code must stay C# 5 (the compiler built into Windows): no `$"..."`, `?.`, or `=>` members.
