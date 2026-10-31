using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using DeskWidgets;
using DeskWidgets.Widgets;

// Medium: open reminder count and up to four reminders you can tick off.
// Click a circle to complete it, click anywhere else to add one. Optionally shows today's
// events from your Calendar widget as reminders too (ticking one only affects this list).
[DeskWidget(Name = "Reminders", Description = "Tick off reminders and today's events")]
public sealed class RemindersWidget : CardWidget
{
    const int Rows = 4;

    sealed class Item { public string Text; public bool Done; }

    // One line in the list: either a stored reminder or one of today's calendar events.
    sealed class Row
    {
        public string Text, Time;
        public bool Done;
        public int Item = -1;      // index into Items, for reminders
        public string EventKey;    // for calendar events
    }

    public RemindersWidget(Section settings) : base(settings, true) { }

    bool ShowEvents { get { return Settings.GetBool("events", false); } }

    protected override void OnStart()
    {
        PluginApi.CalendarChanged += OnCalendarChanged;
    }

    void OnCalendarChanged()
    {
        if (ShowEvents) Render();
    }

    // Redraw each minute while showing events, so finished ones drop off.
    protected override int NextFrameDelay()
    {
        if (!ShowEvents || !IsHandleCreated) return -1;
        DateTime now = DateTime.Now;
        return (60 - now.Second) * 1000 - now.Millisecond + 300;
    }

    // Stored one per line as "0|text" (open) or "1|text" (done).
    List<Item> Items
    {
        get
        {
            string raw = Settings.GetText("items", "0|Tick a circle to finish\n0|Click elsewhere to add one");
            return raw.Split('\n').Where(l => l.Length > 2)
                      .Select(l => new Item { Done = l[0] == '1', Text = l.Substring(2) }).ToList();
        }
        set
        {
            Settings.SetText("items", string.Join("\n", value.Select(i => (i.Done ? "1|" : "0|") + i.Text.Replace("\n", " "))));
        }
    }

    // Calendar events ticked off today, as "title|startTicks" keys.
    HashSet<string> DoneEvents
    {
        get { return new HashSet<string>(Settings.GetText("doneEvents", "").Split('\n').Where(k => k.Length > 0)); }
    }

    static string KeyOf(CalEvent e) { return e.Title.Replace('\n', ' ') + "|" + e.Start.Ticks; }

    // Today's events (still to come, or ticked) first in time order, then open reminders, then
    // completed ones to fill the space.
    List<Row> BuildRows(List<Item> items)
    {
        var rows = new List<Row>();
        var done = DoneEvents;
        if (ShowEvents && IsHandleCreated)
        {
            DateTime now = DateTime.Now, today = now.Date;
            var culture = CultureInfo.CurrentCulture;
            foreach (var e in PluginApi.CalendarEvents.Where(e => e.Start < today.AddDays(1) && e.End > today).OrderBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Start))
            {
                string key = KeyOf(e);
                bool isDone = done.Contains(key);
                if (!isDone && e.End <= now) continue; // over and not ticked: let it go
                rows.Add(new Row { Text = e.Title, Time = e.AllDay ? "All day" : e.Start.ToString("t", culture), Done = isDone, EventKey = key });
            }
        }
        for (int i = 0; i < items.Count; i++) if (!items[i].Done) rows.Add(new Row { Text = items[i].Text, Item = i });
        var open = rows.Where(r => !r.Done).ToList();
        var finished = rows.Where(r => r.Done).ToList();
        for (int i = items.Count - 1; i >= 0; i--) if (items[i].Done) finished.Add(new Row { Text = items[i].Text, Item = i, Done = true });
        return open.Concat(finished).ToList();
    }

    float ListX { get { return P(16) + P(100) + P(12); } }
    float RowTop(int row) { return P(14) + row * P(35.5f); }
    float RowH { get { return P(35.5f); } }

    int RowAt(Point p)
    {
        if (p.X < ListX - P(4)) return -1;
        int row = (int)((p.Y - P(14)) / RowH);
        return p.Y < P(14) || row >= Rows ? -1 : row;
    }

    protected override bool IsButton(Point p)
    {
        int row = RowAt(p);
        return row >= 0 && row < Math.Min(Rows, BuildRows(Items).Count) && p.X < ListX + P(26);
    }

    protected override void OnWidgetClick(Point p)
    {
        var items = Items;
        var rows = BuildRows(items);
        int row = RowAt(p);
        if (row < 0 || row >= Math.Min(Rows, rows.Count) || p.X >= ListX + P(26)) { Add(); return; }
        Row r = rows[row];
        if (r.Item >= 0)
        {
            items[r.Item].Done = !items[r.Item].Done;
            Items = items;
        }
        else
        {
            // Keep only today's keys so the list doesn't grow forever.
            long todayTicks = DateTime.Today.Ticks, tomorrowTicks = DateTime.Today.AddDays(1).Ticks;
            var done = new HashSet<string>(DoneEvents.Where(k =>
            {
                long t;
                return long.TryParse(k.Substring(k.LastIndexOf('|') + 1), out t) && t >= todayTicks && t < tomorrowTicks;
            }));
            if (!done.Remove(r.EventKey)) done.Add(r.EventKey);
            Settings.SetText("doneEvents", string.Join("\n", done));
        }
        Render();
    }

    void Add()
    {
        var values = InputDialog.Ask("New reminder", null, new[] { new InputDialog.Field { Label = "Reminder" } }, "Add");
        if (values == null || values[0].Trim().Length == 0) return;
        var items = Items;
        items.Add(new Item { Text = values[0].Trim() });
        Items = items;
        Render();
    }

    void ClearCompleted()
    {
        Items = Items.Where(i => !i.Done).ToList();
        Render();
    }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        menu.Add("Add Reminder…", Add);
        menu.Add("Clear Completed", ClearCompleted);
        if (PluginApi.HasCalendar)
            menu.AddCheck("Show Today's Calendar Events", ShowEvents, delegate { Settings.Set("events", !ShowEvents); Render(); });
    }

    protected override void DrawContent(Graphics g, Palette pal)
    {
        float m = P(16);
        var items = Items;
        var rows = BuildRows(items);
        int open = rows.Count(r => !r.Done);

        // Left: badge, count, title.
        float br = P(14);
        FillCircle(g, pal.Blue, m + br, m + br, br);
        using (var b = new SolidBrush(pal.OnAccent))
            for (int k = 0; k < 3; k++)
            {
                float y = m + br - P(5) + k * P(5);
                g.FillEllipse(b, m + br - P(6), y - P(1.2f), P(2.4f), P(2.4f));
                g.FillRectangle(b, m + br - P(2), y - P(0.8f), P(8), P(1.6f));
            }
        Label(g, open.ToString(), F(30, "semibold"), pal.Fg, m - P(1), H - m - P(58));
        Label(g, "Reminders", F(13, "semibold"), pal.Blue, m, H - m - P(16), P(100), StringAlignment.Near);

        // Right: rows.
        float x = ListX, right = W - m;
        if (rows.Count == 0)
            Label(g, "All done. Click to add a reminder.", F(13, "regular"), pal.Fg2, x, H / 2 - P(9), right - x, StringAlignment.Near);
        int shown = Math.Min(Rows, rows.Count);
        for (int i = 0; i < shown; i++)
        {
            Row it = rows[i];
            float top = RowTop(i), cy = top + RowH / 2, r = P(9);
            Color ring = it.EventKey != null ? pal.Red : pal.Blue; // events wear the Calendar's red
            if (it.Done)
            {
                FillCircle(g, ring, x + r, cy, r);
                FillCircle(g, pal.OnAccent, x + r, cy, P(3.2f));
            }
            else
                using (var pen = new Pen(it.EventKey != null ? ring : pal.Fg2, P(1.5f))) g.DrawEllipse(pen, x, cy - r, r * 2, r * 2);

            Font f = F(13, "regular");
            float tx = x + P(26), ty = cy - f.GetHeight() / 2, tw = right - tx;
            if (it.Time != null)
            {
                Font tf = F(11, "semibold");
                float timeW = TextWidth(g, it.Time, tf) + P(6);
                Label(g, it.Time, tf, it.Done ? pal.Fg2 : pal.Red, tx, cy - tf.GetHeight() / 2, timeW, StringAlignment.Near);
                tx += timeW;
                tw -= timeW;
            }
            Label(g, it.Text, f, it.Done ? pal.Fg2 : pal.Fg, tx, ty, tw, StringAlignment.Near);
            if (it.Done)
                using (var pen = new Pen(pal.Fg2, Math.Max(1f, P(1))))
                {
                    float lw = Math.Min(tw, TextWidth(g, it.Text, f));
                    g.DrawLine(pen, tx, cy + P(1), tx + lw, cy + P(1));
                }
            if (i < shown - 1)
                using (var sep = new Pen(pal.Sep, 1)) g.DrawLine(sep, x + P(26), top + RowH, right, top + RowH);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) PluginApi.CalendarChanged -= OnCalendarChanged;
        base.Dispose(disposing);
    }
}
