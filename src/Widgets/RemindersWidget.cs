using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DeskWidgets.Widgets
{
    // Medium: open reminder count and up to four reminders you can tick off.
    // Click a circle to complete it, click anywhere else to add one.
    sealed class RemindersWidget : CardWidget
    {
        const int Rows = 4;

        sealed class Item { public string Text; public bool Done; }

        public RemindersWidget(Section settings) : base(settings, true) { }

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

        // Open reminders first, then recently completed ones to fill the space.
        List<int> VisibleOrder(List<Item> items)
        {
            var order = new List<int>();
            for (int i = 0; i < items.Count; i++) if (!items[i].Done) order.Add(i);
            for (int i = items.Count - 1; i >= 0; i--) if (items[i].Done) order.Add(i);
            return order.Take(Rows).ToList();
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
            return row >= 0 && row < VisibleOrder(Items).Count && p.X < ListX + P(26);
        }

        protected override void OnWidgetClick(Point p)
        {
            var items = Items;
            var order = VisibleOrder(items);
            int row = RowAt(p);
            if (row >= 0 && row < order.Count && p.X < ListX + P(26))
            {
                items[order[row]].Done = !items[order[row]].Done;
                Items = items;
                Render();
            }
            else Add();
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
            menu.Add("Add reminder…", Add);
            menu.Add("Clear completed", ClearCompleted);
        }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            float m = P(16);
            var items = Items;
            int open = items.Count(i => !i.Done);

            // Left: badge, count, title.
            float br = P(14);
            FillCircle(g, pal.Blue, m + br, m + br, br);
            using (var b = new SolidBrush(Color.White))
                for (int k = 0; k < 3; k++)
                {
                    float y = m + br - P(5) + k * P(5);
                    g.FillEllipse(b, m + br - P(6), y - P(1.2f), P(2.4f), P(2.4f));
                    g.FillRectangle(b, m + br - P(2), y - P(0.8f), P(8), P(1.6f));
                }
            Label(g, open.ToString(), F(30, "semibold"), pal.Fg, m - P(1), H - m - P(58));
            Label(g, "Reminders", F(13, "semibold"), pal.Blue, m, H - m - P(16), P(100), StringAlignment.Near);

            // Right: rows.
            var order = VisibleOrder(items);
            float x = ListX, right = W - m;
            if (order.Count == 0)
                Label(g, "All done. Click to add a reminder.", F(13, "regular"), pal.Fg2, x, H / 2 - P(9), right - x, StringAlignment.Near);
            for (int row = 0; row < order.Count; row++)
            {
                var it = items[order[row]];
                float top = RowTop(row), cy = top + RowH / 2, r = P(9);
                if (it.Done)
                {
                    FillCircle(g, pal.Blue, x + r, cy, r);
                    FillCircle(g, pal.Bg, x + r, cy, P(3.2f));
                }
                else
                    using (var pen = new Pen(pal.Fg2, P(1.5f))) g.DrawEllipse(pen, x, cy - r, r * 2, r * 2);

                Font f = F(13, "regular");
                float tx = x + P(26), ty = cy - f.GetHeight() / 2;
                Label(g, it.Text, f, it.Done ? pal.Fg2 : pal.Fg, tx, ty, right - tx, StringAlignment.Near);
                if (it.Done)
                    using (var pen = new Pen(pal.Fg2, Math.Max(1f, P(1))))
                    {
                        float tw = Math.Min(right - tx, TextWidth(g, it.Text, f));
                        g.DrawLine(pen, tx, cy + P(1), tx + tw, cy + P(1));
                    }
                if (row < order.Count - 1)
                    using (var sep = new Pen(pal.Sep, 1)) g.DrawLine(sep, tx, top + RowH, right, top + RowH);
            }
        }
    }
}
