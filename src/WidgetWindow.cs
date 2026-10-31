using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskWidgets
{
    // Base class for every widget: a borderless, per-pixel-alpha window that sits on the
    // desktop (below normal windows, survives Win+D, hidden from taskbar/Alt+Tab).
    // Subclasses only implement Measure(), Draw() and optionally NextFrameDelay()/BuildMenu().
    // Public: community widgets derive from it (API v1, see PluginApi.cs).
    public abstract class WidgetWindow : Form
    {
        static float dpi = 1f;
        public static float Dpi { get { return dpi; } internal set { dpi = value; } }
        public static int Px(float logical) { return (int)Math.Round(logical * Dpi); }

        public readonly Section Settings;
        readonly Timer timer = new Timer();
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
        }

        // ---- to implement ----
        protected abstract Size Measure();
        protected abstract void Draw(Graphics g);
        // ms until the next frame is needed, or -1 to only redraw on demand.
        protected virtual int NextFrameDelay() { return -1; }
        protected virtual void BuildMenu(PopupMenu menu) { }

        void ShowMenu()
        {
            var menu = new PopupMenu();
            BuildMenu(menu);
            menu.AddSeparator();
            menu.AddCheck("Lock Position", Locked, delegate { Settings.Set("locked", !Locked); });
            var opacity = menu.AddSubmenu("Opacity");
            foreach (int pct in new[] { 100, 85, 70, 50 })
            {
                int p = pct;
                opacity.AddChoice(p + "%", OpacityPercent == p, delegate { Settings.Set("opacity", p); Render(); });
            }
            menu.AddSeparator();
            menu.Add("Open Desk Widgets", App.Current.OpenLibrary);
            menu.AddSeparator();
            menu.AddDestructive("Remove Widget", delegate { App.Current.RemoveWidget(this); });
            menu.Show(Cursor.Position);
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

        internal void SetPaused(bool value)
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
            OnStart();
            Render();
        }

        // True when other windows hide (almost) all of this widget, so animations can stop drawing
        // frames nobody sees. Widgets sit at the bottom of the z-order, so every visible app window
        // is in front: subtract each one's area from ours and see what's left. Checked at most
        // twice a second.
        static readonly uint MyPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        int coveredCheckedAt;
        bool coveredCached;

        protected bool IsCovered()
        {
            int now = Environment.TickCount;
            if (coveredCheckedAt != 0 && now - coveredCheckedAt < 500) return coveredCached;
            coveredCheckedAt = now == 0 ? 1 : now;
            coveredCached = ComputeCovered();
            return coveredCached;
        }

        bool ComputeCovered()
        {
            var mine = new Rectangle(Location, Size);
            if (mine.Width <= 0 || mine.Height <= 0) return false;
            uint myPid = MyPid;
            var className = new System.Text.StringBuilder(64);
            using (var visible = new Region(mine))
            {
                Native.EnumWindows((hwnd, l) =>
                {
                    if (hwnd == Handle || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
                    int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                    // Click-through and never-activated windows are overlays (GeForce, Narrator, OSDs),
                    // often invisible and full-screen; real app windows are neither.
                    if ((ex & (Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE)) != 0) return true;
                    uint pid;
                    Native.GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == myPid && (ex & Native.WS_EX_LAYERED) != 0) return true; // other widgets
                    int cloaked;
                    if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return true; // other virtual desktop, suspended app
                    className.Length = 0;
                    Native.GetClassName(hwnd, className, className.Capacity);
                    string cls = className.ToString();
                    if (cls == "Progman" || cls == "WorkerW") return true; // the desktop itself
                    Native.RECT r;
                    if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, 16) != 0 &&
                        !Native.GetWindowRect(hwnd, out r)) return true;
                    var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    if (rect.IntersectsWith(mine)) visible.Exclude(rect);
                    return true;
                }, IntPtr.Zero);

                // Covered when less than 5% of the widget is still showing.
                long area = 0;
                using (var m = new System.Drawing.Drawing2D.Matrix())
                    foreach (var s in visible.GetRegionScans(m)) area += (long)(s.Width * s.Height);
                return area < (long)mine.Width * mine.Height / 20;
            }
        }

        // Colour of the last frame at a point (for overlays that must blend in, like the note editor).
        protected Color RenderedColor(Point p)
        {
            if (surface == null || p.X < 0 || p.Y < 0 || p.X >= surface.Width || p.Y >= surface.Height) return Color.Black;
            return Color.FromArgb(255, surface.Bitmap.GetPixel(p.X, p.Y));
        }

        // The wallpaper or display layout changed (only vibrant widgets care).
        public virtual void BackdropChanged() { }

        // Called once the widget is really on screen (not for library previews): start timers,
        // background fetches, etc. here rather than in the constructor.
        protected virtual void OnStart() { }

        public Size WidgetSize { get { return Measure(); } }

        // Positions are stored as fractions (0..1) of the monitor's free space, so a widget in
        // the top-right corner stays there on any resolution, DPI or machine.
        internal void MoveTo(Point topLeft)
        {
            Size sz = Measure();
            Screen screen = Screen.FromRectangle(new Rectangle(topLeft, sz));
            Rectangle wa = screen.WorkingArea;
            Settings.Set("screen", screen.DeviceName);
            Settings.Set("fx", Fraction(topLeft.X - wa.Left, wa.Width - sz.Width));
            Settings.Set("fy", Fraction(topLeft.Y - wa.Top, wa.Height - sz.Height));
            Reposition();
        }

        internal void Reposition()
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
        internal Bitmap Snapshot()
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
                // Pin to the bottom of the z-order: we live on the desktop, under apps. Only rewrite
                // real z-order changes; plain moves and resizes pass through untouched, so several
                // widgets never fight over the bottom slot.
                var wp = (Native.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                if ((wp.flags & Native.SWP_NOZORDER) == 0 && wp.hwndInsertAfter != Native.HWND_BOTTOM)
                {
                    wp.hwndInsertAfter = Native.HWND_BOTTOM;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(wp, m.LParam, false);
                }
            }
            else if (m.Msg == Native.WM_EXITSIZEMOVE)
            {
                MoveTo(Location);
                Render(); // vibrant widgets show what's behind their new spot
            }
            base.WndProc(ref m);
        }

        // ---- mouse: a press that doesn't move is a click, moving past the drag threshold drags ----
        bool pressed;
        Point pressedAt;

        // Points that act as buttons (shown with a hand cursor).
        protected virtual bool IsButton(Point p) { return false; }
        // A left click that didn't turn into a drag.
        protected virtual void OnWidgetClick(Point p) { }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = true;
            pressedAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor want = IsButton(e.Location) ? Cursors.Hand : Cursors.Default;
            if (Cursor != want) Cursor = want;
            // Only drag while the left button is really held: a press whose release we never saw
            // (a dialog opened, the release happened elsewhere) must not turn a later hover into a drag.
            if ((MouseButtons & MouseButtons.Left) == 0) pressed = false;
            if (!pressed || Locked || IsButton(pressedAt)) return;
            Size slop = SystemInformation.DragSize;
            if (Math.Abs(e.X - pressedAt.X) < slop.Width && Math.Abs(e.Y - pressedAt.Y) < slop.Height) return;
            pressed = false;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) ShowMenu();
            else if (e.Button == MouseButtons.Left && pressed)
            {
                pressed = false;
                OnWidgetClick(e.Location);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }
}
