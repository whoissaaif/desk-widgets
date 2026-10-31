using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace DeskWidgets
{
    // A right-click menu in the Apple style (design/apple-design.md §17): a rounded, near-opaque
    // panel with a soft shadow, a blue highlight, checkmarks on the left, a chevron for submenus and
    // destructive items in red. Built on demand, destroyed after use.
    public sealed class PopupMenu
    {
        internal sealed class Item
        {
            public string Text;
            public Action OnClick;
            public bool Checked, Destructive, Separator;
            public PopupMenu Submenu;
        }

        internal readonly List<Item> Items = new List<Item>();

        public int Count { get { return Items.Count; } }

        public void Add(string text, Action onClick)
        {
            Items.Add(new Item { Text = text, OnClick = onClick });
        }

        public void AddCheck(string text, bool isChecked, Action onClick)
        {
            Items.Add(new Item { Text = text, OnClick = onClick, Checked = isChecked });
        }

        // One of several mutually exclusive choices. Like Apple's menus, the chosen one gets a checkmark.
        public void AddChoice(string text, bool isSelected, Action onClick)
        {
            AddCheck(text, isSelected, onClick);
        }

        // Removes or deletes something: shown in red. Put it last in the menu.
        public void AddDestructive(string text, Action onClick)
        {
            Items.Add(new Item { Text = text, OnClick = onClick, Destructive = true });
        }

        public void AddSeparator()
        {
            if (Items.Count > 0 && !Items[Items.Count - 1].Separator) Items.Add(new Item { Separator = true });
        }

        // Keep submenus to one level (HIG).
        public PopupMenu AddSubmenu(string text)
        {
            var sub = new PopupMenu();
            Items.Add(new Item { Text = text, Submenu = sub });
            return sub;
        }

        // Opens at a screen point; returns right away and runs the chosen item's action after closing.
        public void Show(Point screen)
        {
            MenuSession.Open(this, screen);
        }
    }

    // One open menu and its submenus. The root window takes focus (keyboard, closes when another app
    // is clicked); submenus never do. Clicks on our own focus-less windows (widgets) are caught by a
    // message filter.
    sealed class MenuSession : IMessageFilter
    {
        static MenuSession current;

        readonly List<MenuLevel> levels = new List<MenuLevel>();
        readonly IntPtr previous; // our window that was active before, kept looking active
        readonly Timer hoverTimer = new Timer { Interval = 200 };
        Action pendingHover;
        MenuLevel pendingLevel; // the menu whose hover change is pending
        Point lastPointer = Cursor.Position; // Windows repeats a move when a window appears under a still pointer
        bool closed;

        public static void Open(PopupMenu menu, Point screen)
        {
            if (current != null) current.Close(false);
            current = new MenuSession(menu, screen);
        }

        MenuSession(PopupMenu menu, Point screen)
        {
            IntPtr fg = Native.GetForegroundWindow();
            previous = IsOurs(fg) ? fg : IntPtr.Zero;
            hoverTimer.Tick += delegate
            {
                Action run = pendingHover;
                CancelHover();
                if (run != null) run();
            };
            Application.AddMessageFilter(this);

            var root = new MenuLevel(this, menu, true);
            levels.Add(root);
            root.PlaceAt(screen);
            root.Show();
            Native.SetForegroundWindow(root.Handle);
            root.Activate();
            if (previous != IntPtr.Zero) Native.SendMessage(previous, Native.WM_NCACTIVATE, (IntPtr)1, IntPtr.Zero);
        }

        static bool IsOurs(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }

        MenuLevel Deepest { get { return levels[levels.Count - 1]; } }

        // ---- mouse ----

        public void OnMouseMove(Point screen)
        {
            if (screen == lastPointer) return; // not the hand moving: a window appeared or closed under it
            lastPointer = screen;
            MenuLevel level = levels.LastOrDefault(l => l.Bounds.Contains(screen) && l.BodyContains(screen));
            if (level == null)
            {
                if (Deepest.Child == null) Deepest.SetHighlight(-1);
                return;
            }
            // Inside a submenu: its parent row stays highlighted, even if the pointer cut across another row,
            // and whatever the parent had pending (closing this submenu) is off.
            int at = levels.IndexOf(level);
            if (at > 0 && levels[at - 1].ChildIndex >= 0) levels[at - 1].SetHighlight(levels[at - 1].ChildIndex);
            if (pendingLevel != null && levels.IndexOf(pendingLevel) < at) CancelHover();
            int index = level.HitTest(screen);
            // Still on the same row: a real mouse sends many moves per row, so a pending open must survive them.
            if (index == level.Highlight) return;
            level.SetHighlight(index);
            CancelHover();
            int i = index;
            if (level.Child != null && level.ChildIndex != i)
                Later(level, delegate { CloseAfter(level); if (i >= 0 && level.Items[i].Submenu != null) OpenSubmenu(level, i, false); });
            else if (level.Child == null && i >= 0 && level.Items[i].Submenu != null)
                Later(level, delegate { OpenSubmenu(level, i, false); });
        }

        public void OnMouseUp(MenuLevel level, Point screen)
        {
            int index = level.HitTest(screen);
            if (index < 0) return;
            var item = level.Items[index];
            if (item.Submenu != null)
            {
                CancelHover();
                if (level.ChildIndex != index) { CloseAfter(level); OpenSubmenu(level, index, false); }
            }
            else Choose(item);
        }

        // Hover changes wait 200 ms, so passing over rows on the way somewhere doesn't flicker submenus.
        void Later(MenuLevel level, Action action)
        {
            pendingLevel = level;
            pendingHover = action;
            hoverTimer.Start();
        }

        void CancelHover()
        {
            hoverTimer.Stop();
            pendingHover = null;
            pendingLevel = null;
        }

        // Clicks on any of our other windows (widgets never take focus) close the menu and are eaten.
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207,
                      WM_NCLBUTTONDOWN = 0xA1, WM_NCRBUTTONDOWN = 0xA4;
            if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_RBUTTONDOWN && m.Msg != WM_MBUTTONDOWN &&
                m.Msg != WM_NCLBUTTONDOWN && m.Msg != WM_NCRBUTTONDOWN) return false;
            IntPtr hwnd = m.HWnd;
            if (levels.Any(l => l.Handle == hwnd)) return false;
            Close(true);
            return true;
        }

        // ---- keyboard (root window has focus) ----

        public bool OnKey(Keys key)
        {
            MenuLevel level = Deepest;
            switch (key)
            {
                case Keys.Escape: Close(true); return true;
                case Keys.Up: level.MoveHighlight(-1); return true;
                case Keys.Down: level.MoveHighlight(1); return true;
                case Keys.Right:
                    if (level.Highlight >= 0 && level.Items[level.Highlight].Submenu != null) OpenSubmenu(level, level.Highlight, true);
                    return true;
                case Keys.Left:
                    if (levels.Count > 1) CloseAfter(levels[levels.Count - 2]);
                    return true;
                case Keys.Enter:
                case Keys.Space:
                    if (level.Highlight < 0) return true;
                    var item = level.Items[level.Highlight];
                    if (item.Submenu != null) OpenSubmenu(level, level.Highlight, true);
                    else Choose(item);
                    return true;
            }
            return false;
        }

        // ---- levels ----

        void OpenSubmenu(MenuLevel parent, int index, bool highlightFirst)
        {
            if (closed || !levels.Contains(parent)) return;
            CloseAfter(parent);
            var sub = new MenuLevel(this, parent.Items[index].Submenu, false);
            parent.Child = sub;
            parent.ChildIndex = index;
            parent.SetHighlight(index);
            levels.Add(sub);
            sub.PlaceBeside(parent, index);
            sub.Show();
            if (highlightFirst) sub.MoveHighlight(1);
        }

        void CloseAfter(MenuLevel parent)
        {
            int at = levels.IndexOf(parent);
            if (at < 0) return;
            for (int i = levels.Count - 1; i > at; i--)
            {
                levels[i].Dispose();
                levels.RemoveAt(i);
            }
            parent.Child = null;
            parent.ChildIndex = -1;
        }

        void Choose(PopupMenu.Item item)
        {
            if (item.OnClick == null) return;
            Close(true);
            Ui.Later(item.OnClick); // after the menu windows are gone
        }

        // refocus: give focus back to the window that had it (not when another app was clicked).
        public void Close(bool refocus)
        {
            if (closed) return;
            closed = true;
            if (current == this) current = null;
            Application.RemoveMessageFilter(this);
            hoverTimer.Dispose();
            var all = levels.ToArray();
            levels.Clear();
            foreach (var l in all) l.Hide();
            Ui.Later(delegate { foreach (var l in all) l.Dispose(); }); // never inside their own messages
            if (refocus && previous != IntPtr.Zero && Native.IsWindowVisible(previous)) Native.SetForegroundWindow(previous);
            else if (previous != IntPtr.Zero) Native.SendMessage(previous, Native.WM_NCACTIVATE, IntPtr.Zero, IntPtr.Zero);
        }

        public bool IsClosed { get { return closed; } }

        public bool Owns(IntPtr hwnd) { return !closed && levels.Any(l => l.Handle == hwnd); }
    }

    // One panel of a menu: a layered window drawn in full (shadow included) on every change.
    sealed class MenuLevel : Form
    {
        // Metrics in px at 100 % (§17): 5 padding, 24 rows, 11 separators, 10 corners, 6 highlights.
        static int Px(float v) { return WidgetWindow.Px(v); }
        static readonly int Shadow = Px(14), Pad = Px(5), Row = Px(24), SepRow = Px(11),
                            TextLeft = Px(26), TextRight = Px(16), ChevronW = Px(18), MinWidth = Px(180);

        readonly MenuSession session;
        readonly bool root;
        public readonly List<PopupMenu.Item> Items;
        readonly List<Rectangle> rows = new List<Rectangle>(); // body coordinates
        readonly Size body;
        LayeredSurface surface;
        public int Highlight = -1;
        public MenuLevel Child;
        public int ChildIndex = -1;

        public MenuLevel(MenuSession session, PopupMenu menu, bool root)
        {
            this.session = session;
            this.root = root;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            // No separators at the ends.
            Items = menu.Items.SkipWhile(i => i.Separator).ToList();
            while (Items.Count > 0 && Items[Items.Count - 1].Separator) Items.RemoveAt(Items.Count - 1);

            int width = MinWidth, y = Pad;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
                foreach (var item in Items)
                {
                    int h = item.Separator ? SepRow : Row;
                    rows.Add(new Rectangle(0, y, 0, h));
                    y += h;
                    if (item.Separator) continue;
                    int w = TextLeft + (int)Math.Ceiling(g.MeasureString(item.Text, Theme.Menu, PointF.Empty, StringFormat.GenericTypographic).Width)
                            + TextRight + (item.Submenu != null ? ChevronW : 0);
                    width = Math.Max(width, w);
                }
            body = new Size(width, y + Pad);
            for (int i = 0; i < rows.Count; i++) rows[i] = new Rectangle(0, rows[i].Y, width, rows[i].Height);
            Size = new Size(body.Width + Shadow * 2, body.Height + Shadow * 2);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                if (!root) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return !root; } }

        Rectangle BodyOnScreen { get { return new Rectangle(Left + Shadow, Top + Shadow, body.Width, body.Height); } }

        public bool BodyContains(Point screen) { return BodyOnScreen.Contains(screen); }

        // Below and right of the pointer; flipped to stay on the screen.
        public void PlaceAt(Point p)
        {
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = p.X, y = p.Y;
            if (x + body.Width > wa.Right) x = p.X - body.Width;
            if (y + body.Height > wa.Bottom) y = p.Y - body.Height;
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - body.Width));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - body.Height));
            Location = new Point(x - Shadow, y - Shadow);
        }

        // To the right of the parent's item (left if there's no room), rows lined up.
        public void PlaceBeside(MenuLevel parent, int index)
        {
            Rectangle pb = parent.BodyOnScreen;
            Rectangle wa = Screen.FromRectangle(pb).WorkingArea;
            int x = pb.Right - Px(2);
            if (x + body.Width > wa.Right) x = pb.Left - body.Width + Px(2);
            int y = pb.Top + parent.rows[index].Top - Pad;
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - body.Height));
            Location = new Point(x - Shadow, y - Shadow);
        }

        public int HitTest(Point screen)
        {
            Rectangle b = BodyOnScreen;
            if (!b.Contains(screen)) return -1;
            int y = screen.Y - b.Top;
            for (int i = 0; i < rows.Count; i++)
                if (y >= rows[i].Top && y < rows[i].Bottom) return Items[i].Separator ? -1 : i;
            return -1;
        }

        public void SetHighlight(int index)
        {
            if (index == Highlight) return;
            Highlight = index;
            Render();
        }

        public void MoveHighlight(int step)
        {
            if (Items.Count == 0) return;
            int i = Highlight;
            for (int n = 0; n < Items.Count; n++)
            {
                i = i < 0 ? (step > 0 ? 0 : Items.Count - 1) : (i + step + Items.Count) % Items.Count;
                if (!Items[i].Separator) { SetHighlight(i); return; }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Render();
        }

        void Render()
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (surface == null) surface = new LayeredSurface(Width, Height);
            Graphics g = surface.Graphics;
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var b = new RectangleF(Shadow, Shadow, body.Width, body.Height);
            float radius = Px(Theme.RadiusMenu);
            // Soft shadow, a little below (§7.1: rare and soft).
            for (int i = Shadow; i >= 1; i--)
            {
                float t = 1f - i / (float)(Shadow + 1);
                var r = b;
                r.Inflate(i * 0.8f, i * 0.8f);
                r.Offset(0, Px(3));
                Theme.FillRound(g, Color.FromArgb((int)(Theme.Dark ? 14 * t * t : 9 * t * t), 0, 0, 0), r, radius + i);
            }
            using (var path = Theme.RoundRect(b, radius))
            {
                using (var fill = new SolidBrush(Theme.MenuBg)) g.FillPath(fill, path);
                using (var edge = new Pen(Theme.MenuEdge, 1f)) g.DrawPath(edge, path);
            }

            using (var check = new Font(Theme.Icons.FontFamily, 9f))
            using (var chevron = new Font(Theme.Icons.FontFamily, 7f))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                for (int i = 0; i < Items.Count; i++)
                {
                    var item = Items[i];
                    var row = rows[i];
                    row.Offset(Shadow, Shadow);
                    if (item.Separator)
                    {
                        float y = row.Top + row.Height / 2f;
                        using (var pen = new Pen(Theme.Separator, 1f)) g.DrawLine(pen, row.Left + Px(10), y, row.Right - Px(10), y);
                        continue;
                    }
                    bool hot = i == Highlight;
                    if (hot) Theme.FillRound(g, Theme.PrimaryFill, new RectangleF(row.Left + Pad, row.Top, row.Width - Pad * 2, row.Height), Px(Theme.RadiusItem));
                    Color fg = hot ? Theme.OnPrimary : item.Destructive ? Theme.Destructive : Theme.Label;
                    using (var brush = new SolidBrush(fg))
                    {
                        if (item.Checked)
                            g.DrawString("", check, brush, new RectangleF(row.Left + Px(9), row.Top, Px(16), row.Height), fmt);
                        var text = new RectangleF(row.Left + TextLeft, row.Top, row.Width - TextLeft - TextRight - (item.Submenu != null ? ChevronW : 0), row.Height);
                        g.DrawString(item.Text, Theme.Menu, brush, text, fmt);
                        if (item.Submenu != null)
                            g.DrawString("", chevron, brush, new RectangleF(row.Right - TextRight - Px(6), row.Top, Px(14), row.Height), fmt);
                    }
                }
            surface.Present(Handle, 255);
        }

        // ---- input goes to the session ----

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            session.OnMouseMove(PointToScreen(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!session.IsClosed) session.OnMouseMove(Cursor.Position);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = PointToScreen(e.Location);
            if (BodyContains(p)) session.OnMouseUp(this, p);
            else session.Close(true); // released on the shadow, i.e. outside
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return session.OnKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Another app or the desktop was clicked. Checked once activation has settled, so a click
            // that ends up in one of our own submenus never closes the menu.
            if (root) Ui.Later(delegate { if (!session.Owns(Native.GetForegroundWindow())) session.Close(false); });
        }

        // Clicking a submenu must not take activation from the root (WinForms' Form would allow it).
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE && !root) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }
    }
}
