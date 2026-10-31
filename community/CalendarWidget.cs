using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DeskWidgets;
using DeskWidgets.Widgets;

// Medium: today's date and the next events on the left, the month on the right.
// The month needs nothing. Events come from calendar subscription links (.ics: Google,
// Outlook, iCloud), stored encrypted, fetched every 15 minutes and cached for offline use.
[DeskWidget(Name = "Calendar", Description = "Today, your next events and the month")]
public sealed class CalendarWidget : CardWidget
{
    List<CalEvent> events = new List<CalEvent>();
    string status;
    bool busy;
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };

    public CalendarWidget(Section settings) : base(settings, true)
    {
        refresh.Tick += delegate { Fetch(); };
        LoadCache();
    }

    // Subscription links, one per line, encrypted as a whole.
    List<string> Links
    {
        get
        {
            string plain = PluginApi.Unprotect(Settings.Get("links", ""));
            return plain == null ? new List<string>() : plain.Split('\n').Where(l => l.Trim().Length > 0).ToList();
        }
        set
        {
            if (value.Count == 0) Settings.Remove("links");
            else Settings.Set("links", PluginApi.Protect(string.Join("\n", value)));
        }
    }

    protected override void OnStart()
    {
        refresh.Start();
        Publish();
        Fetch();
    }

    // Share the events with other widgets (Reminders can list today's events).
    void Publish()
    {
        if (IsHandleCreated) PluginApi.PublishCalendar(this, Links.Count > 0 ? events : null);
    }

    // Redraw each minute so "next event" and the date stay current.
    protected override int NextFrameDelay()
    {
        if (!IsHandleCreated) return -1;
        DateTime now = DateTime.Now;
        return (60 - now.Second) * 1000 - now.Millisecond + 200;
    }

    // ---------- data ----------
    void Fetch()
    {
        var links = Links;
        if (busy || links.Count == 0) { Render(); return; }
        busy = true;
        string file = CacheFile("calendar");
        Background(() =>
        {
            DateTime from = DateTime.Today, to = DateTime.Today.AddDays(14);
            var all = new List<CalEvent>();
            int failed = 0;
            for (int i = 0; i < links.Count; i++)
            {
                try { all.AddRange(Ics.Parse(Http.Get(links[i].Trim()), from, to, i)); }
                catch (Exception) { failed++; }
            }
            if (failed == links.Count) throw new InvalidOperationException("no calendar could be read");
            return all.OrderBy(e => e.Start).ToList();
        }, (result, error) =>
        {
            busy = false;
            if (error != null) status = "Offline";
            else
            {
                events = result;
                status = null;
                SaveCache(file);
                Publish();
            }
            Render();
        });
    }

    void SaveCache(string file)
    {
        if (file == null) return;
        var sb = new StringBuilder();
        foreach (var e in events)
            sb.Append(e.Start.Ticks).Append('|').Append(e.End.Ticks).Append('|').Append(e.AllDay ? 1 : 0).Append('|')
              .Append(e.Calendar).Append('|').Append(e.Title.Replace('\n', ' ')).Append('\n');
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, PluginApi.Protect(sb.ToString()));
        }
        catch (Exception) { }
    }

    void LoadCache()
    {
        string file = CacheFile("calendar");
        if (file == null || !File.Exists(file)) return;
        string plain = PluginApi.Unprotect(File.ReadAllText(file));
        if (plain == null) return;
        foreach (string line in plain.Split('\n'))
        {
            string[] f = line.Split(new[] { '|' }, 5);
            if (f.Length < 5) continue;
            long a, b;
            int c;
            if (!long.TryParse(f[0], out a) || !long.TryParse(f[1], out b) || !int.TryParse(f[3], out c)) continue;
            events.Add(new CalEvent { Start = new DateTime(a), End = new DateTime(b), AllDay = f[2] == "1", Calendar = c, Title = f[4] });
        }
    }

    void AddLink()
    {
        var values = InputDialog.Ask("Add a calendar",
            "Paste your calendar's private subscription link (.ics). Google Calendar: Settings › your calendar › " +
            "\"Secret address in iCal format\". Outlook: Settings › Calendar › Shared calendars › Publish. iCloud: " +
            "share the calendar as a public link. It's stored encrypted for your Windows account only.",
            new[] { new InputDialog.Field { Label = "Calendar link", Secret = true } }, "Add");
        if (values == null || values[0].Trim().Length == 0) return;
        var links = Links;
        links.Add(values[0].Trim());
        Links = links;
        Fetch();
    }

    // ---------- interaction ----------
    protected override void OnWidgetClick(Point p)
    {
        if (Links.Count == 0) AddLink();
    }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        menu.Add("Add a Calendar…", AddLink);
        int n = Links.Count;
        if (n > 0)
        {
            menu.Add("Refresh Now", Fetch);
            menu.AddDestructive(n == 1 ? "Remove the Calendar" : "Remove All " + n + " Calendars", delegate
            {
                Links = new List<string>();
                events.Clear();
                Publish();
                string file = CacheFile("calendar");
                if (file != null && File.Exists(file)) File.Delete(file);
                Render();
            });
        }
    }

    // ---------- drawing ----------
    protected override void DrawContent(Graphics g, Palette pal)
    {
        float m = P(16);
        DateTime now = DateTime.Now, today = now.Date;
        var culture = CultureInfo.CurrentCulture;
        Color[] calColors = { pal.Blue, pal.Green, pal.Purple, pal.Orange };

        // Left column: weekday, date, the next two events.
        float leftW = P(122);
        Label(g, culture.DateTimeFormat.GetDayName(today.DayOfWeek).ToUpper(culture), F(11, "semibold"), pal.Red, m, m - P(2), leftW, StringAlignment.Near);
        Label(g, today.Day.ToString(culture), F(38, "light"), pal.Fg, m - P(2), m + P(8), leftW, StringAlignment.Near);

        List<CalEvent> upcoming = IsHandleCreated ? Upcoming(now) : SampleEvents(today);
        float y = P(80);
        if (upcoming.Count == 0)
        {
            bool linked = !IsHandleCreated || Links.Count > 0;
            Label(g, linked ? (status ?? "No more events today") : "No calendar linked", F(11, "regular"), pal.Fg2, m, y, leftW, StringAlignment.Near);
            if (!linked) Label(g, "Click to add one", F(11, "semibold"), pal.Blue, m, y + P(15), leftW, StringAlignment.Near);
        }
        foreach (var e in upcoming.Take(2))
        {
            Color c = calColors[e.Calendar % calColors.Length];
            FillRound(g, c, new RectangleF(m, y + P(1), P(3), P(26)), P(1.5f));
            Label(g, e.Title, F(13, "semibold"), pal.Fg, m + P(8), y - P(1), leftW - P(8), StringAlignment.Near);
            Label(g, When(e, today, culture), F(11, "regular"), pal.Fg2, m + P(8), y + P(15), leftW - P(8), StringAlignment.Near);
            y += P(34);
        }

        // Right: the month grid.
        float gx = m + leftW + P(12), gw = W - m - gx, cw = gw / 7f;
        Label(g, culture.DateTimeFormat.GetMonthName(today.Month).ToUpper(culture), F(11, "semibold"), pal.Red, gx + cw * 0.5f - P(4), m - P(2), gw, StringAlignment.Near);
        DayOfWeek first = culture.DateTimeFormat.FirstDayOfWeek;
        for (int i = 0; i < 7; i++)
        {
            string initial = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)first + i) % 7)).Substring(0, 1).ToUpper(culture);
            Label(g, initial, F(11, "semibold"), pal.Fg2, gx + cw * i + cw / 2, P(32), cw, StringAlignment.Center);
        }
        DateTime monthStart = new DateTime(today.Year, today.Month, 1);
        int lead = ((int)monthStart.DayOfWeek - (int)first + 7) % 7;
        int days = DateTime.DaysInMonth(today.Year, today.Month);
        int rows = (lead + days + 6) / 7;
        float rowH = Math.Min(P(17), (H - m - P(50)) / rows);
        Color faded = Color.FromArgb(110, pal.Fg2);
        for (int cell = 0; cell < rows * 7; cell++)
        {
            DateTime d = monthStart.AddDays(cell - lead);
            float cx = gx + cw * (cell % 7) + cw / 2, cy = P(50) + rowH * (cell / 7) + rowH / 2;
            Font f = F(11, d == today ? "bold" : "semibold");
            if (d == today)
            {
                FillCircle(g, pal.Red, cx, cy, Math.Min(cw, rowH) * 0.5f);
                Label(g, d.Day.ToString(culture), f, pal.OnAccent, cx, cy - f.GetHeight() / 2, cw, StringAlignment.Center);
            }
            else Label(g, d.Day.ToString(culture), f, d.Month == today.Month ? pal.Fg : faded, cx, cy - f.GetHeight() / 2, cw, StringAlignment.Center);
        }
    }

    // Events still to come today and tomorrow, all-day ones first within each day.
    List<CalEvent> Upcoming(DateTime now)
    {
        DateTime end = now.Date.AddDays(2);
        return events.Where(e => e.End > now && e.Start < end)
                     .OrderBy(e => e.Start.Date).ThenBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start).ToList();
    }

    static string When(CalEvent e, DateTime today, CultureInfo c)
    {
        string day = e.Start.Date <= today ? "" : e.Start.Date == today.AddDays(1) ? "Tomorrow, " : e.Start.ToString("ddd, ", c);
        if (e.AllDay) return e.Start.Date <= today ? "All day" : day + "all day";
        string t = e.Start.ToString("t", c);
        if (e.Start.Date <= today && e.End.Date == e.Start.Date && e.End > e.Start) t += " – " + e.End.ToString("t", c);
        return day + t;
    }

    static List<CalEvent> SampleEvents(DateTime today)
    {
        return new List<CalEvent>
        {
            new CalEvent { Title = "Design sync", Start = today.AddHours(16), End = today.AddHours(16.5), Calendar = 0 },
            new CalEvent { Title = "Gym", Start = today.AddHours(18.5), End = today.AddHours(19.5), Calendar = 1 },
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose();
            PluginApi.PublishCalendar(this, null);
        }
        base.Dispose(disposing);
    }
}
