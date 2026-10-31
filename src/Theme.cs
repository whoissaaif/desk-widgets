using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace DeskWidgets
{
    // design/apple-design.md as the app's own UI uses it (library window, dialogs, menus). Every
    // color, font and radius comes from here. Light and dark follow the Windows app theme, the way
    // Apple apps follow the system appearance. Widgets have their own palettes in CardWidget.
    static class Theme
    {
        static bool dark = ReadDark();
        public static bool Dark { get { return dark; } }

        // Raised when Windows switches between light and dark.
        public static event Action Changed;

        public static void Refresh()
        {
            bool d = ReadDark();
            if (d == dark) return;
            dark = d;
            if (Changed != null) Changed();
        }

        static bool ReadDark()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && k.GetValue("AppsUseLightTheme") is int && (int)k.GetValue("AppsUseLightTheme") == 0;
        }

        static Color Pick(int light, int darkRgb) { return Rgb(dark ? darkRgb : light); }
        public static Color Rgb(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        // ---- color (§4, web apps §2.9) ----
        public static Color WindowBg { get { return Pick(0xFFFFFF, 0x1F1F1F); } }
        public static Color SidebarBg { get { return Pick(0xF5F5F7, 0x262628); } }
        public static Color Card { get { return Pick(0xF5F5F7, 0x2C2C2E); } }
        public static Color CardHover { get { return Pick(0xE8E8ED, 0x3A3A3C); } }
        // Text uses opacity on dark (white at 92 / 64 %), flattened here because GDI text has no alpha.
        public static Color Label { get { return Pick(0x1D1D1F, 0xEDEDED); } }
        public static Color Label2 { get { return Pick(0x6E6E73, 0xAEAEAE); } }
        // Blue means "you can act on this": links, the primary button, the selected menu item.
        public static Color Link { get { return Pick(0x0066CC, 0x2997FF); } }
        public static readonly Color PrimaryFill = Rgb(0x0071E3);
        public static readonly Color OnPrimary = Color.White;
        public static Color Destructive { get { return Pick(0xD70015, 0xFF453A); } }
        public static Color Fill { get { return dark ? Color.FromArgb(92, 120, 120, 128) : Color.FromArgb(51, 120, 120, 128); } }
        public static Color Tinted { get { return dark ? Color.FromArgb(41, 10, 132, 255) : Color.FromArgb(20, 0, 122, 255); } }
        public static Color Selection { get { return dark ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(15, 0, 0, 0); } }
        public static Color Separator { get { return dark ? Color.FromArgb(153, 84, 84, 88) : Color.FromArgb(74, 60, 60, 67); } }
        public static Color Field { get { return Pick(0xFFFFFF, 0x2C2C2E); } }
        public static Color FieldBorder { get { return Pick(0xD2D2D7, 0x48484A); } }

        // Menus float, so they're near-opaque material with a hairline edge and a soft shadow.
        public static Color MenuBg { get { return dark ? Color.FromArgb(250, 42, 42, 45) : Color.FromArgb(250, 246, 246, 248); } }
        public static Color MenuEdge { get { return dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0); } }

        // ---- shape (§6) ----
        public const int RadiusCard = 18, RadiusField = 12, RadiusSmall = 8, RadiusItem = 6, RadiusMenu = 10;

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        // ---- type (§3) ----
        // Sizes are px (Apple's numbers). Segoe UI Variable is the Windows face closest to SF Pro:
        // Text under 20 px, Display from 20 up (§3.1). Windows 10 falls back to Segoe UI.
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font Font(float px, int weight = 400)
        {
            string key = px + "/" + weight;
            Font f;
            if (fonts.TryGetValue(key, out f)) return f;
            string optical = px >= 20 ? "Display" : "Text";
            string family = "Segoe UI Variable " + optical + (weight == 600 ? " Semibold" : "");
            FontStyle style = weight >= 700 ? FontStyle.Bold : FontStyle.Regular;
            f = new Font(family, px * 0.75f, style);
            if (f.Name != family)
            {
                f.Dispose();
                f = new Font(weight == 600 ? "Segoe UI Semibold" : "Segoe UI", px * 0.75f, style);
            }
            fonts[key] = f;
            return f;
        }

        // The app text styles used across the app's own windows (§3.5, web-app sizes §2.9).
        public static Font LargeTitle { get { return Font(34, 700); } }
        public static Font Title3 { get { return Font(20, 600); } }
        public static Font Headline { get { return Font(17, 600); } }
        public static Font SectionTitle { get { return Font(17, 700); } }
        public static Font Body { get { return Font(15); } }
        public static Font BodyEmphasized { get { return Font(15, 600); } }
        public static Font Footnote { get { return Font(13); } }
        public static Font Caption { get { return Font(12); } }
        public static Font CaptionEmphasized { get { return Font(12, 600); } }
        public static Font Button { get { return Font(13, 600); } }
        public static Font Menu { get { return Font(14); } }

        static Font mono;
        public static Font Mono
        {
            get
            {
                if (mono == null)
                {
                    mono = new Font("Cascadia Mono", 9.75f);
                    if (mono.Name != "Cascadia Mono") { mono.Dispose(); mono = new Font("Consolas", 9.75f); }
                }
                return mono;
            }
        }

        static Font icons;

        // Line icons in the SF Symbols spirit: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on
        // Windows 10 (same code points).
        public static Font Icons
        {
            get
            {
                if (icons == null)
                {
                    icons = new Font("Segoe Fluent Icons", 11f);
                    if (icons.Name != "Segoe Fluent Icons") { icons.Dispose(); icons = new Font("Segoe MDL2 Assets", 11f); }
                }
                return icons;
            }
        }

        // Windows title bar and scrollbars stay native, following the same light/dark choice.
        public static void ApplyToWindow(IntPtr hwnd)
        {
            int d = dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);
        }

        public static void ApplyToScrollbars(IntPtr hwnd)
        {
            Native.UseControlTheme(hwnd, dark);
        }
    }
}
