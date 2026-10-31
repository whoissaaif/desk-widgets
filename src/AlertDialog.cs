using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DeskWidgets
{
    // Alerts in the Apple layout (design/apple-design.md §17): app icon, a bold title, the message,
    // optional facts as a two-column list and a "Show Source" link, then capsule buttons side by side
    // with Cancel on the left. The safe choice is the default.
    sealed class AlertDialog : Form
    {
        int result = -1;

        AlertDialog(string title, string message, IList<KeyValuePair<string, string>> facts, string linkText, Action link,
                    string[] buttons, ButtonStyle[] styles, int defaultIndex, int cancelIndex)
        {
            Text = "Desk Widgets";
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.Label;
            Font = Theme.Footnote;

            int pad = Px(20), width = Px(340), inner = width - pad * 2, y = pad;
            var icon = new PictureBox { Image = IconImage(Px(48)), SizeMode = PictureBoxSizeMode.Zoom };
            icon.SetBounds((width - Px(48)) / 2, y, Px(48), Px(48));
            Controls.Add(icon);
            y += Px(48) + Px(12);

            y = AddText(title, Theme.Headline, Theme.Label, y, inner, pad) + Px(8);
            if (!string.IsNullOrEmpty(message)) y = AddText(message, Theme.Footnote, Theme.Label, y, inner, pad) + Px(12);

            if (facts != null && facts.Count > 0)
            {
                int split = pad + inner * 2 / 5, gap = Px(8);
                foreach (var f in facts)
                {
                    var key = new Label { Text = f.Key, Font = Theme.Footnote, ForeColor = Theme.Label2, TextAlign = ContentAlignment.TopRight };
                    var value = new Label { Text = f.Value, Font = Theme.Footnote, ForeColor = Theme.Label, AutoEllipsis = true };
                    int h = TextRenderer.MeasureText(f.Value, Theme.Footnote).Height;
                    key.SetBounds(pad, y, split - gap - pad, h);
                    value.SetBounds(split, y, pad + inner - split, h);
                    Controls.Add(key);
                    Controls.Add(value);
                    y += h + Px(4);
                }
                y += Px(8);
            }

            if (linkText != null)
            {
                var l = new Label { Text = linkText, Font = Theme.Footnote, ForeColor = Theme.Link, Cursor = Cursors.Hand, AutoSize = true };
                Controls.Add(l);
                l.Location = new Point((width - l.PreferredWidth) / 2, y);
                l.Click += delegate { link(); };
                y += l.PreferredHeight + Px(12);
            }

            y += Px(4);
            int bgap = Px(12), bw = (inner - bgap * (buttons.Length - 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                var b = new PillButton(buttons[i], styles[i]);
                b.SetBounds(pad + i * (bw + bgap), y, bw, b.Height);
                b.Click += delegate { result = index; Close(); };
                Controls.Add(b);
                if (i == defaultIndex) { AcceptButton = b; ActiveControl = b; }
                if (i == cancelIndex) CancelButton = b;
            }
            ClientSize = new Size(width, y + Px(32) + pad);
        }

        int AddText(string text, Font font, Color color, int y, int inner, int pad)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(inner, 0), TextFormatFlags.WordBreak);
            var l = new Label { Text = text, Font = font, ForeColor = color, TextAlign = ContentAlignment.TopCenter };
            l.SetBounds(pad, y, inner, size.Height);
            Controls.Add(l);
            return y + size.Height;
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        // Win32 DrawIconEx onto the dialog color: GDI+ (ToBitmap, DrawIcon) fails on PNG icon frames.
        static Bitmap IconImage(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var icon = new Icon(App.AppIcon, size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.WindowBg);
                IntPtr hdc = g.GetHdc();
                try { Native.DrawIconEx(hdc, 0, 0, icon.Handle, size, size, 0, IntPtr.Zero, 3 /* DI_NORMAL */); }
                finally { g.ReleaseHdc(hdc); }
            }
            return bmp;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);
        }

        static int Run(string title, string message, IList<KeyValuePair<string, string>> facts, string linkText, Action link,
                       string[] buttons, ButtonStyle[] styles, int defaultIndex, int cancelIndex)
        {
            using (var d = new AlertDialog(title, message, facts, linkText, link, buttons, styles, defaultIndex, cancelIndex))
            {
                d.ShowDialog();
                return d.result;
            }
        }

        public static void Show(string title, string message)
        {
            Run(title, message, null, null, null, new[] { "OK" }, new[] { ButtonStyle.Primary }, 0, 0);
        }

        // Asks before something that can't be undone: Cancel is the default, the action is red.
        public static bool Confirm(string title, string message, string verb)
        {
            return Run(title, message, null, null, null, new[] { "Cancel", verb },
                new[] { ButtonStyle.Primary, ButtonStyle.Destructive }, 0, 0) == 1;
        }

        // The install prompt: facts, the warning, the source one click away. Cancel is the default.
        public static bool ConfirmInstall(string name, string title, IList<KeyValuePair<string, string>> facts, string source)
        {
            string warning = "A widget runs with your full rights on this PC. It can read your files and install " +
                             "software. Install only code you've read or trust.";
            return Run(title, warning, facts, "Show Source", delegate { SourceViewer.ShowText(name, source); },
                new[] { "Cancel", "Install" }, new[] { ButtonStyle.Primary, ButtonStyle.Grey }, 0, 0) == 1;
        }

        // Shows the installed, hash-checked source (read only).
        public static void ShowSource(string name, string path)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { Show("The source of " + name + " couldn't be opened.", e.Message); return; }
            SourceViewer.ShowText(name, text);
        }
    }

    // Read-only code view in the system monospace font.
    sealed class SourceViewer : Form
    {
        SourceViewer(string name, string text)
        {
            Text = name + " Source";
            Icon = App.AppIcon;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BackColor = Theme.WindowBg;
            ClientSize = new Size(WidgetWindow.Px(760), WidgetWindow.Px(560));
            Padding = new Padding(WidgetWindow.Px(16), WidgetWindow.Px(12), 0, 0);
            var box = new TextBox
            {
                Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), ReadOnly = true, Multiline = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
                Font = Theme.Mono, BackColor = Theme.WindowBg, ForeColor = Theme.Label
            };
            box.HandleCreated += delegate { Theme.ApplyToScrollbars(box.Handle); };
            Controls.Add(box);
            // A focused TextBox selects all of its text; start at the top with nothing selected.
            Shown += delegate { box.SelectionStart = 0; box.SelectionLength = 0; box.ScrollToCaret(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);
        }

        public static void ShowText(string name, string text)
        {
            using (var v = new SourceViewer(name, text)) v.ShowDialog();
        }
    }
}
