using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskWidgets
{
    static class Ui
    {
        static int Px(float v) { return WidgetWindow.Px(v); }

        // Runs on the UI thread once the current window message is done (works before Application.Run too).
        public static Timer Later(Action action, int delay = 1)
        {
            var t = new Timer { Interval = delay };
            t.Tick += delegate { t.Dispose(); action(); };
            t.Start();
            return t;
        }
    }

    enum ButtonStyle { Primary, Grey, Tinted, Destructive }

    // Apple's capsule buttons (§8.3): Primary is the one blue-filled button per group; Grey and
    // Tinted are the quiet ones; Destructive is a grey capsule with red text.
    sealed class PillButton : Button
    {
        public ButtonStyle Style;
        bool hover, pressed;

        public PillButton(string text, ButtonStyle style)
        {
            Text = text;
            Style = style;
            Font = Theme.Button;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            int w = TextRenderer.MeasureText(text, Font).Width + WidgetWindow.Px(32);
            Size = new Size(Math.Max(WidgetWindow.Px(76), w), WidgetWindow.Px(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, text;
            switch (Style)
            {
                case ButtonStyle.Primary: fill = Theme.PrimaryFill; text = Theme.OnPrimary; break;
                case ButtonStyle.Tinted: fill = Theme.Tinted; text = Theme.Link; break;
                case ButtonStyle.Destructive: fill = Theme.Fill; text = Theme.Destructive; break;
                default: fill = Theme.Fill; text = Theme.Label; break;
            }
            Theme.FillRound(g, fill, r, r.Height / 2);
            if (hover || pressed) // brightness up on hover, down when pressed
                Theme.FillRound(g, pressed ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(Style == ButtonStyle.Primary ? 28 : 14, 255, 255, 255), r, r.Height / 2);
            if (Focused && ShowFocusCues)
                using (var path = Theme.RoundRect(new RectangleF(1, 1, Width - 3, Height - 3), (Height - 3) / 2f))
                using (var pen = new Pen(Color.FromArgb(153, Theme.Link), 2f))
                    g.DrawPath(pen, path);
            if (!Enabled) text = Color.FromArgb(110, text);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, Color.Transparent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // A scroll panel with smooth mouse-wheel interpolation: intercepts WM_MOUSEWHEEL and eases
    // toward the target position instead of jumping. Scrollbar drags and programmatic scrolls stay
    // instant. Used for the library window's content area.
    sealed class SmoothPanel : Panel
    {
        readonly Timer anim = new Timer { Interval = 16 };
        float target;
        bool animating;

        public SmoothPanel()
        {
            anim.Tick += OnTick;
        }

        float MaxScroll
        {
            get
            {
                int max = AutoScrollMinSize.Height - ClientSize.Height;
                return max > 0 ? max : 0;
            }
        }

        void OnTick(object s, EventArgs e)
        {
            float current = -AutoScrollPosition.Y;
            target = Math.Min(target, MaxScroll);
            float diff = target - current;
            if (Math.Abs(diff) < 0.5f)
            {
                AutoScrollPosition = new Point(0, (int)Math.Round(target));
                anim.Stop();
                animating = false;
                return;
            }
            AutoScrollPosition = new Point(0, (int)Math.Round(current + diff * 0.25f));
        }

        public void ScrollToTop()
        {
            anim.Stop();
            animating = false;
            target = 0;
            AutoScrollPosition = Point.Empty;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)(m.WParam.ToInt64() >> 16);
                float step = -(delta / 120f) * WidgetWindow.Px(60);
                if (!animating) target = -AutoScrollPosition.Y;
                target = Math.Max(0, Math.Min(target + step, MaxScroll));
                animating = true;
                anim.Start();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) anim.Dispose();
            base.Dispose(disposing);
        }
    }

    // A rounded text field (§8.7): a borderless TextBox inside a painted 8 px-radius frame that
    // turns blue while focused.
    sealed class TextField : Control
    {
        public readonly TextBox Box;

        public TextField(string text, bool secret, bool multiline = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Box = new TextBox
            {
                Text = text ?? "", UseSystemPasswordChar = secret, BorderStyle = BorderStyle.None, Multiline = multiline,
                BackColor = Theme.Field, ForeColor = Theme.Label, Font = Theme.Body
            };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
            Height = multiline ? WidgetWindow.Px(96) : Box.PreferredHeight + WidgetWindow.Px(14);
            Cursor = Cursors.IBeam;
            // Tab goes straight to the text box; the frame itself never takes focus.
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int px = WidgetWindow.Px(10), py = Box.Multiline ? WidgetWindow.Px(8) : (Height - Box.PreferredHeight) / 2;
            Box.SetBounds(px, py, Width - px * 2, Box.Multiline ? Height - py * 2 : Box.PreferredHeight);
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float radius = WidgetWindow.Px(Theme.RadiusSmall);
            Theme.FillRound(g, Theme.Field, r, radius);
            using (var path = Theme.RoundRect(r, radius))
            using (var pen = new Pen(Box.Focused ? Theme.Link : Theme.FieldBorder, Box.Focused ? 2f : 1f))
                g.DrawPath(pen, path);
        }
    }
}
