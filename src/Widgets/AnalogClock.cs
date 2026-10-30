using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DeskWidgets.Widgets
{
    // macOS-style world clock. The dial (background, ticks, numbers, label) is rendered once
    // into a cached bitmap; each frame only blits that and draws three hands.
    sealed class AnalogClock : WidgetWindow
    {
        struct City
        {
            public readonly string Label, Name, Zone;
            public City(string label, string name, string zone) { Label = label; Name = name; Zone = zone; }
        }

        static readonly City[] Cities =
        {
            new City("", "Local time", ""),
            new City("LON", "London", "GMT Standard Time"),
            new City("PAR", "Paris", "Romance Standard Time"),
            new City("BER", "Berlin", "W. Europe Standard Time"),
            new City("CAI", "Cairo", "Egypt Standard Time"),
            new City("MOS", "Moscow", "Russian Standard Time"),
            new City("DXB", "Dubai", "Arabian Standard Time"),
            new City("KHI", "Karachi", "Pakistan Standard Time"),
            new City("DEL", "New Delhi", "India Standard Time"),
            new City("SIN", "Singapore", "Singapore Standard Time"),
            new City("HKG", "Hong Kong", "China Standard Time"),
            new City("TYO", "Tokyo", "Tokyo Standard Time"),
            new City("SYD", "Sydney", "AUS Eastern Standard Time"),
            new City("AKL", "Auckland", "New Zealand Standard Time"),
            new City("SAO", "São Paulo", "E. South America Standard Time"),
            new City("NYC", "New York", "Eastern Standard Time"),
            new City("CHI", "Chicago", "Central Standard Time"),
            new City("LA", "Los Angeles", "Pacific Standard Time"),
        };

        sealed class Theme
        {
            public Color BgTop, BgBottom, Face, Numbers, Label, TickHour, TickMinute, Hands, HandShadow, Seconds;
        }

        static readonly Theme LightTheme = new Theme
        {
            BgTop = Rgb(0x2E2E30), BgBottom = Rgb(0x1C1C1E), Face = Color.White, Numbers = Rgb(0x111111),
            Label = Rgb(0x8E8E93), TickHour = Rgb(0x6E6E73), TickMinute = Rgb(0xBDBDC2), Hands = Rgb(0x111111),
            HandShadow = Color.FromArgb(45, 0, 0, 0), Seconds = Rgb(0xFF9500)
        };

        static readonly Theme DarkTheme = new Theme
        {
            BgTop = Rgb(0x3A3A3C), BgBottom = Rgb(0x262628), Face = Rgb(0x111113), Numbers = Rgb(0xF5F5F7),
            Label = Rgb(0x8E8E93), TickHour = Rgb(0x8E8E93), TickMinute = Rgb(0x48484A), Hands = Rgb(0xF5F5F7),
            HandShadow = Color.FromArgb(90, 0, 0, 0), Seconds = Rgb(0xFF9F0A)
        };

        static Color Rgb(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        static readonly int[] SizeOptions = { 140, 170, 220, 300 };
        static readonly string[] SizeNames = { "Small", "Medium", "Large", "Extra large" };

        TimeZoneInfo zone;
        Bitmap dial;          // cached static layer
        Pen stemPen, armPen, stemShadow, armShadow, secondsPen;
        SolidBrush hubBrush, secondsBrush, pinBrush;

        public AnalogClock(Section settings) : base(settings)
        {
            LoadZone();
        }

        int Diameter { get { return Px(Settings.GetInt("size", 170)); } }
        string SecondsMode { get { return Settings.Get("seconds", "tick"); } } // off | tick | smooth
        Theme CurrentTheme { get { return Settings.Get("face", "light") == "dark" ? DarkTheme : LightTheme; } }

        void LoadZone()
        {
            zone = null;
            string id = Settings.Get("tz", "");
            if (id.Length == 0) return;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        void Changed()
        {
            LoadZone();
            DisposeDial();
            Render();
        }

        protected override Size Measure()
        {
            int d = Diameter;
            return new Size(d, d);
        }

        protected override int NextFrameDelay()
        {
            string mode = SecondsMode;
            if (mode == "smooth") return 33; // ~30 fps sweep
            DateTime now = DateTime.UtcNow;
            if (mode == "off") return (59 - now.Second) * 1000 + (1000 - now.Millisecond) + 10;
            return 1000 - now.Millisecond + 10; // wake just after the next whole second
        }

        protected override void Draw(Graphics g)
        {
            int s = Diameter;
            if (dial == null || dial.Width != s) BuildDial(s);

            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(dial, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            DateTime utc = DateTime.UtcNow;
            DateTime t = zone == null ? utc.ToLocalTime() : TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            string mode = SecondsMode;
            double sec = t.Second + (mode == "smooth" ? t.Millisecond / 1000.0 : 0);
            double min = t.Minute + sec / 60.0;
            double hour = t.Hour % 12 + min / 60.0;

            float c = s / 2f, r = s * 0.445f;
            float shadowDy = s * 0.007f;
            Hand(g, c, c + shadowDy, hour * 30, r * 0.52f, r, stemShadow, armShadow);
            Hand(g, c, c + shadowDy, min * 6, r * 0.84f, r, stemShadow, armShadow);
            Hand(g, c, c, hour * 30, r * 0.52f, r, stemPen, armPen);
            Hand(g, c, c, min * 6, r * 0.84f, r, stemPen, armPen);
            Dot(g, hubBrush, c, s * 0.030f);

            if (mode != "off")
            {
                double a = sec * 6 * Math.PI / 180;
                float dx = (float)Math.Sin(a), dy = (float)-Math.Cos(a);
                g.DrawLine(secondsPen, c - dx * r * 0.18f, c - dy * r * 0.18f, c + dx * r * 0.92f, c + dy * r * 0.92f);
                Dot(g, secondsBrush, c, s * 0.021f);
                Dot(g, pinBrush, c, s * 0.008f);
            }
        }

        // Thin stem from the hub, then a thicker rounded arm (the macOS hand shape).
        static void Hand(Graphics g, float cx, float cy, double degrees, float length, float r, Pen stem, Pen arm)
        {
            double a = degrees * Math.PI / 180;
            float dx = (float)Math.Sin(a), dy = (float)-Math.Cos(a);
            float joint = r * 0.13f, armEnd = length - arm.Width / 2;
            g.DrawLine(stem, cx, cy, cx + dx * joint, cy + dy * joint);
            g.DrawLine(arm, cx + dx * joint, cy + dy * joint, cx + dx * armEnd, cy + dy * armEnd);
        }

        static void Dot(Graphics g, Brush b, float c, float radius)
        {
            g.FillEllipse(b, c - radius, c - radius, radius * 2, radius * 2);
        }

        void BuildDial(int s)
        {
            DisposeDial();
            Theme th = CurrentTheme;
            float c = s / 2f, r = s * 0.445f;

            dial = new Bitmap(s, s, PixelFormat.Format32bppPArgb);
            dial.SetResolution(96, 96);
            using (var g = Graphics.FromImage(dial))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                using (var bg = RoundedRect(new RectangleF(0, 0, s, s), s * 0.225f))
                using (var bgBrush = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s), th.BgTop, th.BgBottom))
                    g.FillPath(bgBrush, bg);

                using (var face = new SolidBrush(th.Face))
                    g.FillEllipse(face, c - r, c - r, r * 2, r * 2);

                using (var minorPen = new Pen(th.TickMinute, s * 0.0055f))
                using (var majorPen = new Pen(th.TickHour, s * 0.009f))
                {
                    for (int i = 0; i < 60; i++)
                    {
                        bool major = i % 5 == 0;
                        double a = i * 6 * Math.PI / 180;
                        float dx = (float)Math.Sin(a), dy = (float)-Math.Cos(a);
                        float inner = r * (major ? 0.895f : 0.925f), outer = r * 0.965f;
                        g.DrawLine(major ? majorPen : minorPen, c + dx * inner, c + dy * inner, c + dx * outer, c + dy * outer);
                    }
                }

                using (var numBrush = new SolidBrush(th.Numbers))
                {
                    for (int h = 1; h <= 12; h++)
                    {
                        double a = h * 30 * Math.PI / 180;
                        float x = c + (float)Math.Sin(a) * r * 0.735f, y = c - (float)Math.Cos(a) * r * 0.735f;
                        DrawCentered(g, h.ToString(), r * 0.27f, x, y, numBrush);
                    }
                }

                string label = Settings.Get("label", "");
                if (label.Length > 0)
                    using (var labelBrush = new SolidBrush(th.Label))
                        DrawCentered(g, label.ToUpperInvariant(), r * 0.135f, c, c - r * 0.40f, labelBrush);
            }

            stemPen = RoundPen(th.Hands, s * 0.016f);
            armPen = RoundPen(th.Hands, s * 0.034f);
            stemShadow = RoundPen(th.HandShadow, s * 0.016f);
            armShadow = RoundPen(th.HandShadow, s * 0.034f);
            secondsPen = RoundPen(th.Seconds, s * 0.0115f);
            hubBrush = new SolidBrush(th.Hands);
            secondsBrush = new SolidBrush(th.Seconds);
            pinBrush = new SolidBrush(th.Face);
        }

        static Pen RoundPen(Color color, float width)
        {
            return new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        static FontFamily numberFamily;
        static FontStyle numberStyle;

        static void DrawCentered(Graphics g, string text, float emSize, float x, float y, Brush brush)
        {
            if (numberFamily == null)
            {
                try { numberFamily = new FontFamily("Segoe UI Semibold"); numberStyle = FontStyle.Regular; }
                catch (ArgumentException) { numberFamily = new FontFamily("Segoe UI"); numberStyle = FontStyle.Bold; }
            }
            using (var path = new GraphicsPath())
            {
                path.AddString(text, numberFamily, (int)numberStyle, emSize, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF b = path.GetBounds();
                using (var m = new Matrix())
                {
                    m.Translate(x - b.X - b.Width / 2, y - b.Y - b.Height / 2);
                    path.Transform(m);
                }
                g.FillPath(brush, path);
            }
        }

        static GraphicsPath RoundedRect(RectangleF rc, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(rc.X, rc.Y, d, d, 180, 90);
            p.AddArc(rc.Right - d, rc.Y, d, d, 270, 90);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0, 90);
            p.AddArc(rc.X, rc.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void DisposeDial()
        {
            if (dial == null) return;
            dial.Dispose(); dial = null;
            stemPen.Dispose(); armPen.Dispose(); stemShadow.Dispose(); armShadow.Dispose(); secondsPen.Dispose();
            hubBrush.Dispose(); secondsBrush.Dispose(); pinBrush.Dispose();
        }

        protected override void BuildMenu(PopupMenu menu)
        {
            var city = menu.AddSubmenu("City");
            string curZone = Settings.Get("tz", ""), curLabel = Settings.Get("label", "");
            foreach (var each in Cities)
            {
                var ct = each;
                string text = ct.Label.Length > 0 ? ct.Name + "\t" + ct.Label : ct.Name;
                city.AddChoice(text, ct.Zone == curZone && ct.Label == curLabel, delegate
                {
                    Settings.Set("label", ct.Label);
                    Settings.Set("tz", ct.Zone);
                    Changed();
                });
                if (ct.Zone.Length == 0) city.AddSeparator();
            }

            var size = menu.AddSubmenu("Size");
            int curSize = Settings.GetInt("size", 170);
            for (int i = 0; i < SizeOptions.Length; i++)
            {
                int px = SizeOptions[i];
                size.AddChoice(SizeNames[i], curSize == px, delegate { Settings.Set("size", px); Changed(); });
            }

            var seconds = menu.AddSubmenu("Seconds hand");
            string curMode = SecondsMode;
            seconds.AddChoice("Ticking", curMode == "tick", delegate { Settings.Set("seconds", "tick"); Changed(); });
            seconds.AddChoice("Smooth sweep", curMode == "smooth", delegate { Settings.Set("seconds", "smooth"); Changed(); });
            seconds.AddChoice("Hidden", curMode == "off", delegate { Settings.Set("seconds", "off"); Changed(); });

            var face = menu.AddSubmenu("Face");
            bool dark = CurrentTheme == DarkTheme;
            face.AddChoice("Light", !dark, delegate { Settings.Set("face", "light"); Changed(); });
            face.AddChoice("Dark", dark, delegate { Settings.Set("face", "dark"); Changed(); });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeDial();
            base.Dispose(disposing);
        }
    }
}
