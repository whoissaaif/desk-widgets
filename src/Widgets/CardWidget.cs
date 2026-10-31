using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;

namespace DeskWidgets.Widgets
{
    // Base for the Apple-style card widgets: small (170 x 170 pt), medium (364 x 170 pt) or large (364 x 382 pt),
    // 22 pt corner radius, 16 pt margins, dark / light / vibrant appearance, three sizes.
    // Subclasses lay out in points and call P() to convert; everything is drawn on demand.
    abstract class CardWidget : WidgetWindow
    {
        protected sealed class Palette
        {
            public Color Bg, Fg, Fg2, Fill, Sep, Red, Orange, Yellow, Green, Blue, Purple;
            public Color OnAccent; // glyphs and text drawn on top of an accent-coloured fill
        }

        protected static readonly Palette DarkPalette = new Palette
        {
            Bg = Rgb(0x1C1C1E), Fg = Color.White, Fg2 = Rgb(0x98989F), Fill = Rgb(0x2C2C2E), Sep = Rgb(0x38383A),
            Red = Rgb(0xFF453A), Orange = Rgb(0xFF9F0A), Yellow = Rgb(0xFFD60A), Green = Rgb(0x30D158),
            Blue = Rgb(0x0A84FF), Purple = Rgb(0xBF5AF2), OnAccent = Color.White
        };

        protected static readonly Palette LightPalette = new Palette
        {
            Bg = Color.White, Fg = Color.Black, Fg2 = Rgb(0x6E6E73), Fill = Rgb(0xF2F2F7), Sep = Rgb(0xE5E5EA),
            Red = Rgb(0xFF3B30), Orange = Rgb(0xFF9500), Yellow = Rgb(0xFFCC00), Green = Rgb(0x34C759),
            Blue = Rgb(0x007AFF), Purple = Rgb(0xAF52DE), OnAccent = Color.White
        };

        // macOS "vibrant" desktop widgets: frosted wallpaper behind, everything in white.
        protected static readonly Palette VibrantPalette = new Palette
        {
            Bg = Color.FromArgb(40, 255, 255, 255), Fg = Color.White, Fg2 = Color.FromArgb(175, 255, 255, 255),
            Fill = Color.FromArgb(40, 255, 255, 255), Sep = Color.FromArgb(55, 255, 255, 255),
            Red = Color.White, Orange = Color.White, Yellow = Color.White, Green = Color.White, Blue = Color.White, Purple = Color.White,
            OnAccent = Rgb(0x2A3B42)
        };

        protected static Color Rgb(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        static readonly int[] ScaleOptions = { 85, 100, 125 };
        static readonly string[] ScaleNames = { "Compact", "Standard", "Large" };

        protected enum CardSize { Small, Medium, Large }

        readonly CardSize size;
        readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        float fontScale;

        protected CardWidget(Section settings, bool medium) : this(settings, medium ? CardSize.Medium : CardSize.Small) { }

        protected CardWidget(Section settings, CardSize size) : base(settings)
        {
            this.size = size;
        }

        protected string Face { get { return Settings.Get("face", DefaultFace); } }
        protected virtual string DefaultFace { get { return "dark"; } }
        protected bool IsVibrant { get { return Face == "vibrant"; } }
        protected bool IsDark { get { return Face != "light"; } }
        protected Palette Pal { get { return IsVibrant ? VibrantPalette : Face == "light" ? LightPalette : DarkPalette; } }
        protected float Zoom { get { return Dpi * Settings.GetInt("scale", 100) / 100f; } }
        protected float P(float pt) { return pt * Zoom; }
        protected float W { get { return P(size == CardSize.Small ? 170 : 364); } }
        protected float H { get { return P(size == CardSize.Large ? 382 : 170); } }

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
            {
                if (IsVibrant) DrawFrosted(g, card);
                else using (var b = new SolidBrush(CardBackground(pal))) g.FillPath(b, card);
            }
            DrawContent(g, pal);
            g.ResetClip();
        }

        // Vibrant background: the blurred wallpaper behind the widget, a soft dark tint so white text
        // stays readable on light wallpapers, and a hairline edge like glass.
        Bitmap backdrop;
        Rectangle backdropFor;

        void DrawFrosted(Graphics g, GraphicsPath card)
        {
            Size size = Measure();
            // Library previews have no place on screen yet: they get a neutral stand-in.
            Rectangle where = IsHandleCreated ? new Rectangle(Location, size) : Rectangle.Empty;
            if (backdrop == null || where != backdropFor || backdrop.Size != size)
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = DesktopBackdrop.Blurred(size, where);
                backdropFor = where;
            }
            var state = g.Save();
            g.SetClip(card);
            g.DrawImageUnscaled(backdrop, 0, 0);
            using (var tint = new SolidBrush(Color.FromArgb(70, 20, 24, 28))) g.FillPath(tint, card);
            using (var frost = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillPath(frost, card);
            g.Restore(state);
            using (var edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) g.DrawPath(edge, card);
        }

        internal override void BackdropChanged()
        {
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (IsVibrant) Render();
        }

        // Widgets that cover the card with an image use a dark backing so no light edge shows at the corners.
        protected virtual Color CardBackground(Palette pal) { return pal.Bg; }

        protected GraphicsPath CardPath()
        {
            return RoundRect(new RectangleF(0, 0, W, H), P(22));
        }

        // Runs slow work (network, disk, decoding) on a worker thread and hands the result, or the
        // exception, back on the UI thread. Silently dropped if the widget closed meanwhile.
        protected void Background<T>(Func<T> work, Action<T, Exception> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                T result = default(T);
                Exception error = null;
                try { result = work(); }
                catch (Exception ex) { error = ex; }
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)delegate { if (!IsDisposed) done(result, error); }); }
                catch (InvalidOperationException) { } // handle destroyed between the check and the call
            });
        }

        // Cache file for this widget instance (null in library previews, which never write).
        protected string CacheFile(string name)
        {
            return App.DataDir == null ? null : System.IO.Path.Combine(App.DataDir, "cache", name + "-" + Settings.Name + ".txt");
        }

        // Appearance menu entries: { setting value, label }. Weather swaps Dark/Light for its sky.
        protected virtual string[][] AppearanceChoices
        {
            get { return new[] { new[] { "dark", "Dark" }, new[] { "light", "Light" }, new[] { "vibrant", "Vibrant" } }; }
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

        // Draws an image desaturated (vibrant mode tones down photos and avatars, like macOS).
        protected static void DrawGray(Graphics g, Image img, RectangleF dest)
        {
            var m = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { 0.30f, 0.30f, 0.30f, 0, 0 }, new[] { 0.59f, 0.59f, 0.59f, 0, 0 }, new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 }
            });
            using (var attrs = new System.Drawing.Imaging.ImageAttributes())
            {
                attrs.SetColorMatrix(m);
                g.DrawImage(img, new[] { dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) },
                    new RectangleF(0, 0, img.Width, img.Height), GraphicsUnit.Pixel, attrs);
            }
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
            string face = Face;
            foreach (var choice in AppearanceChoices)
            {
                string id = choice[0];
                look.AddChoice(choice[1], face == id, delegate { Settings.Set("face", id); Changed(); });
            }
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
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            }
            base.Dispose(disposing);
        }
    }
}
