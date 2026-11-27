using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using DeskWidgets;

// A small black macOS-style countdown. The water in the card drains as the timer runs down,
// with two out-of-step waves and a lit surface so the pool reads as liquid rather than a bar.
[DeskWidget(Name = "Water Timer", Description = "Black countdown that drains as rippling water")]
public sealed class WaterTimer : WidgetWindow
{
    // The face is drawn at fixed coordinates, then scaled by the display's DPI.
    const int ArtWidth = 176;
    const int ArtHeight = 176;
    const int Radius = 34;

    // Highest the surface can climb, so the pool never covers the time.
    const float MaxRise = 84f;
    const float MinRise = 5f;

    static readonly int[] Minutes = { 25, 10, 5, 15, 45 };
    static readonly string[] Lengths = { "25 minutes", "10 minutes", "5 minutes", "15 minutes", "45 minutes" };

    readonly Countdown countdown = new Countdown();
    readonly Section settings;
    int choice;

    public WaterTimer(Section settings) : base(settings)
    {
        this.settings = settings;
        choice = IndexOf(settings.Get("minutes", "25"));
        countdown.Reset(Total);
    }

    int Total { get { return Minutes[choice] * 60; } }

    static int IndexOf(string text)
    {
        int wanted;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out wanted)) return 0;
        for (int i = 0; i < Minutes.Length; i++) if (Minutes[i] == wanted) return i;
        return 0;
    }

    protected override Size Measure() { return new Size(Px(ArtWidth), Px(ArtHeight)); }

    // Fixed layout: resizing would move the waterline's limits.
    protected override bool CanResize { get { return false; } }

    protected override int NextFrameDelay()
    {
        if (IsCovered()) return 1500;
        return countdown.Running ? 60 : 800;
    }

    protected override void BuildMenu(PopupMenu menu)
    {
        menu.Add(countdown.Running ? "Pause" : "Start", delegate { Toggle(); });
        menu.Add("Reset", delegate { countdown.Reset(Total); Render(); });
        menu.AddSeparator();
        PopupMenu lengths = menu.AddSubmenu("Duration");
        for (int i = 0; i < Minutes.Length; i++)
        {
            int index = i;
            lengths.AddChoice(Lengths[i], i == choice, delegate { SetLength(index); });
        }
    }

    void Toggle()
    {
        countdown.Toggle();
        Render();
    }

    void SetLength(int index)
    {
        choice = index;
        settings.Set("minutes", Minutes[index].ToString(CultureInfo.InvariantCulture));
        countdown.Reset(Total);
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

        double remaining = countdown.Remaining;
        float level = countdown.Total <= 0 ? 0f : (float)(remaining / countdown.Total);
        int whole = (int)Math.Ceiling(remaining);
        string time = string.Format(CultureInfo.InvariantCulture,
            "{0:00}:{1:00}", whole / 60, whole % 60);

        DrawFace(g, level, (float)countdown.Elapsed, time);
    }

    // A stopwatch keeps the wave and the timer on the same clock, so repaint timing never
    // steals time and the swell freezes exactly while paused.
    sealed class Countdown
    {
        readonly Stopwatch watch = new Stopwatch();
        double total;

        public double Total { get { return total; } }
        public double Remaining { get { return Math.Max(0, total - watch.Elapsed.TotalSeconds); } }
        public double Elapsed { get { return watch.Elapsed.TotalSeconds; } }
        public bool Running { get { return watch.IsRunning && Remaining > 0; } }

        public void Reset(int seconds)
        {
            watch.Reset();
            total = seconds;
        }

        public void Toggle()
        {
            if (Remaining <= 0) Reset((int)total);
            if (watch.IsRunning) watch.Stop();
            else watch.Start();
        }
    }

    static void DrawFace(Graphics g, float level, float phase, string time)
    {
        Rectangle card = new Rectangle(1, 1, ArtWidth - 2, ArtHeight - 2);
        using (GraphicsPath shape = Rounded(card, Radius))
        {
            using (LinearGradientBrush body = new LinearGradientBrush(
                new Rectangle(0, 0, ArtWidth, ArtHeight), Hex(0x1d1f23), Hex(0x060607), 90f))
            using (Pen edge = new Pen(Color.FromArgb(34, 255, 255, 255), 1f))
            {
                g.FillPath(body, shape);
                g.DrawPath(edge, shape);
            }

            Region previous = g.Clip;
            g.SetClip(shape, CombineMode.Replace);

            // Glass: a faint sheen across the top. Clamp the wrap, or the brush tiles a
            // bright row along its own bottom edge.
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                new Rectangle(0, 0, ArtWidth, 70),
                Color.FromArgb(30, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
            {
                sheen.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(sheen, 0, 0, ArtWidth, 70);
            }

            float surface = card.Bottom - (MinRise + level * (MaxRise - MinRise));
            DrawWater(g, surface, phase);

            // Light from the pool spilling up into the black, which is what sells the depth.
            using (GraphicsPath haze = new GraphicsPath())
            {
                haze.AddEllipse(-40, surface - 34, ArtWidth + 80, 68);
                using (PathGradientBrush spill = new PathGradientBrush(haze))
                {
                    spill.CenterColor = Color.FromArgb(20, 40, 150, 235);
                    spill.SurroundColors = new[] { Color.FromArgb(0, 40, 150, 235) };
                    g.FillPath(spill, haze);
                }
            }

            // Depth: the card darkens towards its bottom edge, over the water too.
            using (LinearGradientBrush depth = new LinearGradientBrush(
                new Rectangle(0, ArtHeight - 52, ArtWidth, 52),
                Color.FromArgb(0, 0, 0, 0), Color.FromArgb(44, 0, 0, 0), 90f))
            {
                depth.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(depth, 0, ArtHeight - 52, ArtWidth, 52);
            }

            DrawTime(g, time);
            g.Clip = previous;
        }

        // The window's own rim, drawn after the clip so it stays crisp.
        using (GraphicsPath shape = Rounded(card, Radius))
        using (Pen rim = new Pen(Color.FromArgb(16, 255, 255, 255), 1f))
            g.DrawPath(rim, shape);
    }

    static void DrawTime(Graphics g, string time)
    {
        using (Font font = new Font("Bahnschrift", 34, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(236, 203, 207, 213)))
        using (StringFormat format = new StringFormat())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            g.DrawString(time, font, brush, new Rectangle(8, 10, ArtWidth - 16, 46), format);
        }
    }

    static void DrawWater(Graphics g, float surface, float phase)
    {
        // Grey swell behind the blue: the body of the wave seen through the crest.
        PointF[] back = Edge(surface - 9f, 4.2f, 0.052f, phase * 0.62f + 2.6f, 0.042f);
        using (GraphicsPath path = Body(back))
        using (SolidBrush grey = new SolidBrush(Hex(0x40464d)))
        {
            g.FillPath(grey, path);
        }
        using (Pen rim = new Pen(Color.FromArgb(70, 0x74, 0x7c, 0x85), 1f))
        {
            rim.StartCap = LineCap.Round;
            rim.EndCap = LineCap.Round;
            g.DrawLines(rim, back);
        }

        // Blue pool in front, driven at its own speed so the two never lock together.
        PointF[] front = Edge(surface, 4.3f, 0.047f, phase * 1.9f, 0.042f);
        using (GraphicsPath path = Body(front))
        using (LinearGradientBrush blue = new LinearGradientBrush(
            new Rectangle(0, (int)surface - 10, ArtWidth, ArtHeight + 20),
            Hex(0x53caff), Hex(0x0f74e0), 90f))
        {
            blue.WrapMode = WrapMode.TileFlipXY;
            g.FillPath(blue, path);
        }

        // Lit surface, and a soft shimmer sitting just under it.
        using (Pen lit = new Pen(Color.FromArgb(88, 214, 241, 255), 1f))
        {
            lit.StartCap = LineCap.Round;
            lit.EndCap = LineCap.Round;
            g.DrawLines(lit, front);
        }
        using (GraphicsPath glow = new GraphicsPath())
        {
            glow.AddEllipse(ArtWidth * 0.52f, surface + 7f, 64, 24);
            using (PathGradientBrush shine = new PathGradientBrush(glow))
            {
                shine.CenterColor = Color.FromArgb(40, 200, 240, 255);
                shine.SurroundColors = new[] { Color.FromArgb(0, 200, 240, 255) };
                g.FillPath(shine, glow);
            }
        }
    }

    static float WaveAt(float x, float amp, float freq, float phase)
    {
        return (float)(Math.Sin(x * freq + phase) * amp
            + Math.Sin(x * freq * 2.41f + phase * 1.63f) * (amp * 0.32f));
    }

    static PointF[] Edge(float surface, float amp, float freq, float phase, float tilt)
    {
        int count = (ArtWidth + 12) / 2 + 1;
        PointF[] points = new PointF[count];
        for (int i = 0; i < count; i++)
        {
            float x = -6f + i * 2f;
            points[i] = new PointF(x, surface + tilt * (x - ArtWidth / 2f) + WaveAt(x, amp, freq, phase));
        }
        return points;
    }

    // The surface line closed off along the bottom of the card.
    static GraphicsPath Body(PointF[] edge)
    {
        PointF[] body = new PointF[edge.Length + 2];
        Array.Copy(edge, body, edge.Length);
        body[edge.Length] = new PointF(ArtWidth + 6, ArtHeight + 6);
        body[edge.Length + 1] = new PointF(-6, ArtHeight + 6);
        GraphicsPath path = new GraphicsPath();
        path.AddPolygon(body);
        return path;
    }

    static Color Hex(int rgb)
    {
        return Color.FromArgb(255, rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255);
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
