using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskWidgets
{
    // Small dialog for the few things widgets need typed in (a reminder, a city, a token), laid out
    // like an Apple sheet (§17): bold title, a hint, labeled rounded fields, then Cancel and the
    // blue default button on the right. Created when needed, disposed right after.
    public sealed class InputDialog : Form
    {
        public sealed class Field
        {
            public string Label, Value;
            public bool Secret;
        }

        readonly List<TextBox> boxes = new List<TextBox>();
        bool fadingOut;

        InputDialog(string title, string hint, Field[] fields, string okText, string extraText, Action<InputDialog> extra)
        {
            Text = "Desk Widgets"; // the title is in the sheet itself
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.Label;
            Font = Theme.Footnote;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;

            int pad = Px(20), width = Px(420), inner = width - pad * 2, y = pad;
            y = AddLabel(title, Theme.Headline, Theme.Label, y, inner, pad) + Px(4);
            if (!string.IsNullOrEmpty(hint)) y = AddLabel(hint, Theme.Footnote, Theme.Label2, y, inner, pad);
            y += Px(16);
            foreach (var f in fields)
            {
                y = AddLabel(f.Label, Theme.Footnote, Theme.Label2, y, inner, pad) + Px(6);
                var field = new TextField(f.Value, f.Secret);
                field.SetBounds(pad, y, inner, field.Height);
                Controls.Add(field);
                boxes.Add(field.Box);
                y += field.Height + Px(12);
            }

            y += Px(8);
            var ok = new PillButton(okText, ButtonStyle.Primary) { DialogResult = DialogResult.OK };
            var cancel = new PillButton("Cancel", ButtonStyle.Grey) { DialogResult = DialogResult.Cancel };
            ok.Location = new Point(width - pad - ok.Width, y);
            cancel.Location = new Point(ok.Left - Px(12) - cancel.Width, y);
            Controls.Add(ok);
            Controls.Add(cancel);
            if (extraText != null)
            {
                var more = new PillButton(extraText, ButtonStyle.Grey) { Location = new Point(pad, y) };
                more.Click += delegate { extra(this); };
                Controls.Add(more);
            }
            AcceptButton = ok;
            CancelButton = cancel;
            if (boxes.Count > 0) ActiveControl = boxes[0];
            ClientSize = new Size(width, y + ok.Height + pad);
            Opacity = 0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Anim.Run(0f, 1f, 150, delegate(float v) { if (!IsDisposed) Opacity = v; });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!fadingOut && Opacity > 0)
            {
                // Save before cancel resets it to None.
                var result = DialogResult;
                e.Cancel = true;
                fadingOut = true;
                Anim.Run(1f, 0f, 120, delegate(float v) { if (!IsDisposed) Opacity = v; }, delegate { if (!IsDisposed) { DialogResult = result; Close(); } });
                return;
            }
            base.OnFormClosing(e);
        }

        int AddLabel(string text, Font font, Color color, int y, int inner, int pad)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(inner, 0), TextFormatFlags.WordBreak);
            var l = new Label { Text = text, Font = font, ForeColor = color };
            l.SetBounds(pad, y, inner, size.Height);
            Controls.Add(l);
            return y + size.Height;
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        public void SetValue(int index, string value) { boxes[index].Text = value; }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);
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
                if (d.ShowDialog(AlertDialog.noOwner) != DialogResult.OK) return null;
                return d.boxes.ConvertAll(b => b.Text).ToArray();
            }
        }
    }
}
