using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using DeskWidgets;

// Focus timer in two appearances: a minimal tomato clock or a pixel moth that folds its wings
// while running. The style is a menu choice and is remembered between sessions.
[DeskWidget(Name = "Pomodoro", Description = "Focus timer with tomato and pixel moth styles")]
public sealed class Pomodoro : WidgetWindow
{
    // The face is drawn at fixed coordinates, then scaled by the display's DPI.
    const int ArtWidth = 264;
    const int ArtHeight = 282;

    static readonly int[] Durations = { 25 * 60, 5 * 60, 15 * 60 };
    static readonly string[] Modes = { "Focus", "Short", "Long" };
    static readonly Rectangle Play = new Rectangle(105, 156, 54, 43);
    static readonly Rectangle Reset = new Rectangle(184, 162, 34, 32);
    static readonly Rectangle[] Tabs =
    {
        new Rectangle(16, 230, 72, 34),
        new Rectangle(96, 230, 72, 34),
        new Rectangle(176, 230, 72, 34)
    };

    readonly Countdown countdown = new Countdown();
    readonly Section settings;
    bool pixelFace;

    public Pomodoro(Section settings) : base(settings)
    {
        this.settings = settings;
        pixelFace = settings.Get("style", "tomato") == "pixel";
    }

    protected override Size Measure() { return new Size(Px(ArtWidth), Px(ArtHeight)); }

    // Fixed pixel art: resizing would break every coordinate.
    protected override bool CanResize { get { return false; } }

    protected override int NextFrameDelay()
    {
        // No point repainting while the widget is behind a window.
        if (IsCovered()) return 1000;
        return countdown.Running ? (pixelFace ? 125 : 200) : (pixelFace ? 250 : 500);
    }

    // Mouse points arrive in device pixels; the art is laid out in 96 dpi units.
    Point Local(Point p)
    {
        return new Point((int)Math.Round(p.X / Dpi), (int)Math.Round(p.Y / Dpi));
    }

    protected override bool IsButton(Point p)
    {
        Point q = Local(p);
        if (Play.Contains(q) || Reset.Contains(q)) return true;
        for (int i = 0; i < Tabs.Length; i++) if (Tabs[i].Contains(q)) return true;
        return false;
    }

    protected override void OnWidgetClick(Point p)
    {
        Point q = Local(p);
        if (Play.Contains(q)) { Toggle(); return; }
        if (Reset.Contains(q)) { countdown.Reset(countdown.Mode); Render(); return; }
        for (int i = 0; i < Tabs.Length; i++)
        {
            if (!Tabs[i].Contains(q)) continue;
            if (i != countdown.Mode) { countdown.Reset(i); Render(); }
            return;
        }
    }

    void Toggle()
    {
        countdown.Toggle();
        Render();
    }

    protected override void BuildMenu(PopupMenu menu)
    {
        menu.Add(countdown.Running ? "Pause" : "Start", delegate { Toggle(); });
        menu.Add("Reset timer", delegate { countdown.Reset(countdown.Mode); Render(); });
        PopupMenu modes = menu.AddSubmenu("Timer mode");
        for (int i = 0; i < Modes.Length; i++)
        {
            int mode = i;
            modes.AddChoice(Modes[i], countdown.Mode == mode,
                delegate { countdown.Reset(mode); Render(); });
        }
        menu.AddSeparator();
        PopupMenu styles = menu.AddSubmenu("Style");
        styles.AddChoice("Tomato", !pixelFace, delegate { SetStyle(false); });
        styles.AddChoice("Pixel moth", pixelFace, delegate { SetStyle(true); });
    }

    void SetStyle(bool pixel)
    {
        pixelFace = pixel;
        settings.Set("style", pixel ? "pixel" : "tomato");
        Render();
    }

    protected override void Draw(Graphics g)
    {
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.ScaleTransform(Dpi, Dpi);

        DrawFace(g, pixelFace, countdown.Mode, countdown.Remaining, countdown.Running,
            countdown.Finished, (int)(countdown.Elapsed * 8) % 8);
    }

    // The countdown runs off a stopwatch, so repaint timing never steals time from the timer.
    sealed class Countdown
    {
        readonly Stopwatch watch = new Stopwatch();
        double available = Durations[0];

        public int Mode { get; private set; }
        public double Elapsed { get { return Durations[Mode] - Remaining; } }
        public double Remaining { get { return Math.Max(0, available - watch.Elapsed.TotalSeconds); } }
        public bool Running { get { return watch.IsRunning && Remaining > 0; } }
        public bool Finished { get { return Remaining <= 0; } }

        public void Reset(int mode)
        {
            watch.Reset();
            Mode = mode;
            available = Durations[mode];
        }

        public void Toggle()
        {
            if (Finished) Reset(Mode);
            if (watch.IsRunning) watch.Stop();
            else watch.Start();
        }
    }

    static void DrawFace(Graphics g, bool pixel, int mode, double seconds, bool running,
        bool finished, int frame)
    {
        Color ink = Hex(pixel ? 0xffeed7 : 0xf7f8fa);
        Color muted = Hex(pixel ? 0xd0bad5 : 0xa0a8b4);
        Color accent = Hex(pixel ? (mode == 0 ? 0xffb395 : 0x9ed8bc) : 0xf2f4f7);
        Color track = Hex(pixel ? 0x493950 : 0x30343a);

        using (GraphicsPath card = Rounded(new Rectangle(1, 1, 262, 280), pixel ? 15 : 26))
        using (LinearGradientBrush fill = new LinearGradientBrush(
            new Rectangle(0, 0, ArtWidth, ArtHeight),
            Hex(pixel ? 0x291e32 : 0x101216), Hex(pixel ? 0x191824 : 0x1a1f25), 90f))
        using (Pen border = new Pen(Hex(pixel ? 0x46344c : 0x383c43)))
        {
            g.FillPath(fill, card);
            g.DrawPath(border, card);
        }

        Rectangle ring = new Rectangle(73, 18, 118, 118);
        using (Pen pen = new Pen(track, pixel ? 6f : 5f)) g.DrawEllipse(pen, ring);

        float sweep = (float)(360 * (1 - seconds / Durations[mode]));
        if (sweep > 0)
        {
            using (Pen pen = new Pen(accent, pixel ? 6f : 5f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, ring, -90, sweep);
            }
        }
        double radians = (sweep - 90) * Math.PI / 180;
        using (SolidBrush dot = new SolidBrush(accent))
            g.FillEllipse(dot, 129 + (float)Math.Cos(radians) * 59,
                74 + (float)Math.Sin(radians) * 59, 6, 6);

        if (pixel) DrawMoth(g, frame, running);
        else DrawTomato(g);

        int whole = (int)Math.Ceiling(seconds);
        string time = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:00}:{1:00}", whole / 60, whole % 60);
        if (pixel)
        {
            g.SmoothingMode = SmoothingMode.None;
            PixelTime(g, time, ink);
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
        else
        {
            DrawLabel(g, time, new Rectangle(77, 73, 110, 47), ink, 34, FontStyle.Regular, false);
        }

        PaintRound(g, Play, pixel ? 7 : 14, accent);
        PaintRound(g, Reset, 8, Hex(pixel ? 0x33283f : 0x242a32));
        DrawControls(g, running, Hex(pixel ? 0x2e1923 : 0x15191f), muted);

        string status = finished ? (mode == 0 ? "Focus complete" : "Break complete") :
            running ? (mode == 0 ? "In focus" : "On break") :
            seconds < Durations[mode] ? "Paused" : null;
        if (status != null)
            DrawLabel(g, status, new Rectangle(60, 200, 144, 22), muted, 11, FontStyle.Regular, pixel);

        for (int i = 0; i < Tabs.Length; i++)
        {
            if (i == mode) PaintRound(g, Tabs[i], pixel ? 6 : 9, accent);
            DrawLabel(g, Modes[i], Tabs[i], i == mode ? Hex(0x1c1920) : muted, 12, FontStyle.Bold, false);
        }
    }

    static void DrawControls(Graphics g, bool running, Color dark, Color muted)
    {
        using (SolidBrush brush = new SolidBrush(dark))
        {
            if (running)
            {
                g.FillRectangle(brush, 126, 170, 4, 15);
                g.FillRectangle(brush, 134, 170, 4, 15);
            }
            else
            {
                g.FillPolygon(brush, new[]
                {
                    new Point(127, 169), new Point(127, 186), new Point(140, 178)
                });
            }
        }
        using (Pen pen = new Pen(muted, 1.6f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawArc(pen, 195, 172, 12, 12, 35, 296);
            g.DrawLines(pen, new[]
            {
                new Point(207, 171), new Point(207, 176), new Point(202, 176)
            });
        }
    }

    static readonly string[] OpenWings =
    {
        ".....o.o.....",
        "......o......",
        "##...ooo...##",
        "####.ooo.####",
        ".#+##o#o##+#.",
        "..###o#o###..",
        "...##.o.##...",
        "....#...#...."
    };

    static readonly string[] FoldedWings =
    {
        ".....o.o.....",
        "......o......",
        "..#..ooo..#..",
        "..##.ooo.##..",
        "...#+o#o+#...",
        "...##o#o##...",
        "....#.o.#....",
        "............."
    };

    static void DrawMoth(Graphics g, int frame, bool running)
    {
        bool lifted = running && frame >= 4;
        string[] sprite = lifted ? FoldedWings : OpenWings;
        int y = lifted ? 43 : 46;
        SmoothingMode previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        using (SolidBrush wing = new SolidBrush(Hex(0xcca7f2)))
        using (SolidBrush spot = new SolidBrush(Hex(0xa06cb9)))
        using (SolidBrush body = new SolidBrush(Hex(0xffd69d)))
        {
            for (int row = 0; row < sprite.Length; row++)
            {
                for (int col = 0; col < sprite[row].Length; col++)
                {
                    char pixel = sprite[row][col];
                    if (pixel == '.') continue;
                    Brush brush = pixel == '+' ? spot : pixel == 'o' ? body : wing;
                    g.FillRectangle(brush, 112 + col * 3, y + row * 3, 3, 3);
                }
            }
        }
        g.SmoothingMode = previous;
    }

    static void DrawTomato(Graphics g)
    {
        Rectangle body = new Rectangle(119, 48, 26, 23);
        using (LinearGradientBrush tomato = new LinearGradientBrush(body, Hex(0xff7541), Hex(0xb62025), 70f))
            g.FillEllipse(tomato, body);
        using (SolidBrush highlight = new SolidBrush(Color.FromArgb(90, Hex(0xffd3a1))))
            g.FillEllipse(highlight, 123, 52, 8, 4);
        using (SolidBrush leaf = new SolidBrush(Hex(0x639d4d)))
            g.FillPolygon(leaf, new[]
            {
                new Point(125, 47), new Point(131, 48), new Point(134, 45), new Point(135, 48),
                new Point(141, 47), new Point(136, 51), new Point(132, 50), new Point(127, 52)
            });
    }

    // Each row is a three-bit mask for a compact 3 x 5 pixel digit.
    static readonly int[][] Glyphs =
    {
        new[] {7, 5, 5, 5, 7}, new[] {2, 6, 2, 2, 7},
        new[] {7, 1, 7, 4, 7}, new[] {7, 1, 7, 1, 7},
        new[] {5, 5, 7, 1, 1}, new[] {7, 4, 7, 1, 7},
        new[] {7, 4, 7, 5, 7}, new[] {7, 1, 2, 2, 2},
        new[] {7, 5, 7, 5, 7}, new[] {7, 5, 7, 1, 7}
    };

    static void PixelTime(Graphics g, string time, Color ink)
    {
        int x = 90;
        using (SolidBrush brush = new SolidBrush(ink))
        {
            for (int c = 0; c < time.Length; c++)
            {
                char ch = time[c];
                if (ch == ':')
                {
                    g.FillRectangle(brush, x, 89, 4, 4);
                    g.FillRectangle(brush, x, 99, 4, 4);
                    x += 9;
                    continue;
                }
                int[] glyph = Glyphs[ch - '0'];
                for (int row = 0; row < glyph.Length; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        if ((glyph[row] & (4 >> col)) != 0)
                            g.FillRectangle(brush, x + col * 5, 82 + row * 5, 5, 5);
                    }
                }
                x += 20;
            }
        }
    }

    static void DrawLabel(Graphics g, string value, Rectangle box, Color color,
        float size, FontStyle style, bool pixelLabel)
    {
        using (Font font = new Font(pixelLabel ? "Consolas" : "Bahnschrift", size, style, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(pixelLabel ? value.ToUpperInvariant() : value, font, brush, box, format);
        }
    }

    static Color Hex(int rgb)
    {
        return Color.FromArgb(255, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255);
    }

    static void PaintRound(Graphics g, Rectangle rect, int radius, Color color)
    {
        using (GraphicsPath path = Rounded(rect, radius))
        using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
    }

    static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        int d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
