// A minimal community widget. Install it from Desk Widgets: Installed > Install from File…
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using DeskWidgets;

[DeskWidget(Name = "Hello", Description = "The smallest community widget")]
public sealed class HelloWidget : WidgetWindow
{
    public HelloWidget(Section s) : base(s) { }

    protected override Size Measure() { return new Size(Px(170), Px(170)); }

    // Redraw once a minute for the clock line.
    protected override int NextFrameDelay() { return 60000 - DateTime.Now.Second * 1000; }

    protected override void Draw(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        int r = Px(22), w = Px(170);
        using (var path = new GraphicsPath())
        {
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(w - r * 2 - 1, 0, r * 2, r * 2, 270, 90);
            path.AddArc(w - r * 2 - 1, w - r * 2 - 1, r * 2, r * 2, 0, 90);
            path.AddArc(0, w - r * 2 - 1, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using (var b = new SolidBrush(Color.FromArgb(28, 28, 30))) g.FillPath(b, path);
        }
        using (var f = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel))
            g.DrawString("Hello", f, Brushes.White, Px(16), Px(16));
        using (var f = new Font("Segoe UI", 13f, GraphicsUnit.Pixel))
            g.DrawString(DateTime.Now.ToString("HH:mm"), f, Brushes.Gray, Px(16), Px(44));
    }

    protected override void BuildMenu(PopupMenu menu)
    {
        menu.Add("Say Hi", delegate { System.Windows.Forms.MessageBox.Show("Hi from a plugin"); });
    }
}
