using System;
using System.Drawing;
using System.Windows.Forms;
using DeskWidgets;
using DeskWidgets.Widgets;

// Small: one pinned note. Click it and type straight into it; click away (or press Esc) to
// save. Like Apple Notes, the first line is the title.
[DeskWidget(Name = "Notes", Description = "One note, click to edit")]
public sealed class NotesWidget : CardWidget
{
    NoteEditor editor;

    public NotesWidget(Section settings) : base(settings, false) { }

    // Older versions stored a separate title and body: fold them into one text.
    string NoteText
    {
        get
        {
            string text = Settings.GetText("text", null);
            if (text != null) return text;
            string title = Settings.GetText("title", null), body = Settings.GetText("body", null);
            return title == null && body == null ? "" : (title ?? "") + "\n" + (body ?? "");
        }
    }

    // Where the text sits, below the "Notes" header and above the "Edited" line.
    RectangleF TextArea { get { float m = P(16); return new RectangleF(m, m + P(22), W - m * 2, H - m * 2 - P(22) - P(18)); } }

    void BeginEdit()
    {
        if (editor != null) { editor.Activate(); return; }
        RectangleF a = TextArea;
        var screen = new Rectangle(Left + (int)a.X, Top + (int)a.Y, (int)a.Width, (int)a.Height);
        Color back = RenderedColor(new Point((int)(a.X + a.Width - 3), (int)(a.Bottom - 3)));
        editor = new NoteEditor(screen, NoteText.Replace("\n", "\r\n"), F(13, "regular"), back, Pal.Fg);
        editor.Done += text =>
        {
            editor = null;
            text = text.Replace("\r\n", "\n").TrimEnd();
            if (text != NoteText.TrimEnd())
            {
                Settings.SetText("text", text);
                Settings.Remove("title");
                Settings.Remove("body");
                Settings.Set("edited", DateTime.Now.Ticks);
            }
            Render();
        };
        editor.Show();
    }

    // A click (not a drag) anywhere starts typing; the note stays draggable.
    protected override void OnWidgetClick(Point p) { BeginEdit(); }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        menu.Add("Edit Note", BeginEdit);
    }

    protected override void DrawContent(Graphics g, Palette pal)
    {
        float m = P(16), w = W - m * 2;
        DrawNoteGlyph(g, pal.Yellow, m, m + P(1), P(12));
        Label(g, "Notes", F(13, "semibold"), pal.Yellow, m + P(17), m - P(1));

        string text = NoteText;
        if (text.Trim().Length == 0)
            Label(g, "Click to write…", F(13, "regular"), pal.Fg2, m, m + P(24), w, StringAlignment.Near);
        else
        {
            int nl = text.IndexOf('\n');
            string title = nl < 0 ? text : text.Substring(0, nl);
            string body = nl < 0 ? "" : text.Substring(nl + 1).Trim();
            Label(g, title, F(15, "bold"), pal.Fg, m, m + P(22), w, StringAlignment.Near);
            if (body.Length > 0) Paragraph(g, body, F(13, "regular"), pal.Fg2, new RectangleF(m, m + P(44), w, P(70)));
        }

        long ticks;
        if (long.TryParse(Settings.Get("edited", ""), out ticks))
        {
            var t = new DateTime(ticks);
            Label(g, "Edited " + (t.Date == DateTime.Today ? t.ToString("t") : t.ToString("d MMM")), F(11, "regular"), pal.Fg2, m, H - m - P(13));
        }
    }

    static void DrawNoteGlyph(Graphics g, Color c, float x, float y, float s)
    {
        FillRound(g, c, new RectangleF(x, y, s, s), s * 0.22f);
        using (var pen = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1f, s * 0.11f)))
        {
            g.DrawLine(pen, x + s * 0.25f, y + s * 0.38f, x + s * 0.75f, y + s * 0.38f);
            g.DrawLine(pen, x + s * 0.25f, y + s * 0.62f, x + s * 0.6f, y + s * 0.62f);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && editor != null) editor.Close();
        base.Dispose(disposing);
    }

    // Borderless text box laid exactly over the note's text, in the note's own colours, so typing
    // looks like writing on the widget. (Layered widget windows can't host a real text box.)
    public sealed class NoteEditor : Form
    {
        readonly TextBox box;
        bool finished;
        public event Action<string> Done;

        public NoteEditor(Rectangle screenRect, string text, Font font, Color back, Color fore)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = screenRect;
            BackColor = back;
            box = new TextBox
            {
                Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None, BackColor = back, ForeColor = fore, Font = font,
                Dock = DockStyle.Fill, Text = text
            };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(); }
                else if (e.Control && e.KeyCode == Keys.A) { e.SuppressKeyPress = true; box.SelectAll(); }
            };
            Controls.Add(box);
            Deactivate += delegate { Finish(); }; // clicking anywhere else saves
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: not in the taskbar or Alt+Tab
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            box.Focus();
            box.SelectionStart = box.TextLength;
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            if (Done != null) Done(box.Text);
            BeginInvoke((Action)Close);
        }
    }
}
