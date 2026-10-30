using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskWidgets
{
    // Small dark dialog for the few things widgets need typed in (a note, a reminder, a token).
    // Created when needed, disposed right after, so it costs nothing the rest of the time.
    sealed class InputDialog : Form
    {
        public sealed class Field
        {
            public string Label, Value;
            public bool Multiline, Secret;
        }

        static readonly Color Bg = Color.FromArgb(32, 32, 34), Box = Color.FromArgb(45, 45, 48), Muted = Color.FromArgb(160, 160, 168);
        readonly List<TextBox> boxes = new List<TextBox>();

        InputDialog(string title, string hint, Field[] fields, string okText, string extraText, Action<InputDialog> extra)
        {
            Text = title;
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = Bg;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;

            int pad = Px(18), width = Px(420), y = Px(16);
            if (!string.IsNullOrEmpty(hint))
            {
                var h = new Label { Text = hint, ForeColor = Muted, Location = new Point(pad, y), MaximumSize = new Size(width - pad * 2, 0), AutoSize = true };
                Controls.Add(h);
                y += h.PreferredHeight + Px(12);
            }
            foreach (var f in fields)
            {
                var l = new Label { Text = f.Label, AutoSize = true, Location = new Point(pad, y) };
                Controls.Add(l);
                y += l.PreferredHeight + Px(4);
                var tb = new TextBox
                {
                    Text = f.Value ?? "", Multiline = f.Multiline, UseSystemPasswordChar = f.Secret,
                    BackColor = Box, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(pad, y), Width = width - pad * 2,
                    Height = f.Multiline ? Px(120) : Px(26), ScrollBars = f.Multiline ? ScrollBars.Vertical : ScrollBars.None,
                    AcceptsReturn = f.Multiline
                };
                if (f.Multiline) tb.HandleCreated += delegate { Native.UseDarkControlTheme(tb.Handle); };
                Controls.Add(tb);
                boxes.Add(tb);
                y += tb.Height + Px(12);
            }

            var ok = new Button { Text = okText, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(10, 132, 255), ForeColor = Color.White, Size = new Size(Px(96), Px(30)) };
            ok.FlatAppearance.BorderSize = 0;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Box, ForeColor = Color.White, Size = new Size(Px(96), Px(30)) };
            cancel.FlatAppearance.BorderSize = 0;
            y += Px(4);
            ok.Location = new Point(width - pad - ok.Width, y);
            cancel.Location = new Point(ok.Left - Px(8) - cancel.Width, y);
            Controls.Add(ok);
            Controls.Add(cancel);
            if (extraText != null)
            {
                var more = new Button { Text = extraText, FlatStyle = FlatStyle.Flat, BackColor = Box, ForeColor = Color.White, AutoSize = true, Height = Px(30), Location = new Point(pad, y) };
                more.FlatAppearance.BorderSize = 0;
                more.Click += delegate { extra(this); };
                Controls.Add(more);
            }
            AcceptButton = fields.Length > 0 && fields[fields.Length - 1].Multiline ? null : ok;
            CancelButton = cancel;
            ClientSize = new Size(width, y + ok.Height + pad);
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        public void SetValue(int index, string value) { boxes[index].Text = value; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
        }

        // Returns the field values, or null if cancelled.
        public static string[] Ask(string title, string hint, Field[] fields, string okText)
        {
            return Ask(title, hint, fields, okText, null, null);
        }

        public static string[] Ask(string title, string hint, Field[] fields, string okText, string extraText, Action<InputDialog> extra)
        {
            using (var d = new InputDialog(title, hint, fields, okText, extraText, extra))
            {
                if (d.ShowDialog() != DialogResult.OK) return null;
                return d.boxes.ConvertAll(b => b.Text).ToArray();
            }
        }
    }
}
