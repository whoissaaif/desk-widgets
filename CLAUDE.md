# Desk Widgets

One design language for everything: `design/apple-design.md`. Read it before changing any UI
(widgets, the library window, dialogs, right-click menus, the website or design mockups) and
follow it. Section 17 maps it onto this app; when a change needs a rule that isn't there, add it
to section 17 in the same change. In code, colors, type and shape come from `src/Theme.cs`
(widgets: the palettes in `CardWidget`), never from hex values scattered in UI code.
`design/DESIGN.md` is retired; don't follow it or add rules to it.

Only the window title bar and scrollbars are drawn by Windows. Everything else, right-click menus
included, is drawn by the app in the Apple style.
