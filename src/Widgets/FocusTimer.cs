using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;

namespace DeskWidgets.Widgets
{
    // Small: a focus countdown shown as a ring, with start/pause and reset buttons.
    // Redraws once a second only while running; idle it costs nothing. The end time is saved,
    // so a running timer survives an app restart.
    sealed class FocusTimer : CardWidget
    {
        static readonly int[] Lengths = { 15, 25, 45, 60 };

        public FocusTimer(Section settings) : base(settings, false) { }

        int LengthSec { get { return Settings.GetInt("length", 25) * 60; } }

        // Running: "ends" holds the UTC end time. Paused: "left" holds the seconds remaining.
        bool Running { get { return Settings.Get("ends", "").Length > 0; } }

        int SecondsLeft
        {
            get
            {
                if (Running)
                {
                    long ends;
                    if (!long.TryParse(Settings.Get("ends", ""), out ends)) return 0;
                    return Math.Max(0, (int)Math.Ceiling((new DateTime(ends, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds));
                }
                return Math.Min(LengthSec, Settings.GetInt("left", LengthSec));
            }
        }

        void Start()
        {
            int left = SecondsLeft;
            if (left == 0) left = LengthSec;
            Settings.Remove("left");
            Settings.Set("ends", DateTime.UtcNow.AddSeconds(left).Ticks);
            Render();
        }

        void Pause()
        {
            int left = SecondsLeft;
            Settings.Remove("ends");
            Settings.Set("left", left);
            Render();
        }

        void Reset()
        {
            Settings.Remove("ends");
            Settings.Remove("left");
            Render();
        }

        protected override int NextFrameDelay()
        {
            if (!Running || !IsHandleCreated) return -1;
            long ends;
            long.TryParse(Settings.Get("ends", ""), out ends);
            double ms = (new DateTime(ends, DateTimeKind.Utc) - DateTime.UtcNow).TotalMilliseconds;
            if (ms <= 0) return 0;
            return (int)(ms % 1000) + 15; // wake just after each whole second ticks over
        }

        protected override void Draw(Graphics g)
        {
            if (Running && SecondsLeft == 0)
            {
                // Finished: stop, keep the empty ring and say so.
                Settings.Remove("ends");
                Settings.Set("left", 0);
                if (IsHandleCreated) SystemSounds.Asterisk.Play();
            }
            base.Draw(g);
        }

        // Geometry shared by drawing and hit testing.
        PointF RingCenter { get { return new PointF(P(16) + P(46), P(97)); } }
        PointF PlayCenter { get { return new PointF(W - P(16) - P(14), P(80)); } }
        PointF ResetCenter { get { return new PointF(W - P(16) - P(14), P(116)); } }

        static bool Within(Point p, PointF c, float r)
        {
            float dx = p.X - c.X, dy = p.Y - c.Y;
            return dx * dx + dy * dy <= r * r;
        }

        protected override bool IsButton(Point p)
        {
            return Within(p, PlayCenter, P(17)) || Within(p, ResetCenter, P(17));
        }

        protected override void OnWidgetClick(Point p)
        {
            if (Within(p, PlayCenter, P(17))) { if (Running) Pause(); else Start(); }
            else if (Within(p, ResetCenter, P(17))) Reset();
        }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            float m = P(16);
            int left = SecondsLeft, total = LengthSec;
            bool running = Running;
            string state = left == 0 ? "Done" : running ? "Running" : left == total ? "Ready" : "Paused";

            Label(g, "Focus", F(13, "semibold"), pal.Orange, m, m - P(1));
            Label(g, state, F(11, "regular"), pal.Fg2, W - m, m + P(1), P(80), StringAlignment.Far);

            // Ring: remaining time as the orange arc, starting at 12 o'clock.
            PointF c = RingCenter;
            float r = P(46) - P(4.5f), stroke = P(9);
            var box = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
            using (var track = new Pen(pal.Fill, stroke)) g.DrawEllipse(track, box);
            float sweep = total > 0 ? 360f * left / total : 0;
            if (sweep > 0.5f)
                using (var arc = new Pen(pal.Orange, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(arc, box, -90, sweep);
            string time = (left / 60) + ":" + (left % 60).ToString("00");
            Font tf = F(20, "semibold");
            Label(g, time, tf, pal.Fg, c.X, c.Y - tf.GetHeight() / 2, r * 2, StringAlignment.Center);

            // Buttons: start/pause (orange) and reset.
            PointF pc = PlayCenter, rc = ResetCenter;
            float br = P(14);
            FillCircle(g, pal.Orange, pc.X, pc.Y, br);
            using (var white = new SolidBrush(Color.White))
            {
                if (running)
                {
                    float bw = P(3.2f), bh = P(11);
                    g.FillRectangle(white, pc.X - P(4.6f), pc.Y - bh / 2, bw, bh);
                    g.FillRectangle(white, pc.X + P(1.4f), pc.Y - bh / 2, bw, bh);
                }
                else
                {
                    float s = P(6);
                    g.FillPolygon(white, new[] { new PointF(pc.X - s * 0.7f, pc.Y - s), new PointF(pc.X - s * 0.7f, pc.Y + s), new PointF(pc.X + s, pc.Y) });
                }
            }
            FillCircle(g, pal.Fill, rc.X, rc.Y, br);
            float ar = P(5.5f);
            using (var pen = new Pen(pal.Fg, P(1.7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, rc.X - ar, rc.Y - ar, ar * 2, ar * 2, -60, 300);
            using (var b = new SolidBrush(pal.Fg))
            {
                // Arrowhead at the start of the arc (about 1 o'clock), pointing clockwise.
                float ax = rc.X + ar * (float)Math.Cos(-Math.PI / 3), ay = rc.Y + ar * (float)Math.Sin(-Math.PI / 3), s = P(3.2f);
                g.FillPolygon(b, new[] { new PointF(ax - s, ay - s * 1.2f), new PointF(ax + s * 1.3f, ay - s * 0.2f), new PointF(ax - s * 0.3f, ay + s * 1.2f) });
            }
        }

        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            if (Running) menu.Add("Pause", Pause); else menu.Add(SecondsLeft == 0 || SecondsLeft == LengthSec ? "Start" : "Resume", Start);
            menu.Add("Reset", Reset);
            var len = menu.AddSubmenu("Length");
            int cur = Settings.GetInt("length", 25);
            foreach (int each in Lengths)
            {
                int min = each;
                len.AddChoice(min + " minutes", cur == min, delegate { Settings.Set("length", min); Reset(); });
            }
        }
    }
}
