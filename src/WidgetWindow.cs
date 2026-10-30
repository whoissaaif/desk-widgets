using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskWidgets
{
    // Base class for every widget: a borderless, per-pixel-alpha window that sits on the
    // desktop (below normal windows, survives Win+D, hidden from taskbar/Alt+Tab).
    // Subclasses only implement Measure(), Draw() and optionally NextFrameDelay()/BuildMenu().
    abstract class WidgetWindow : Form
    {
        public static float Dpi = 1f;
        public static int Px(float logical) { return (int)Math.Round(logical * Dpi); }

        public readonly Section Settings;
        readonly Timer timer = new Timer();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        LayeredSurface surface;
        bool paused;

        protected WidgetWindow(Section settings)
        {
            Settings = settings;
            Text = "Desk Widget";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            timer.Tick += delegate { timer.Stop(); Render(); };
            menu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                menu.Items.Clear();
                BuildMenu(menu.Items);
                if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(Choice("Lock position", Locked, delegate { Settings.Set("locked", !Locked); }));
                var opacity = new ToolStripMenuItem("Opacity");
                foreach (int pct in new[] { 100, 85, 70, 50 })
                {
                    int p = pct;
                    opacity.DropDownItems.Add(Choice(p + "%", OpacityPercent == p, delegate { Settings.Set("opacity", p); Render(); }));
                }
                menu.Items.Add(opacity);
                menu.Items.Add("Remove this widget", null, delegate { App.Current.RemoveWidget(this); });
                menu.Items.Add(new ToolStripSeparator());
                App.Current.FillAppMenu(menu.Items);
                e.Cancel = false;
            };
        }

        // ---- to implement ----
        protected abstract Size Measure();
        protected abstract void Draw(Graphics g);
        // ms until the next frame is needed, or -1 to only redraw on demand.
        protected virtual int NextFrameDelay() { return -1; }
        protected virtual void BuildMenu(ToolStripItemCollection items) { }

        // ---- helpers for subclasses ----
        protected static ToolStripMenuItem Choice(string text, bool isChecked, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            item.Checked = isChecked;
            return item;
        }

        protected bool Locked { get { return Settings.GetBool("locked", false); } }
        int OpacityPercent { get { return Math.Max(10, Math.Min(100, Settings.GetInt("opacity", 100))); } }

        public void Render()
        {
            if (!IsHandleCreated || IsDisposed || paused) return;
            Size sz = Measure();
            if (surface == null || surface.Width != sz.Width || surface.Height != sz.Height)
            {
                if (surface != null) surface.Dispose();
                surface = new LayeredSurface(sz.Width, sz.Height);
                Size = sz;
                PlaceOnScreen(); // keep the same relative anchor when the size changes
            }
            Draw(surface.Graphics);
            surface.Present(Handle, (byte)(OpacityPercent * 255 / 100));

            int delay = NextFrameDelay();
            if (delay >= 0)
            {
                timer.Interval = Math.Max(1, delay);
                timer.Start();
            }
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (paused) timer.Stop();
            else Render();
        }

        // ---- window plumbing ----
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Owned by the desktop window so "Show desktop" (Win+D) doesn't hide us.
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman != IntPtr.Zero) Native.SetOwner(Handle, progman);
            PlaceOnScreen();
            Render();
        }

        public Size WidgetSize { get { return Measure(); } }

        // Positions are stored as fractions (0..1) of the monitor's free space, so a widget in
        // the top-right corner stays there on any resolution, DPI or machine.
        public void MoveTo(Point topLeft)
        {
            Size sz = Measure();
            Screen screen = Screen.FromRectangle(new Rectangle(topLeft, sz));
            Rectangle wa = screen.WorkingArea;
            Settings.Set("screen", screen.DeviceName);
            Settings.Set("fx", Fraction(topLeft.X - wa.Left, wa.Width - sz.Width));
            Settings.Set("fy", Fraction(topLeft.Y - wa.Top, wa.Height - sz.Height));
            Reposition();
        }

        public void Reposition()
        {
            if (IsHandleCreated) PlaceOnScreen();
        }

        static float Fraction(int offset, int range)
        {
            return range <= 0 ? 0f : Math.Max(0f, Math.Min(1f, offset / (float)range));
        }

        void PlaceOnScreen()
        {
            Size sz = Measure();
            float fx = Settings.GetFloat("fx", float.NaN), fy = Settings.GetFloat("fy", float.NaN);
            if (float.IsNaN(fx) || float.IsNaN(fy))
            {
                int x = Settings.GetInt("x", int.MinValue), y = Settings.GetInt("y", int.MinValue);
                Settings.Remove("x");
                Settings.Remove("y");
                var wa0 = Screen.PrimaryScreen.WorkingArea;
                MoveTo(x != int.MinValue ? new Point(x, y) : new Point(wa0.Right - sz.Width - Px(40), wa0.Top + Px(40)));
                return;
            }
            string device = Settings.Get("screen", "");
            Screen screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            Location = new Point(wa.Left + (int)Math.Round(fx * (wa.Width - sz.Width)),
                                 wa.Top + (int)Math.Round(fy * (wa.Height - sz.Height)));
        }

        // Renders the widget into a new bitmap (used for library previews and the drag ghost).
        public Bitmap Snapshot()
        {
            Size sz = Measure();
            var bmp = new Bitmap(sz.Width, sz.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Draw(g);
            }
            return bmp;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_WINDOWPOSCHANGING)
            {
                // Pin to the bottom of the z-order: we live on the desktop, under apps.
                var wp = (Native.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
                wp.flags &= ~Native.SWP_NOZORDER;
                System.Runtime.InteropServices.Marshal.StructureToPtr(wp, m.LParam, false);
            }
            else if (m.Msg == Native.WM_EXITSIZEMOVE)
            {
                MoveTo(Location);
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !Locked)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                menu.Dispose();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }
}
