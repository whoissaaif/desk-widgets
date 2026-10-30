using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DeskWidgets.Widgets
{
    // Base for the Apple-style card widgets: small (170 x 170 pt) or medium (364 x 170 pt),
    // 22 pt corner radius, 16 pt margins, light or dark appearance, three sizes.
    // Subclasses lay out in points and call P() to convert; everything is drawn on demand.
    abstract class CardWidget : WidgetWindow
    {
        protected sealed class Palette
        {
            public Color Bg, Fg, Fg2, Fill, Sep, Red, Orange, Yellow, Green, Blue, Purple;
        }

        protected static readonly Palette DarkPalette = new Palette
        {
            Bg = Rgb(0x1C1C1E), Fg = Color.White, Fg2 = Rgb(0x98989F), Fill = Rgb(0x2C2C2E), Sep = Rgb(0x38383A),
            Red = Rgb(0xFF453A), Orange = Rgb(0xFF9F0A), Yellow = Rgb(0xFFD60A), Green = Rgb(0x30D158),
            Blue = Rgb(0x0A84FF), Purple = Rgb(0xBF5AF2)
        };

        protected static readonly Palette LightPalette = new Palette
        {
            Bg = Color.White, Fg = Color.Black, Fg2 = Rgb(0x6E6E73), Fill = Rgb(0xF2F2F7), Sep = Rgb(0xE5E5EA),
            Red = Rgb(0xFF3B30), Orange = Rgb(0xFF9500), Yellow = Rgb(0xF5B800), Green = Rgb(0x34C759),
            Blue = Rgb(0x007AFF), Purple = Rgb(0xAF52DE)
        };

        protected static Color Rgb(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        static readonly int[] ScaleOptions = { 85, 100, 125 };
        static readonly string[] ScaleNames = { "Compact", "Standard", "Large" };

        readonly bool medium;
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        float fontScale;

        protected CardWidget(Section settings, bool medium) : base(settings)
        {
            this.medium = medium;
        }

        protected bool IsDark { get { return Settings.Get("face", "dark") != "light"; } }
        protected Palette Pal { get { return IsDark ? DarkPalette : LightPalette; } }
        protected float Zoom { get { return Dpi * Settings.GetInt("scale", 100) / 100f; } }
        protected float P(float pt) { return pt * Zoom; }
        protected float W { get { return P(medium ? 364 : 170); } }
        protected float H { get { return P(170); } }

        protected override Size Measure()
        {
            return new Size((int)Math.Round(W), (int)Math.Round(H));
        }

        protected abstract void DrawContent(Graphics g, Palette pal);

        protected override void Draw(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Palette pal = Pal;
            using (var card = CardPath())
            using (var b = new SolidBrush(CardBackground(pal)))
                g.FillPath(b, card);
            DrawContent(g, pal);
            g.ResetClip();
        }

        // Widgets that cover the card with an image use a dark backing so no light edge shows at the corners.
        protected virtual Color CardBackground(Palette pal) { return pal.Bg; }

        protected GraphicsPath CardPath()
        {
            return RoundRect(new RectangleF(0, 0, W, H), P(22));
        }

        // Called after a setting changes: drop caches that depend on it and redraw.
        protected virtual void Changed()
        {
            Render();
        }

        // ---- drawing helpers ----
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, path);
        }

        protected static void FillCircle(Graphics g, Color c, float cx, float cy, float radius)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        // Weight: "regular", "semibold", "light", "bold". Sizes in points (Apple's type scale: 11, 13, 15...).
        protected Font F(float pt, string weight)
        {
            if (fontScale != Zoom)
            {
                foreach (var f in fonts.Values) f.Dispose();
                fonts.Clear();
                fontScale = Zoom;
            }
            string key = weight + pt;
            Font font;
            if (!fonts.TryGetValue(key, out font))
            {
                string family = weight == "semibold" ? "Segoe UI Semibold" : weight == "light" ? "Segoe UI Light" : "Segoe UI";
                FontStyle style = weight == "bold" ? FontStyle.Bold : FontStyle.Regular;
                font = new Font(family, P(pt), style, GraphicsUnit.Pixel);
                fonts[key] = font;
            }
            return font;
        }

        static readonly StringFormat Typo = MakeFormat(StringAlignment.Near);
        static readonly StringFormat TypoRight = MakeFormat(StringAlignment.Far);
        static readonly StringFormat TypoCenter = MakeFormat(StringAlignment.Center);

        static StringFormat MakeFormat(StringAlignment align)
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.Alignment = align;
            f.Trimming = StringTrimming.EllipsisCharacter;
            f.FormatFlags |= StringFormatFlags.NoWrap;
            return f;
        }

        // Single line of text; x is the left edge (or right edge / centre for other alignments),
        // y the top of the line box. Long text is cut with an ellipsis at maxWidth.
        protected void Label(Graphics g, string s, Font f, Color c, float x, float y, float maxWidth, StringAlignment align)
        {
            var fmt = align == StringAlignment.Far ? TypoRight : align == StringAlignment.Center ? TypoCenter : Typo;
            float left = align == StringAlignment.Far ? x - maxWidth : align == StringAlignment.Center ? x - maxWidth / 2 : x;
            using (var b = new SolidBrush(c))
                g.DrawString(s, f, b, new RectangleF(left, y, maxWidth, f.GetHeight() * 1.3f), fmt);
        }

        protected void Label(Graphics g, string s, Font f, Color c, float x, float y)
        {
            Label(g, s, f, c, x, y, W, StringAlignment.Near);
        }

        // Wrapped paragraph clipped to a box, with an ellipsis on the last visible line.
        protected void Paragraph(Graphics g, string s, Font f, Color c, RectangleF box)
        {
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var b = new SolidBrush(c))
            {
                fmt.Trimming = StringTrimming.EllipsisWord;
                fmt.FormatFlags |= StringFormatFlags.LineLimit;
                g.DrawString(s, f, b, box, fmt);
            }
        }

        protected float TextWidth(Graphics g, string s, Font f)
        {
            return g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        // ---- shared menu: appearance and size ----
        protected override void BuildMenu(PopupMenu menu)
        {
            BuildWidgetMenu(menu);
            menu.AddSeparator();
            var look = menu.AddSubmenu("Appearance");
            look.AddChoice("Dark", IsDark, delegate { Settings.Set("face", "dark"); Changed(); });
            look.AddChoice("Light", !IsDark, delegate { Settings.Set("face", "light"); Changed(); });
            var size = menu.AddSubmenu("Size");
            int cur = Settings.GetInt("scale", 100);
            for (int i = 0; i < ScaleOptions.Length; i++)
            {
                int pct = ScaleOptions[i];
                size.AddChoice(ScaleNames[i], cur == pct, delegate { Settings.Set("scale", pct); Changed(); });
            }
        }

        protected virtual void BuildWidgetMenu(PopupMenu menu) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var f in fonts.Values) f.Dispose();
                fonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
