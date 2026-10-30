using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskWidgets
{
    // The widget library: a card per widget type. Drag a card onto the desktop (or double-click
    // it) to add that widget. Only exists while open, so it costs nothing the rest of the time.
    sealed class LibraryWindow : Form
    {
        static readonly Color Bg = Color.FromArgb(28, 28, 30);
        readonly FlowLayoutPanel grid;

        public LibraryWindow()
        {
            Text = "Widget Library";
            Icon = App.AppIcon;
            BackColor = Bg;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(WidgetWindow.Px(620), WidgetWindow.Px(480));
            MinimumSize = new Size(WidgetWindow.Px(260), WidgetWindow.Px(300));

            grid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(WidgetWindow.Px(14), 0, WidgetWindow.Px(14), WidgetWindow.Px(14)),
                BackColor = Bg
            };
            foreach (var type in WidgetRegistry.Types) grid.Controls.Add(new WidgetCard(type));

            var header = new Panel { Dock = DockStyle.Top, Height = WidgetWindow.Px(78), BackColor = Bg };
            header.Controls.Add(new Label
            {
                Text = "Drag a widget onto your desktop, or double-click to add it.",
                ForeColor = Color.FromArgb(152, 152, 158), AutoSize = true,
                Location = new Point(WidgetWindow.Px(22), WidgetWindow.Px(48))
            });
            header.Controls.Add(new Label
            {
                Text = "Widget Library", Font = new Font("Segoe UI Semibold", 16f), AutoSize = true,
                Location = new Point(WidgetWindow.Px(18), WidgetWindow.Px(12))
            });

            Controls.Add(grid);
            Controls.Add(header);
            App.Current.WidgetsChanged += OnWidgetsChanged;
        }

        void OnWidgetsChanged()
        {
            foreach (Control c in grid.Controls) c.Invalidate();
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
            Size = new Size(WidgetWindow.Px(176), WidgetWindow.Px(240));
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
            using (var path = RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), WidgetWindow.Px(14)))
            using (var b = new SolidBrush(hover ? Color.FromArgb(58, 58, 60) : Color.FromArgb(44, 44, 46)))
                g.FillPath(b, path);

            int box = Width - pad * 2;
            float scale = Math.Min(1f, box / (float)Math.Max(preview.Width, preview.Height));
            int pw = (int)(preview.Width * scale), ph = (int)(preview.Height * scale);
            g.DrawImage(preview, new Rectangle((Width - pw) / 2, pad + (box - ph) / 2, pw, ph));

            int textTop = pad + box + WidgetWindow.Px(8);
            using (var nameFont = new Font("Segoe UI Semibold", 9.5f))
                TextRenderer.DrawText(g, type.Name, nameFont, new Point(pad, textTop), Color.White);
            int lineW = Width - pad * 2;
            TextRenderer.DrawText(g, type.Description, Font, new Rectangle(pad, textTop + WidgetWindow.Px(21), lineW, WidgetWindow.Px(18)),
                Color.FromArgb(152, 152, 158), TextFormatFlags.EndEllipsis);
            int count = App.Current.CountOf(type.Id);
            if (count > 0)
                TextRenderer.DrawText(g, count + " on desktop", Font, new Rectangle(pad, textTop + WidgetWindow.Px(39), lineW, WidgetWindow.Px(18)),
                    Color.FromArgb(255, 159, 10), TextFormatFlags.EndEllipsis);
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
            if (!dragging) return;
            EndDrag();
            // Dropped outside the library window = place it there.
            if (!FindForm().Bounds.Contains(Cursor.Position)) App.Current.AddWidget(type, Cursor.Position);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (dragging && !Capture) EndDrag();
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

        static GraphicsPath RoundRect(Rectangle r, int radius)
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
