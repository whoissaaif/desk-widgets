using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using DeskWidgets;
using DeskWidgets.Widgets;

// Medium: album art, track, artist, progress and previous / play-pause / next for whatever
// Windows is playing (Spotify, browsers, Media Player...). Updates come as events from Windows;
// the widget only redraws once a second while something plays, to move the progress bar.
[DeskWidget(Name = "Now Playing", Description = "What's playing, with play, pause and skip")]
public sealed class NowPlayingWidget : CardWidget
{
    MediaSession media;            // opened in OnStart, never for library previews
    MediaSession.Snapshot now;
    Bitmap art;                    // cropped to the art square
    string status;
    bool refreshing, refreshAgain;

    public NowPlayingWidget(Section settings) : base(settings, true) { }

    protected override void OnStart()
    {
        Background(() => MediaSession.Open(), (m, error) =>
        {
            if (error != null) { status = "Media controls aren't available on this version of Windows"; Render(); return; }
            media = m;
            media.Changed += OnMediaChanged;
            RefreshMedia();
        });
    }

    // From a Windows worker thread: hop to the UI thread, then read in the background.
    void OnMediaChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)RefreshMedia); }
        catch (InvalidOperationException) { }
    }

    // Read the session in the background. Bursts of events (a track change fires several) are
    // folded into at most one extra read.
    void RefreshMedia()
    {
        if (media == null) return;
        if (refreshing) { refreshAgain = true; return; }
        refreshing = true;
        int artSize = (int)Math.Round(ArtSize);
        var m = media;
        Background(() =>
        {
            var snap = m.Read();
            Bitmap cover = snap != null && snap.Art != null ? Square(snap.Art, artSize) : null;
            return Tuple.Create(snap, cover);
        }, (result, error) =>
        {
            refreshing = false;
            if (error == null)
            {
                now = result.Item1;
                if (art != null) art.Dispose();
                art = result.Item2;
                status = null;
            }
            Render();
            if (refreshAgain) { refreshAgain = false; RefreshMedia(); }
        });
    }

    static Bitmap Square(byte[] bytes, int size)
    {
        try
        {
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float side = Math.Min(img.Width, img.Height);
                    g.DrawImage(img, new Rectangle(0, 0, size, size), new RectangleF((img.Width - side) / 2, (img.Height - side) / 2, side, side), GraphicsUnit.Pixel);
                }
                return bmp;
            }
        }
        catch (Exception) { return null; }
    }

    protected override int NextFrameDelay()
    {
        if (now == null || !now.Playing || now.Duration <= TimeSpan.Zero || !IsHandleCreated) return -1;
        return 1000 - DateTime.UtcNow.Millisecond + 20;
    }

    // ---------- layout ----------
    float ArtSize { get { return H - P(32); } }
    float RightX { get { return P(16) + ArtSize + P(14); } }
    PointF PlayCenter { get { return new PointF((RightX + W - P(16)) / 2, H - P(16) - P(18)); } }
    PointF PrevCenter { get { PointF c = PlayCenter; return new PointF(c.X - P(52), c.Y); } }
    PointF NextCenter { get { PointF c = PlayCenter; return new PointF(c.X + P(52), c.Y); } }

    static bool Within(Point p, PointF c, float r) { float dx = p.X - c.X, dy = p.Y - c.Y; return dx * dx + dy * dy <= r * r; }

    protected override bool IsButton(Point p)
    {
        return now != null && (Within(p, PlayCenter, P(20)) || Within(p, PrevCenter, P(16)) || Within(p, NextCenter, P(16)));
    }

    protected override void OnWidgetClick(Point p)
    {
        if (media == null || now == null) return;
        if (Within(p, PlayCenter, P(20))) media.PlayPause();
        else if (Within(p, PrevCenter, P(16))) media.Previous();
        else if (Within(p, NextCenter, P(16))) media.Next();
    }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        if (media != null && now != null)
        {
            menu.Add(now.Playing ? "Pause" : "Play", media.PlayPause);
            menu.Add("Next Track", media.Next);
            menu.Add("Previous Track", media.Previous);
        }
    }

    // ---------- drawing ----------
    protected override void DrawContent(Graphics g, Palette pal)
    {
        MediaSession.Snapshot s = IsHandleCreated ? now : Sample();
        float m = P(16), a = ArtSize, rx = RightX, rw = W - m - rx;

        // Album art (or a placeholder note).
        var artRect = new RectangleF(m, m, a, a);
        using (var clip = RoundRect(artRect, P(10)))
        {
            if (art != null)
            {
                var state = g.Save();
                g.SetClip(clip);
                if (IsVibrant) DrawGray(g, art, artRect); else g.DrawImage(art, artRect);
                g.Restore(state);
            }
            else if (!IsHandleCreated) PreviewArt(g, clip, artRect);
            else
            {
                using (var b = new SolidBrush(pal.Fill)) g.FillPath(b, clip);
                NoteGlyph(g, pal.Fg2, artRect);
            }
        }

        if (s == null)
        {
            Label(g, "Not playing", F(15, "semibold"), pal.Fg, rx, m + P(14), rw, StringAlignment.Near);
            Paragraph(g, status ?? "Play music or a video in any app and it shows up here.", F(11, "regular"), pal.Fg2, new RectangleF(rx, m + P(36), rw, P(60)));
            return;
        }

        Label(g, ("NOW PLAYING" + (s.App.Length > 0 ? " · " + s.App.ToUpperInvariant() : "")), F(11, "semibold"), pal.Fg2, rx, m - P(1), rw, StringAlignment.Near);
        Label(g, s.Title, F(15, "semibold"), pal.Fg, rx, m + P(14), rw, StringAlignment.Near);
        Label(g, s.Artist, F(13, "regular"), pal.Fg2, rx, m + P(34), rw, StringAlignment.Near);

        // Progress (only when the app reports a timeline; some browsers don't).
        if (s.Duration > TimeSpan.Zero)
        {
            TimeSpan pos = s.Position + (s.Playing ? DateTime.UtcNow - s.PositionAtUtc : TimeSpan.Zero);
            if (pos > s.Duration) pos = s.Duration;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            float by = m + P(64), bh = P(4);
            FillRound(g, pal.Fill, new RectangleF(rx, by, rw, bh), bh / 2);
            float frac = (float)(pos.TotalSeconds / s.Duration.TotalSeconds);
            if (frac > 0) FillRound(g, pal.Fg, new RectangleF(rx, by, Math.Max(bh, rw * frac), bh), bh / 2);
            Label(g, Clock(pos), F(11, "regular"), pal.Fg2, rx, by + P(8), rw / 2, StringAlignment.Near);
            Label(g, Clock(s.Duration), F(11, "regular"), pal.Fg2, rx + rw, by + P(8), rw / 2, StringAlignment.Far);
        }

        // Controls.
        PointF pc = PlayCenter, prev = PrevCenter, next = NextCenter;
        FillCircle(g, pal.Fg, pc.X, pc.Y, P(18));
        Color glyph = IsVibrant ? pal.OnAccent : pal.Bg;
        using (var b = new SolidBrush(glyph))
        {
            if (s.Playing)
            {
                float bw = P(4), bh = P(13);
                g.FillRectangle(b, pc.X - P(5.5f), pc.Y - bh / 2, bw, bh);
                g.FillRectangle(b, pc.X + P(1.5f), pc.Y - bh / 2, bw, bh);
            }
            else
            {
                float t = P(7);
                g.FillPolygon(b, new[] { new PointF(pc.X - t * 0.6f, pc.Y - t), new PointF(pc.X - t * 0.6f, pc.Y + t), new PointF(pc.X + t, pc.Y) });
            }
        }
        SkipButton(g, pal, prev, false, s.CanPrevious || !IsHandleCreated);
        SkipButton(g, pal, next, true, s.CanNext || !IsHandleCreated);
    }

    void SkipButton(Graphics g, Palette pal, PointF c, bool forward, bool enabled)
    {
        FillCircle(g, pal.Fill, c.X, c.Y, P(14));
        using (var b = new SolidBrush(enabled ? pal.Fg : pal.Fg2))
        {
            float s = P(4.5f), d = forward ? 1 : -1;
            for (int k = 0; k < 2; k++)
            {
                float x = c.X + d * (-P(4.5f) + k * P(4.5f));
                g.FillPolygon(b, new[] { new PointF(x, c.Y - s), new PointF(x, c.Y + s), new PointF(x + d * s, c.Y) });
            }
        }
    }

    static string Clock(TimeSpan t)
    {
        return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00")
                                 : t.Minutes + ":" + t.Seconds.ToString("00");
    }

    void NoteGlyph(Graphics g, Color c, RectangleF r)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = r.Width * 0.28f;
        using (var b = new SolidBrush(c))
        using (var pen = new Pen(c, Math.Max(1.5f, s * 0.12f)))
        {
            g.FillEllipse(b, cx - s * 0.7f, cy + s * 0.25f, s * 0.55f, s * 0.42f);
            g.FillEllipse(b, cx + s * 0.15f, cy + s * 0.05f, s * 0.55f, s * 0.42f);
            g.DrawLine(pen, cx - s * 0.17f, cy + s * 0.45f, cx - s * 0.17f, cy - s * 0.55f);
            g.DrawLine(pen, cx + s * 0.68f, cy + s * 0.25f, cx + s * 0.68f, cy - s * 0.75f);
            g.DrawLine(pen, cx - s * 0.17f, cy - s * 0.55f, cx + s * 0.68f, cy - s * 0.75f);
        }
    }

    // Library preview: a sample track with painted art.
    static MediaSession.Snapshot Sample()
    {
        return new MediaSession.Snapshot
        {
            Title = "Low Tide Radio", Artist = "The Harbour Lights", App = "Spotify", Playing = true, CanNext = true, CanPrevious = true,
            Position = TimeSpan.FromSeconds(84), Duration = TimeSpan.FromSeconds(221), PositionAtUtc = DateTime.UtcNow
        };
    }

    void PreviewArt(Graphics g, GraphicsPath clip, RectangleF r)
    {
        var state = g.Save();
        g.SetClip(clip);
        using (var sky = new LinearGradientBrush(new PointF(0, r.Top), new PointF(0, r.Bottom), Rgb(0xF08A5D), Rgb(0x3D1E52)))
            g.FillRectangle(sky, r);
        FillCircle(g, Rgb(0xFFD27A), r.X + r.Width * 0.68f, r.Y + r.Height * 0.32f, r.Width * 0.17f);
        using (var hill = new SolidBrush(Rgb(0x0D3B4F)))
            g.FillEllipse(hill, r.X + r.Width * 0.05f, r.Y + r.Height * 0.55f, r.Width * 0.9f, r.Height * 0.9f);
        g.Restore(state);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (media != null) { media.Changed -= OnMediaChanged; media.Dispose(); media = null; }
            if (art != null) { art.Dispose(); art = null; }
        }
        base.Dispose(disposing);
    }
}
