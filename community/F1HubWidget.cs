using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using DeskWidgets;
using DeskWidgets.Widgets;

[DeskWidget(Name = "F1 Race Hub", Description = "Next race, its sessions, a countdown and the standings")]
public sealed class F1HubWidget : CardWidget
{
    F1.Weekend next;
    List<F1.Standing> drivers;
    int afterRound;
    PointF[] track;
    string trackFor, status;
    bool busy;
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };

    public F1HubWidget(Section settings) : base(settings, CardSize.Large)
    {
        refresh.Tick += delegate { Fetch(); };
        LoadCache();
    }

    protected override void OnStart()
    {
        refresh.Start();
        Fetch();
    }

    protected override int NextFrameDelay()
    {
        if (!IsHandleCreated || next == null) return -1;
        F1.Session s = NextSession(next, DateTime.UtcNow);
        if (s == null) return -1;
        DateTime now = DateTime.UtcNow;
        double ms = ((s.Start > now ? s.Start : s.End) - now).TotalMilliseconds;
        if (s.Start > now && ms < 3600000) return (int)(ms % 1000) + 15;
        return (int)(60000 - now.Second * 1000 - now.Millisecond) + 15;
    }

    void Apply(string nextJson, string standingsJson)
    {
        next = F1.ParseNext(nextJson);
        drivers = F1.ParseStandings(standingsJson, out afterRound);
    }

    void LoadCache()
    {
        string file = CacheFile("f1-hub");
        if (file == null || !File.Exists(file)) return;
        try
        {
            string[] lines = File.ReadAllLines(file);
            Apply(lines[0], lines[1]);
            if (lines.Length > 3 && lines[3].Length > 0) { trackFor = lines[2]; track = ParseTrack(lines[3]); }
        }
        catch (Exception) { next = null; drivers = null; }
    }

    void Fetch()
    {
        if (busy) return;
        busy = true;
        string file = CacheFile("f1-hub");
        string cacheDir = PluginApi.DataDir == null ? null : Path.Combine(PluginApi.DataDir, "cache");
        string haveTrackFor = track == null ? null : trackFor;
        Background(() =>
        {
            string a = F1.FetchNextJson(), b = F1.FetchStandingsJson();
            F1.Weekend w = F1.ParseNext(a);
            PointF[] t = null;
            if (w != null && w.Name != haveTrackFor)
                try { t = F1.Track(w, cacheDir); } catch (Exception) { }
            return new object[] { a, b, t, w == null ? null : w.Name };
        }, (res, error) =>
        {
            busy = false;
            if (error != null) { status = "Offline"; Render(); return; }
            try
            {
                Apply((string)res[0], (string)res[1]);
                status = null;
                if (res[2] != null || (string)res[3] != trackFor) { track = (PointF[])res[2]; trackFor = (string)res[3]; }
                if (file != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllLines(file, new[] { OneLine((string)res[0]), OneLine((string)res[1]), trackFor ?? "", FormatTrack(track) });
                }
            }
            catch (Exception) { status = "No data"; }
            Render();
        });
    }

    static string OneLine(string json) { return json.Replace("\r", " ").Replace("\n", " "); }

    static string FormatTrack(PointF[] t)
    {
        if (t == null) return "";
        return string.Join(";", t.Select(p => p.X.ToString("0.####", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.####", CultureInfo.InvariantCulture)));
    }

    static PointF[] ParseTrack(string s)
    {
        return s.Split(';').Select(xy =>
        {
            string[] v = xy.Split(',');
            return new PointF(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture));
        }).ToArray();
    }

    static F1.Session NextSession(F1.Weekend w, DateTime now)
    {
        return w.Sessions.FirstOrDefault(s => s.End > now);
    }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        menu.Add("Refresh Now", Fetch);
    }

    protected override void DrawContent(Graphics g, Palette pal)
    {
        bool sample = !IsHandleCreated;
        F1.Weekend w = sample ? SampleWeekend() : next;
        List<F1.Standing> table = sample ? SampleDrivers() : drivers;
        PointF[] outline = sample ? SampleTrack : track;
        int round = sample ? 18 : afterRound;
        float m = P(16);
        DateTime now = DateTime.UtcNow;

        if (table == null)
        {
            Label(g, status ?? "Loading F1…", F(15, "semibold"), pal.Fg, W / 2, H / 2 - P(12), W - m * 2, StringAlignment.Center);
            return;
        }

        float textW = W - m * 2 - (outline != null && w != null ? P(158) : 0);
        if (w == null)
        {
            Label(g, "SEASON COMPLETE", F(11, "semibold"), pal.Red, m, P(16), textW, StringAlignment.Near);
            Label(g, "See you next year.", F(20, "semibold"), pal.Fg, m, P(31), textW, StringAlignment.Near);
        }
        else
        {
            bool started = now >= w.Sessions[0].Start;
            Label(g, ("Round " + w.Round + " · " + (started ? "Race weekend" : "Next race")).ToUpperInvariant(),
                F(11, "semibold"), pal.Red, m, P(16), textW, StringAlignment.Near);
            Label(g, w.Place, F(20, "semibold"), pal.Fg, m, P(31), textW, StringAlignment.Near);
            Label(g, Dates(w) + (status != null ? "  ·  " + status : ""), F(13, "regular"), pal.Fg2, m, P(58), textW, StringAlignment.Near);
            if (outline != null) DrawTrack(g, pal, outline, new RectangleF(W - m - P(150), P(14), P(150), P(72)));

            F1.Session s = NextSession(w, now);
            string label = s == null ? "Weekend" : now >= s.Start ? s.Name : s.Name + " in";
            string value = s == null ? "Finished" : now >= s.Start ? "Live now" : Span(s.Start - now);
            Label(g, label, F(11, "regular"), pal.Fg2, m, P(96), W - m * 2, StringAlignment.Near);
            Label(g, value, F(28, "semibold"), pal.Fg, m - P(1), P(110), W - m * 2, StringAlignment.Near);
            DrawStrip(g, pal, w, s, now, new RectangleF(m, P(150), W - m * 2, P(54)));
        }

        float top = w == null ? P(80) : P(216);
        using (var pen = new Pen(pal.Sep, 1f)) g.DrawLine(pen, m, top, W - m, top);
        Label(g, "DRIVERS", F(11, "semibold"), pal.Fg2, m, top + P(8), P(120), StringAlignment.Near);
        if (round > 0) Label(g, "After round " + round, F(11, "regular"), pal.Fg2, W - m, top + P(8), P(160), StringAlignment.Far);
        DrawStandings(g, pal, table, top + P(28), m);
    }

    void DrawTrack(Graphics g, Palette pal, PointF[] pts, RectangleF box)
    {
        float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
        float pad = P(5);
        float scale = Math.Min((box.Width - pad * 2) / Math.Max(0.01f, maxX - minX), (box.Height - pad * 2) / Math.Max(0.01f, maxY - minY));
        float ox = box.X + (box.Width - (maxX - minX) * scale) / 2, oy = box.Y + (box.Height - (maxY - minY) * scale) / 2;
        PointF[] screen = pts.Select(p => new PointF(ox + (p.X - minX) * scale, oy + (p.Y - minY) * scale)).ToArray();
        using (var pen = new Pen(pal.Fg2, P(2.2f)) { LineJoin = LineJoin.Round })
            g.DrawPolygon(pen, screen);
        if (!IsVibrant) FillCircle(g, pal.Bg, screen[0].X, screen[0].Y, P(4.6f));
        FillCircle(g, pal.Red, screen[0].X, screen[0].Y, P(3.2f));
    }

    void DrawStrip(Graphics g, Palette pal, F1.Weekend w, F1.Session nextSession, DateTime now, RectangleF r)
    {
        FillRound(g, pal.Fill, r, P(10));
        int n = w.Sessions.Count;
        float colW = r.Width / n;
        for (int i = 0; i < n; i++)
        {
            F1.Session s = w.Sessions[i];
            bool done = now >= s.End, current = s == nextSession;
            float cx = r.X + colW * i + colW / 2;
            DateTime local = s.Start.ToLocalTime();
            Label(g, s.Short, F(11, "semibold"), current ? pal.Red : done ? pal.Fg2 : pal.Fg, cx, r.Y + P(8), colW - P(4), StringAlignment.Center);
            Label(g, local.ToString("ddd", CultureInfo.CurrentCulture), F(11, "regular"), done ? pal.Fg2 : pal.Fg, cx, r.Y + P(22), colW - P(4), StringAlignment.Center);
            Label(g, local.ToString("t", CultureInfo.CurrentCulture), F(11, "regular"), pal.Fg2, cx, r.Y + P(36), colW - P(4), StringAlignment.Center);
        }
    }

    void DrawStandings(Graphics g, Palette pal, List<F1.Standing> table, float y0, float m)
    {
        float rh = P(24);
        double leader = table.Count > 0 ? table[0].Points : 0;
        for (int i = 0; i < Math.Min(5, table.Count); i++)
        {
            F1.Standing d = table[i];
            float y = y0 + rh * i;
            if (i > 0) using (var pen = new Pen(pal.Sep, 1f)) g.DrawLine(pen, m + P(31), y - P(3), W - m, y - P(3));
            Label(g, d.Position.ToString(CultureInfo.CurrentCulture), F(13, "semibold"), pal.Fg2, m + P(14), y, P(20), StringAlignment.Far);
            FillRound(g, IsVibrant ? pal.Fg : F1.TeamColor(d.Team), new RectangleF(m + P(21), y + P(2), P(3), P(14)), P(1.5f));
            Label(g, d.Code, F(13, "semibold"), pal.Fg, m + P(31), y, P(40), StringAlignment.Near);
            Label(g, d.Family, F(13, "regular"), pal.Fg2, m + P(70), y, W - m * 2 - P(70) - P(110), StringAlignment.Near);
            Label(g, Points(d.Points), F(13, "semibold"), pal.Fg, W - m - P(46), y, P(60), StringAlignment.Far);
            if (i > 0) Label(g, "−" + Points(leader - d.Points), F(13, "regular"), pal.Fg2, W - m, y, P(44), StringAlignment.Far);
        }
    }

    static string Points(double p) { return p.ToString("0.#", CultureInfo.CurrentCulture); }

    static string Dates(F1.Weekend w)
    {
        DateTime a = w.Sessions[0].Start.ToLocalTime(), b = w.Race.Start.ToLocalTime();
        return a.ToString("d MMM", CultureInfo.CurrentCulture) + " – " + b.ToString("d MMM", CultureInfo.CurrentCulture);
    }

    static string Span(TimeSpan t)
    {
        if (t.TotalDays >= 1) return (int)t.TotalDays + "d " + t.Hours + "h";
        if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes.ToString("00") + "m";
        return t.Minutes + ":" + t.Seconds.ToString("00");
    }

    static F1.Weekend SampleWeekend()
    {
        DateTime q = DateTime.UtcNow.AddHours(3).AddMinutes(12);
        var w = new F1.Weekend { Round = 19, Name = "Mexico City Grand Prix", Place = "Mexico City" };
        string[][] names = { new[] { "Practice 1", "FP1" }, new[] { "Practice 2", "FP2" }, new[] { "Practice 3", "FP3" }, new[] { "Qualifying", "Quali" }, new[] { "Race", "Race" } };
        double[] offsets = { -26.5, -23, -3.5, 0, 23 };
        for (int i = 0; i < 5; i++)
        {
            DateTime s = q.AddHours(offsets[i]);
            w.Sessions.Add(new F1.Session { Name = names[i][0], Short = names[i][1], Start = s, End = s.AddMinutes(i == 4 ? 120 : 60) });
        }
        return w;
    }

    static List<F1.Standing> SampleDrivers()
    {
        return new List<F1.Standing>
        {
            new F1.Standing { Position = 1, Code = "ANT", Family = "Antonelli", Team = "mercedes", Points = 365 },
            new F1.Standing { Position = 2, Code = "RUS", Family = "Russell", Team = "mercedes", Points = 242 },
            new F1.Standing { Position = 3, Code = "HAM", Family = "Hamilton", Team = "ferrari", Points = 241 },
            new F1.Standing { Position = 4, Code = "LEC", Family = "Leclerc", Team = "ferrari", Points = 226 },
            new F1.Standing { Position = 5, Code = "NOR", Family = "Norris", Team = "mclaren", Points = 214 }
        };
    }

    static readonly PointF[] SampleTrack = new[]
    {
        new PointF(.46f, .114f), new PointF(.80f, .09f), new PointF(.88f, .12f), new PointF(.86f, .195f), new PointF(.71f, .25f),
        new PointF(.68f, .29f), new PointF(.71f, .328f), new PointF(.84f, .38f), new PointF(.88f, .46f), new PointF(.80f, .51f),
        new PointF(.58f, .51f), new PointF(.52f, .45f), new PointF(.53f, .435f), new PointF(.50f, .38f), new PointF(.47f, .375f),
        new PointF(.23f, .46f), new PointF(.09f, .42f), new PointF(.075f, .34f), new PointF(.095f, .21f), new PointF(.14f, .14f)
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) refresh.Dispose();
        base.Dispose(disposing);
    }
}
