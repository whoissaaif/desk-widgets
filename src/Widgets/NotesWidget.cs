using System;
using System.Drawing;

namespace DeskWidgets.Widgets
{
    // Small: one pinned note. Click it to edit.
    sealed class NotesWidget : CardWidget
    {
        public NotesWidget(Section settings) : base(settings, false) { }

        string Title { get { return Settings.GetText("title", "Note"); } }
        string Body { get { return Settings.GetText("body", "Click to write something."); } }

        void Edit()
        {
            var values = InputDialog.Ask("Edit note", null, new[]
            {
                new InputDialog.Field { Label = "Title", Value = Title },
                new InputDialog.Field { Label = "Note", Value = Body.Replace("\n", "\r\n"), Multiline = true }
            }, "Save");
            if (values == null) return;
            Settings.SetText("title", values[0].Trim().Length > 0 ? values[0].Trim() : "Note");
            Settings.SetText("body", values[1].TrimEnd());
            Settings.Set("edited", DateTime.Now.Ticks);
            Render();
        }

        // A click (not a drag) anywhere opens the editor; the note stays draggable.
        protected override void OnWidgetClick(Point p) { Edit(); }

        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            menu.Add("Edit note…", Edit);
        }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            float m = P(16), w = W - m * 2;
            DrawNoteGlyph(g, pal.Yellow, m, m + P(1), P(12));
            Label(g, "Notes", F(13, "semibold"), pal.Yellow, m + P(17), m - P(1));
            Label(g, Title, F(15, "bold"), pal.Fg, m, m + P(22), w, StringAlignment.Near);
            Paragraph(g, Body, F(13, "regular"), pal.Fg2, new RectangleF(m, m + P(44), w, P(70)));

            long ticks;
            string edited = "";
            if (long.TryParse(Settings.Get("edited", ""), out ticks))
            {
                var t = new DateTime(ticks);
                edited = "Edited " + (t.Date == DateTime.Today ? t.ToString("h:mm tt") : t.ToString("d MMM"));
            }
            Label(g, edited, F(11, "regular"), pal.Fg2, m, H - m - P(13));
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
    }
}
