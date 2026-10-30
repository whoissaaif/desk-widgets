using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskWidgets
{
    // The main app window: a category sidebar, and per category a banner plus a card per widget.
    // Drag a card onto the desktop (or double-click it) to add that widget. The window only exists
    // while open, so it costs nothing the rest of the time.
    sealed class LibraryWindow : Form
    {
        internal static readonly Color Bg = Color.FromArgb(28, 28, 30);
        internal static readonly Color SidebarBg = Color.FromArgb(36, 36, 38);
        internal static readonly Color Muted = Color.FromArgb(152, 152, 158);
        internal static readonly Color Accent = Color.FromArgb(255, 159, 10);

        sealed class CategorySection
        {
            public WidgetCategory Category;
            public CategoryBanner Banner;
            public List<WidgetCard> Cards = new List<WidgetCard>();
        }

        readonly Panel content;
        readonly Label hint;
        readonly List<CategorySection> sections = new List<CategorySection>();
        readonly List<SidebarItem> nav = new List<SidebarItem>();
        readonly SidebarItem startup;
        string filter; // category id, or null for all

        public LibraryWindow()
        {
            Text = "Desk Widgets";
            Icon = App.AppIcon;
            BackColor = Bg;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(880), Px(580));
            MinimumSize = new Size(Px(560), Px(400));

            content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg };
            content.HandleCreated += delegate { Native.UseDarkControlTheme(content.Handle); }; // dark scrollbar
            hint = new Label { Text = "Drag a widget onto your desktop, or double-click to add it.", ForeColor = Muted, AutoSize = true };
            content.Controls.Add(hint);
            foreach (var cat in WidgetRegistry.Categories)
            {
                var types = WidgetRegistry.InCategory(cat.Id);
                if (types.Length == 0) continue;
                var sec = new CategorySection { Category = cat, Banner = new CategoryBanner(cat, types) };
                content.Controls.Add(sec.Banner);
                foreach (var t in types)
                {
                    var card = new WidgetCard(t);
                    sec.Cards.Add(card);
                    content.Controls.Add(card);
                }
                sections.Add(sec);
            }
            content.Resize += delegate { LayoutContent(); };

            var sidebar = new Panel { Dock = DockStyle.Left, Width = Px(214), BackColor = SidebarBg };
            sidebar.Controls.Add(new Label
            {
                Text = "Desk Widgets", Font = new Font("Segoe UI Semibold", 14f), AutoSize = true,
                Location = new Point(Px(18), Px(18))
            });
            int y = Px(70);
            AddNav(sidebar, '', "All widgets", null, ref y);
            foreach (var sec in sections) AddNav(sidebar, sec.Category.Glyph, sec.Category.Name, sec.Category.Id, ref y);

            startup = new SidebarItem(App.StartWithWindows ? '' : '', "Start with Windows");
            startup.SetBounds(Px(8), sidebar.Height - Px(52), sidebar.Width - Px(16), Px(38));
            startup.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            startup.Click += delegate
            {
                App.StartWithWindows = !App.StartWithWindows;
                startup.Glyph = App.StartWithWindows ? '' : '';
                startup.Invalidate();
            };
            sidebar.Controls.Add(startup);

            Controls.Add(content);
            Controls.Add(sidebar);
            App.Current.WidgetsChanged += OnWidgetsChanged;
            SelectCategory(null);
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        void AddNav(Panel sidebar, char glyph, string text, string categoryId, ref int y)
        {
            var item = new SidebarItem(glyph, text) { Tag = categoryId };
            item.SetBounds(Px(8), y, sidebar.Width - Px(16), Px(38));
            item.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            item.Click += delegate { SelectCategory((string)item.Tag); };
            sidebar.Controls.Add(item);
            nav.Add(item);
            y += Px(42);
        }

        void SelectCategory(string categoryId)
        {
            filter = categoryId;
            foreach (var item in nav)
            {
                item.Selected = (string)item.Tag == categoryId;
                item.Invalidate();
            }
            content.AutoScrollPosition = Point.Empty;
            LayoutContent();
        }

        // Manual layout: banner, then a wrapping grid of cards, per visible category.
        void LayoutContent()
        {
            int pad = Px(24), gap = Px(14);
            int width = Math.Max(Px(200), content.ClientSize.Width - pad * 2);
            int scroll = content.AutoScrollPosition.Y;
            int y = Px(22);
            content.SuspendLayout();
            hint.Location = new Point(pad, y + scroll);
            y += hint.Height + Px(16);
            foreach (var sec in sections)
            {
                bool show = filter == null || sec.Category.Id == filter;
                sec.Banner.Visible = show;
                foreach (var card in sec.Cards) card.Visible = show;
                if (!show) continue;

                sec.Banner.SetBounds(pad, y + scroll, width, Px(68));
                y += Px(68) + gap;
                // Cards flow left to right and wrap; medium widgets have double-width cards.
                int x = pad, ch = sec.Cards[0].Height;
                foreach (var card in sec.Cards)
                {
                    if (x > pad && x + card.Width > pad + width) { x = pad; y += ch + gap; }
                    card.Location = new Point(x, y + scroll);
                    x += card.Width + gap;
                }
                y += ch + gap + Px(20);
            }
            content.AutoScrollMinSize = new Size(0, y);
            content.ResumeLayout();
        }

        void OnWidgetsChanged()
        {
            foreach (Control c in content.Controls) c.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1; // dark title bar (Windows 10 2004+ / 11; ignored elsewhere)
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            App.Current.WidgetsChanged -= OnWidgetsChanged;
            base.OnFormClosed(e);
        }

        internal static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Font iconFont;

        // Windows 11 ships "Segoe Fluent Icons", Windows 10 "Segoe MDL2 Assets" (same code points).
        internal static Font IconFont
        {
            get
            {
                if (iconFont == null)
                {
                    iconFont = new Font("Segoe Fluent Icons", 11f);
                    if (iconFont.Name != "Segoe Fluent Icons") { iconFont.Dispose(); iconFont = new Font("Segoe MDL2 Assets", 11f); }
                }
                return iconFont;
            }
        }
    }

    sealed class SidebarItem : Control
    {
        public char Glyph;
        public bool Selected;
        bool hover;

        public SidebarItem(char glyph, string text)
        {
            Glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent.BackColor);
            if (Selected || hover)
                using (var path = LibraryWindow.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), WidgetWindow.Px(6)))
                using (var b = new SolidBrush(Selected ? Color.FromArgb(52, 52, 55) : Color.FromArgb(44, 44, 47)))
                    g.FillPath(b, path);
            if (Selected)
                using (var path = LibraryWindow.RoundRect(new Rectangle(0, Height / 2 - WidgetWindow.Px(8), WidgetWindow.Px(3), WidgetWindow.Px(16)), WidgetWindow.Px(1)))
                using (var b = new SolidBrush(LibraryWindow.Accent))
                    g.FillPath(b, path);

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Glyph.ToString(), LibraryWindow.IconFont,
                new Rectangle(WidgetWindow.Px(14), 0, WidgetWindow.Px(24), Height), Color.White, flags);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(WidgetWindow.Px(44), 0, Width - WidgetWindow.Px(48), Height),
                Color.White, flags | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    sealed class CategoryBanner : Control
    {
        readonly WidgetCategory category;
        readonly WidgetType[] types;

        public CategoryBanner(WidgetCategory category, WidgetType[] types)
        {
            this.category = category;
            this.types = types;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent.BackColor);
            using (var path = LibraryWindow.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), WidgetWindow.Px(12)))
            using (var b = new LinearGradientBrush(ClientRectangle, Color.FromArgb(54, 46, 36), Color.FromArgb(40, 40, 42), LinearGradientMode.Horizontal))
                g.FillPath(b, path);

            int tile = Height - WidgetWindow.Px(24), tx = WidgetWindow.Px(12), ty = (Height - tile) / 2;
            using (var path = LibraryWindow.RoundRect(new Rectangle(tx, ty, tile, tile), WidgetWindow.Px(8)))
            using (var b = new SolidBrush(LibraryWindow.Accent))
                g.FillPath(b, path);
            using (var big = new Font(LibraryWindow.IconFont.FontFamily, 14f))
                TextRenderer.DrawText(g, category.Glyph.ToString(), big, new Rectangle(tx, ty, tile, tile), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int textX = tx + tile + WidgetWindow.Px(14);
            using (var title = new Font("Segoe UI Semibold", 13f))
                TextRenderer.DrawText(g, category.Name, title, new Point(textX, ty - WidgetWindow.Px(3)), Color.White);
            TextRenderer.DrawText(g, category.Description, Font, new Point(textX, ty + tile - WidgetWindow.Px(17)), LibraryWindow.Muted);

            int placed = 0;
            foreach (var t in types) placed += App.Current.CountOf(t.Id);
            string right = types.Length + (types.Length == 1 ? " widget" : " widgets") + (placed > 0 ? "  ·  " + placed + " on desktop" : "");
            TextRenderer.DrawText(g, right, Font, new Rectangle(0, 0, Width - WidgetWindow.Px(18), Height), LibraryWindow.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    sealed class WidgetCard : Control
    {
        readonly WidgetType type;
        readonly Bitmap preview;   // rendered at the real widget size, reused for the drag ghost
        bool hover, dragging;
        Point downAt;
        DragGhost ghost;

        public WidgetCard(WidgetType type)
        {
            this.type = type;
            preview = type.Snapshot();
            // Wide widgets (medium, 364 x 170) get a card two columns wide, with the same gap as the grid.
            bool wide = preview.Width > preview.Height * 1.5f;
            Size = new Size(WidgetWindow.Px(wide ? 176 * 2 + 14 : 176), WidgetWindow.Px(240));
            Margin = new Padding(WidgetWindow.Px(8));
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Parent.BackColor);

            int pad = WidgetWindow.Px(12);
            using (var path = LibraryWindow.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), WidgetWindow.Px(14)))
            using (var b = new SolidBrush(hover ? Color.FromArgb(58, 58, 60) : Color.FromArgb(44, 44, 46)))
                g.FillPath(b, path);

            int boxW = Width - pad * 2, box = WidgetWindow.Px(176) - pad * 2;
            float scale = Math.Min(1f, Math.Min(boxW / (float)preview.Width, box / (float)preview.Height));
            int pw = (int)(preview.Width * scale), ph = (int)(preview.Height * scale);
            g.DrawImage(preview, new Rectangle((Width - pw) / 2, pad + (box - ph) / 2, pw, ph));

            int textTop = pad + box + WidgetWindow.Px(8);
            using (var nameFont = new Font("Segoe UI Semibold", 9.5f))
                TextRenderer.DrawText(g, type.Name, nameFont, new Point(pad, textTop), Color.White);
            int lineW = Width - pad * 2;
            TextRenderer.DrawText(g, type.Description, Font, new Rectangle(pad, textTop + WidgetWindow.Px(21), lineW, WidgetWindow.Px(18)),
                LibraryWindow.Muted, TextFormatFlags.EndEllipsis);
            int count = App.Current.CountOf(type.Id);
            if (count > 0)
                TextRenderer.DrawText(g, count + " on desktop", Font, new Rectangle(pad, textTop + WidgetWindow.Px(39), lineW, WidgetWindow.Px(18)),
                    LibraryWindow.Accent, TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) downAt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left) return;
            if (!dragging)
            {
                Size slop = SystemInformation.DragSize;
                if (Math.Abs(e.X - downAt.X) < slop.Width && Math.Abs(e.Y - downAt.Y) < slop.Height) return;
                dragging = true;
                ghost = new DragGhost(preview);
                ghost.CenterOn(Cursor.Position);
                ghost.Show();
                return;
            }
            ghost.CenterOn(Cursor.Position);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Drop();
        }

        // WinForms may release capture before or after MouseUp, so both paths finish the drag.
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) Drop();
        }

        void Drop()
        {
            if (!dragging) return;
            EndDrag();
            // Dropped outside the library window = place it there.
            Point at = Cursor.Position;
            if (!FindForm().Bounds.Contains(at)) App.Current.AddWidget(type, at);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            App.Current.AddWidget(type, null);
        }

        void EndDrag()
        {
            dragging = false;
            if (ghost != null) { ghost.Dispose(); ghost = null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                EndDrag();
                preview.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Semi-transparent, click-through, topmost copy of the widget that follows the cursor while dragging.
    sealed class DragGhost : Form
    {
        readonly LayeredSurface surface;

        public DragGhost(Bitmap image)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            surface = new LayeredSurface(image.Width, image.Height);
            surface.Graphics.CompositingMode = CompositingMode.SourceCopy;
            surface.Graphics.DrawImageUnscaled(image, 0, 0);
            Size = image.Size;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE |
                              Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            surface.Present(Handle, 190);
        }

        public void CenterOn(Point p)
        {
            Location = new Point(p.X - Width / 2, p.Y - Height / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) surface.Dispose();
            base.Dispose(disposing);
        }
    }
}
