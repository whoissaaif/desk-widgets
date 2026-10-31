using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DeskWidgets
{
    // "Upload a Widget": sends a C# file to the gallery for review, like docs/upload.html. An input
    // sheet (§17.5): pick a widget of yours or start a new one, name, category, description, the
    // source file and an optional preview. A widget you installed from a file brings its own
    // preview, rendered from the widget itself.
    sealed class UploadDialog : Form
    {
        const string NewWidget = "";

        readonly Picker widgetPicker, categoryPicker;
        readonly TextField slug, name, description;
        readonly Label idHint, sourceLabel, previewLabel, error;
        readonly PillButton send, cancel;
        readonly List<string> mine = new List<string>();
        string widget = NewWidget, category = "productivity", sourcePath, source;
        byte[] preview;

        UploadDialog(string path, byte[] renderedPreview)
        {
            Text = "Desk Widgets";
            Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.Label;
            Font = Theme.Footnote;
            AutoScaleMode = AutoScaleMode.None;

            int pad = Px(20), width = Px(420), inner = width - pad * 2, y = pad;
            y = AddLabel("Upload a Widget", Theme.Headline, Theme.Label, y, inner, pad) + Px(4);
            y = AddLabel("The owner reads every version before it appears in Explore. Signed in as " + Account.Username + ".",
                Theme.Footnote, Theme.Label2, y, inner, pad) + Px(16);

            widgetPicker = new Picker();
            widgetPicker.Click += delegate { ShowWidgetMenu(); };
            y = AddRow("Widget", widgetPicker, y, inner, pad);
            slug = new TextField("", false);
            slug.Box.MaxLength = 36;
            slug.Box.TextChanged += delegate { UpdateId(); };
            y = AddRow("Short Name", slug, y, inner, pad) - Px(6);
            idHint = new Label { Font = Theme.Font(12), ForeColor = Theme.Label2 };
            idHint.SetBounds(pad, y, inner, Px(18));
            Controls.Add(idHint);
            y += Px(26);

            name = new TextField("", false);
            name.Box.MaxLength = 40;
            y = AddRow("Name", name, y, inner, pad);
            categoryPicker = new Picker();
            categoryPicker.Click += delegate { ShowCategoryMenu(); };
            y = AddRow("Category", categoryPicker, y, inner, pad);
            description = new TextField("", false);
            description.Box.MaxLength = 120;
            y = AddRow("Description", description, y, inner, pad);

            sourceLabel = new Label { Font = Theme.Footnote, ForeColor = Theme.Label, AutoEllipsis = true };
            y = AddFileRow("C# Source", sourceLabel, "Choose File…", ChooseSource, y, inner, pad);
            previewLabel = new Label { Font = Theme.Footnote, ForeColor = Theme.Label, AutoEllipsis = true };
            y = AddFileRow("Preview (optional)", previewLabel, "Choose PNG…", ChoosePreview, y, inner, pad);

            error = new Label { Font = Theme.Footnote, ForeColor = Theme.Destructive };
            error.SetBounds(pad, y, inner, Px(36));
            Controls.Add(error);
            y += Px(40);

            send = new PillButton("Send for Review", ButtonStyle.Primary);
            send.Click += delegate { Send(); };
            cancel = new PillButton("Cancel", ButtonStyle.Grey) { DialogResult = DialogResult.Cancel };
            send.Location = new Point(width - pad - send.Width, y);
            cancel.Location = new Point(send.Left - Px(12) - cancel.Width, y);
            Controls.Add(send);
            Controls.Add(cancel);
            AcceptButton = send;
            CancelButton = cancel;
            ClientSize = new Size(width, y + send.Height + pad);

            preview = renderedPreview;
            previewLabel.Text = preview != null ? "Rendered from your widget" : "None";
            if (path != null) LoadSource(path);
            SetCategory(category);
            SetWidget(NewWidget);
            ActiveControl = name.Box.Text.Length == 0 ? name.Box : slug.Box;
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        int AddLabel(string text, Font font, Color color, int y, int inner, int pad)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(inner, 0), TextFormatFlags.WordBreak);
            var l = new Label { Text = text, Font = font, ForeColor = color };
            l.SetBounds(pad, y, inner, size.Height);
            Controls.Add(l);
            return y + size.Height;
        }

        int AddRow(string label, Control field, int y, int inner, int pad)
        {
            y = AddLabel(label, Theme.Footnote, Theme.Label2, y, inner, pad) + Px(6);
            field.SetBounds(pad, y, inner, field.Height);
            Controls.Add(field);
            return y + field.Height + Px(12);
        }

        int AddFileRow(string label, Label value, string buttonText, Action choose, int y, int inner, int pad)
        {
            y = AddLabel(label, Theme.Footnote, Theme.Label2, y, inner, pad) + Px(6);
            var button = new PillButton(buttonText, ButtonStyle.Grey);
            button.Location = new Point(pad + inner - button.Width, y);
            button.Click += delegate { choose(); };
            Controls.Add(button);
            value.SetBounds(pad, y + (button.Height - Px(18)) / 2, inner - button.Width - Px(12), Px(18));
            Controls.Add(value);
            return y + button.Height + Px(12);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);
        }

        // Your widgets arrive after the sheet opens; a new version of one you already sent is the
        // usual case, so it's picked when the file's short name matches.
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> ids = null;
                try { ids = Account.MyWidgets(); }
                catch (Exception) { }
                App.RunOnUi(delegate
                {
                    if (IsDisposed || ids == null) return;
                    mine.AddRange(ids);
                    string match = mine.FirstOrDefault(id => id == "u." + Account.Username + "." + slug.Box.Text);
                    if (match != null && widget == NewWidget) SetWidget(match);
                });
            });
        }

        // ---- choices ----

        void ShowWidgetMenu()
        {
            var menu = new PopupMenu();
            menu.AddChoice("New Widget", widget == NewWidget, delegate { SetWidget(NewWidget); });
            if (mine.Count > 0) menu.AddSeparator();
            foreach (string id in mine) { string pick = id; menu.AddChoice(id, widget == id, delegate { SetWidget(pick); }); }
            menu.Show(widgetPicker.PointToScreen(new Point(0, widgetPicker.Height)));
        }

        void ShowCategoryMenu()
        {
            var menu = new PopupMenu();
            foreach (var c in WidgetRegistry.Categories) { string id = c.Id; menu.AddChoice(c.Name, category == id, delegate { SetCategory(id); }); }
            menu.Show(categoryPicker.PointToScreen(new Point(0, categoryPicker.Height)));
        }

        void SetWidget(string id)
        {
            widget = id;
            widgetPicker.Text = id == NewWidget ? "New Widget" : id + " (new version)";
            widgetPicker.Invalidate();
            slug.Enabled = id == NewWidget;
            UpdateId();
        }

        void SetCategory(string id)
        {
            category = id;
            categoryPicker.Text = WidgetRegistry.Categories.First(c => c.Id == id).Name;
            categoryPicker.Invalidate();
        }

        void UpdateId()
        {
            idHint.Text = widget != NewWidget ? "Sends version 2 or later of " + widget + "."
                : "Its id will be u." + Account.Username + "." + (slug.Box.Text.Length > 0 ? slug.Box.Text : "short-name") + ". It can't change later.";
        }

        // ---- files ----

        void ChooseSource()
        {
            using (var pick = new OpenFileDialog { Title = "Choose the Widget's Source", Filter = "C# widget (*.cs)|*.cs" })
                if (pick.ShowDialog(this) == DialogResult.OK) LoadSource(pick.FileName);
        }

        void LoadSource(string path)
        {
            string text;
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length > Plugins.MaxSourceBytes) { error.Text = "The file is larger than 512 KB."; return; }
                text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF');
            }
            catch (DecoderFallbackException) { error.Text = "The file isn't UTF-8 text."; return; }
            catch (Exception e) { error.Text = "The file couldn't be read: " + e.Message; return; }
            sourcePath = path;
            source = text;
            sourceLabel.Text = Path.GetFileName(path);
            error.Text = "";
            // Fill in what the file already says about itself.
            Match m = Regex.Match(text, @"\[\s*DeskWidget\s*\(([^)]*)\)");
            Func<string, string> attr = key =>
            {
                Match a = Regex.Match(m.Groups[1].Value, key + @"\s*=\s*""((?:[^""\\]|\\.)*)""");
                return a.Success ? Regex.Unescape(a.Groups[1].Value) : null;
            };
            if (m.Success && name.Box.Text.Length == 0) name.Box.Text = Trim(attr("Name"), 40);
            if (m.Success && description.Box.Text.Length == 0) description.Box.Text = Trim(attr("Description"), 120);
            if (slug.Box.Text.Length == 0) slug.Box.Text = Slug(Path.GetFileNameWithoutExtension(path));
        }

        static string Trim(string s, int max) { s = s ?? ""; return s.Length > max ? s.Substring(0, max) : s; }

        static string Slug(string fileName)
        {
            string s = Regex.Replace(Regex.Replace(fileName, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (s.EndsWith("-widget") && s.Length > 7) s = s.Substring(0, s.Length - 7);
            return s.Length > 36 ? s.Substring(0, 36).Trim('-') : s;
        }

        void ChoosePreview()
        {
            using (var pick = new OpenFileDialog { Title = "Choose a Preview", Filter = "PNG image (*.png)|*.png" })
            {
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                byte[] png;
                try { png = File.ReadAllBytes(pick.FileName); }
                catch (Exception e) { error.Text = "The image couldn't be read: " + e.Message; return; }
                if (png.Length > 100 * 1024) { error.Text = "The preview is larger than 100 KB."; return; }
                if (png.Length < 8 || png[0] != 0x89 || png[1] != 0x50) { error.Text = "The preview must be a PNG image."; return; }
                preview = png;
                previewLabel.Text = Path.GetFileName(pick.FileName);
                error.Text = "";
            }
        }

        // A widget's snapshot as a PNG under 100 KB, or null.
        public static byte[] PreviewOf(WidgetType type)
        {
            Bitmap shot = type != null ? type.Snapshot() : null;
            if (shot == null) return null;
            using (shot)
                for (float scale = 1f; scale >= 0.25f; scale /= 2)
                {
                    int w = Math.Max(1, (int)(shot.Width * scale)), h = Math.Max(1, (int)(shot.Height * scale));
                    if (w > 1024 || h > 1024) continue;
                    using (var small = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                    using (var ms = new MemoryStream())
                    {
                        using (var g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(shot, 0, 0, w, h);
                        }
                        small.Save(ms, ImageFormat.Png);
                        if (ms.Length <= 100 * 1024) return ms.ToArray();
                    }
                }
            return null;
        }

        // ---- sending ----

        void Send()
        {
            Func<string, bool> fail = message => { error.Text = message; return false; };
            string text = source;
            if (text == null) { fail("Choose the widget's C# file."); return; }
            if (widget == NewWidget && !Regex.IsMatch(slug.Box.Text, "^[a-z0-9-]{1,36}$"))
            { fail("The short name can use lowercase letters, digits and hyphens."); return; }
            if (name.Box.Text.Trim().Length == 0) { fail("Give the widget a name."); return; }
            if (!Regex.IsMatch(text, @"\[\s*DeskWidget\b")) { fail("The source needs one class marked [DeskWidget]."); return; }
            string hidden = Plugins.HiddenCharacter(text);
            if (hidden != null) { fail(Plugins.HiddenMessage(hidden)); return; }

            var upload = new Account.Upload
            {
                WidgetId = widget == NewWidget ? null : widget, Slug = slug.Box.Text, Name = name.Box.Text.Trim(),
                Category = category, Description = description.Box.Text.Trim(), Source = text, Preview = preview
            };
            send.Enabled = cancel.Enabled = false;
            error.ForeColor = Theme.Label2;
            error.Text = "Sending…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string id;
                int version;
                string problem = Account.Send(upload, out id, out version);
                App.RunOnUi(delegate
                {
                    if (IsDisposed) return;
                    send.Enabled = cancel.Enabled = true;
                    error.ForeColor = Theme.Destructive;
                    if (problem != null) { error.Text = problem; return; }
                    sentId = id;
                    sentVersion = version;
                    DialogResult = DialogResult.OK;
                });
            });
        }

        string sentId;
        int sentVersion;

        // Asks for the details and sends them. path and plugin are both optional: a widget installed
        // from a file brings its source and a rendered preview.
        public static void Run(string path, PluginInfo plugin)
        {
            byte[] rendered = plugin != null ? PreviewOf(plugin.Type) : null;
            using (var d = new UploadDialog(path, rendered))
            {
                if (plugin != null)
                {
                    if (plugin.Name != null) d.name.Box.Text = Trim(plugin.Name, 40);
                    d.slug.Box.Text = Trim(plugin.Id.Substring(plugin.Id.LastIndexOf('.') + 1), 36); // u.local.<slug>
                }
                if (d.ShowDialog() != DialogResult.OK) return;
                AlertDialog.Show("Sent for review.",
                    d.sentId + (d.sentVersion > 0 ? " version " + d.sentVersion : "") +
                    " is waiting for the owner. It appears in Explore once it's approved.");
            }
        }
    }

    // A pop-up button in the shape of a text field (§8.7): the current choice and a chevron. Opens a
    // menu of choices (PopupMenu) below it.
    sealed class Picker : Control
    {
        public Picker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = new TextBox { Font = Theme.Body }.PreferredHeight + WidgetWindow.Px(14);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            float radius = WidgetWindow.Px(Theme.RadiusSmall);
            Theme.FillRound(g, Theme.Field, r, radius);
            using (var path = Theme.RoundRect(r, radius))
            using (var pen = new Pen(Focused ? Theme.Link : Theme.FieldBorder, Focused ? 2f : 1f))
                g.DrawPath(pen, path);
            int px = WidgetWindow.Px(10), chevron = WidgetWindow.Px(20);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(px, 0, Width - px * 2 - chevron, Height), Theme.Label, flags);
            TextRenderer.DrawText(g, "\uE70D", Theme.Icons, new Rectangle(Width - px - chevron, 0, chevron, Height), Theme.Link,
                flags | TextFormatFlags.HorizontalCenter);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Down) OnClick(EventArgs.Empty);
        }
    }
}
