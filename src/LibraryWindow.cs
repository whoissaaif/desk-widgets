using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeskWidgets
{
    // The main app window, laid out like Apple's web apps (design/apple-design.md §2.9, §17): a quiet
    // sidebar (categories, then Installed), a Large Title page header, and a grid of cards per
    // category. Drag a card onto the desktop (or double-click it) to add that widget. The window only
    // exists while open, so it costs nothing the rest of the time.
    sealed class LibraryWindow : Form
    {
        const string InstalledPage = "@installed", ExplorePage = "@explore";

        sealed class Section
        {
            public WidgetCategory Category;
            public SectionHeader Header;
            public List<WidgetCard> Cards = new List<WidgetCard>();
        }

        readonly Panel content, sidebar;
        readonly PageHeader header;
        readonly PillButton installButton;
        readonly EmptyState noneInstalled, exploreState;
        readonly PillButton uploadButton;
        readonly List<ExploreCard> exploreCards = new List<ExploreCard>();
        readonly List<PluginRow> updates = new List<PluginRow>();
        bool exploreLoading, exploreLoaded;
        readonly List<Section> sections = new List<Section>();
        readonly List<WidgetCard> installedCards = new List<WidgetCard>();
        readonly List<PluginRow> turnedOff = new List<PluginRow>();
        readonly List<SidebarItem> nav = new List<SidebarItem>();
        readonly SidebarItem startup;
        string page; // category id, InstalledPage, or null for all widgets

        public LibraryWindow()
        {
            Text = "Desk Widgets";
            Icon = App.AppIcon;
            BackColor = Theme.WindowBg;
            ForeColor = Theme.Label;
            Font = Theme.Footnote;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Px(980), Px(640));
            MinimumSize = new Size(Px(640), Px(420));

            content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.WindowBg };
            content.HandleCreated += delegate { Theme.ApplyToScrollbars(content.Handle); };
            header = new PageHeader();
            content.Controls.Add(header);
            installButton = new PillButton("Install from File…", ButtonStyle.Tinted);
            installButton.Click += delegate { App.InstallFromFile(); };
            content.Controls.Add(installButton);
            noneInstalled = new EmptyState("No Installed Widgets", "Widgets you install from a file appear here.");
            content.Controls.Add(noneInstalled);
            uploadButton = new PillButton("Upload a Widget ↗", ButtonStyle.Tinted);
            uploadButton.Click += delegate { OpenWebsite("upload.html"); };
            content.Controls.Add(uploadButton);
            exploreState = new EmptyState("", "");
            content.Controls.Add(exploreState);
            BuildCards();
            content.Resize += delegate { LayoutContent(); };

            sidebar = new Panel { Dock = DockStyle.Left, Width = Px(260), BackColor = Theme.SidebarBg };
            BuildNav();
            startup = new SidebarItem('', "Start with Windows") { Toggle = true, On = App.StartWithWindows };
            startup.SetBounds(Px(12), sidebar.Height - Px(50), sidebar.Width - Px(24), Px(34));
            startup.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            startup.Click += delegate
            {
                App.StartWithWindows = !App.StartWithWindows;
                startup.On = App.StartWithWindows;
                startup.Invalidate();
            };
            sidebar.Controls.Add(startup);

            Controls.Add(content);
            Controls.Add(sidebar);
            App.Current.WidgetsChanged += OnWidgetsChanged;
            WidgetRegistry.Changed += OnRegistryChanged;
            Theme.Changed += OnThemeChanged;
            ShowPage(null);
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        // ---- building ----

        // Cards per category, the Installed page's cards and its list of turned-off widgets.
        // A widget whose preview crashes gets no card.
        void BuildCards()
        {
            Func<WidgetType, WidgetCard> card = t =>
            {
                Bitmap preview = t.Snapshot();
                if (preview == null) return null;
                var c = new WidgetCard(t, preview);
                content.Controls.Add(c);
                return c;
            };
            foreach (var cat in WidgetRegistry.Categories)
            {
                var sec = new Section { Category = cat };
                foreach (var t in WidgetRegistry.InCategory(cat.Id))
                {
                    var c = card(t);
                    if (c != null) sec.Cards.Add(c);
                }
                if (sec.Cards.Count == 0) continue;
                sec.Header = new SectionHeader(cat.Name, cat.Description);
                string id = cat.Id;
                sec.Header.Click += delegate { ShowPage(id); };
                content.Controls.Add(sec.Header);
                sections.Add(sec);
            }
            foreach (var p in Plugins.Installed)
            {
                if (p.Type != null)
                {
                    var c = card(p.Type);
                    if (c != null) installedCards.Add(c);
                    if (p.UpdateVersion > p.Version)
                    {
                        var update = new PluginRow(p, true);
                        content.Controls.Add(update);
                        updates.Add(update);
                    }
                }
                else
                {
                    var row = new PluginRow(p, false);
                    content.Controls.Add(row);
                    turnedOff.Add(row);
                }
            }
        }

        void ClearCards()
        {
            var old = new List<Control>();
            foreach (var sec in sections) { old.Add(sec.Header); old.AddRange(sec.Cards.Cast<Control>()); }
            old.AddRange(installedCards.Cast<Control>());
            old.AddRange(turnedOff.Cast<Control>());
            old.AddRange(updates.Cast<Control>());
            foreach (var c in old) { content.Controls.Remove(c); c.Dispose(); }
            sections.Clear();
            installedCards.Clear();
            turnedOff.Clear();
            updates.Clear();
        }

        void BuildNav()
        {
            foreach (var c in sidebar.Controls.Cast<Control>().Where(c => c != startup).ToArray()) { sidebar.Controls.Remove(c); c.Dispose(); }
            nav.Clear();
            var title = new Label { Text = "Desk Widgets", Font = Theme.Title3, ForeColor = Theme.Label, AutoSize = true, Location = new Point(Px(24), Px(22)) };
            sidebar.Controls.Add(title);
            int y = Px(72);
            AddNav('', "All Widgets", null, ref y);
            y += Px(12);
            AddGroupLabel("Categories", ref y);
            foreach (var sec in sections) AddNav(sec.Category.Glyph, sec.Category.Name, sec.Category.Id, ref y);
            y += Px(12);
            AddGroupLabel("Community", ref y);
            AddNav('\uE774', "Explore", ExplorePage, ref y);
            AddNav('', "Installed", InstalledPage, ref y);
        }

        void AddGroupLabel(string text, ref int y)
        {
            var l = new Label { Text = text, Font = Theme.CaptionEmphasized, ForeColor = Theme.Label2, AutoSize = true, Location = new Point(Px(24), y) };
            sidebar.Controls.Add(l);
            y += Px(24);
        }

        void AddNav(char glyph, string text, string target, ref int y)
        {
            var item = new SidebarItem(glyph, text) { Tag = target };
            item.SetBounds(Px(12), y, sidebar.Width - Px(24), Px(34));
            item.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            item.Click += delegate { ShowPage((string)item.Tag); };
            sidebar.Controls.Add(item);
            nav.Add(item);
            y += Px(36);
        }

        void ShowPage(string target)
        {
            page = target;
            foreach (var item in nav)
            {
                item.Selected = (string)item.Tag == target;
                item.Invalidate();
            }
            var cat = sections.Select(s => s.Category).FirstOrDefault(c => c.Id == target);
            if (target == ExplorePage)
            {
                header.Set("Explore", "Widgets made by the community. Each one is reviewed before it appears here.");
                if (!exploreLoaded && !exploreLoading) LoadExplore();
            }
            else if (target == InstalledPage)
                header.Set("Installed", "Widgets you installed. Drag one onto your desktop to use it.");
            else if (cat != null)
                header.Set(cat.Name, cat.Description + ". Drag a widget onto your desktop, or double-click to add it.");
            else
                header.Set("All Widgets", "Drag a widget onto your desktop, or double-click to add it.");
            content.AutoScrollPosition = Point.Empty;
            LayoutContent();
        }

        // ---- layout: header, then wrapping grids of cards ----

        void LayoutContent()
        {
            int pad = Px(32), gap = Px(20);
            int width = Math.Max(Px(200), content.ClientSize.Width - pad * 2);
            int scroll = content.AutoScrollPosition.Y;
            int y = Px(28);
            content.SuspendLayout();
            bool installed = page == InstalledPage, explore = page == ExplorePage;

            PillButton action = installed ? installButton : explore ? uploadButton : null;
            header.Width = width - (action != null ? action.Width + gap : 0);
            header.Location = new Point(pad, y + scroll);
            installButton.Visible = installed;
            uploadButton.Visible = explore;
            if (action != null) action.Location = new Point(pad + width - action.Width, y + scroll + Px(6));
            y += header.Height + Px(24);

            foreach (var card in exploreCards) card.Visible = explore;
            exploreState.Visible = explore && exploreCards.Count == 0;
            if (exploreState.Visible)
            {
                exploreState.SetBounds(pad, y + scroll + Px(80), width, exploreState.Height);
                y += Px(80) + exploreState.Height;
            }
            if (explore) y = Grid(exploreCards, pad, width, y, scroll) + Px(32);

            foreach (var sec in sections)
            {
                bool show = page == null || sec.Category.Id == page;
                sec.Header.Visible = show && page == null;
                foreach (var card in sec.Cards) card.Visible = show;
                if (!show) continue;
                if (page == null)
                {
                    sec.Header.SetBounds(pad, y + scroll, width, sec.Header.Height);
                    y += sec.Header.Height + Px(12);
                }
                y = Grid(sec.Cards, pad, width, y, scroll) + Px(32);
            }

            foreach (var card in installedCards) card.Visible = installed;
            foreach (var row in turnedOff) row.Visible = installed;
            foreach (var row in updates) row.Visible = installed;
            noneInstalled.Visible = installed && installedCards.Count == 0 && turnedOff.Count == 0;
            if (noneInstalled.Visible)
            {
                noneInstalled.SetBounds(pad, y + scroll + Px(80), width, noneInstalled.Height);
                y += Px(80) + noneInstalled.Height;
            }
            if (installed)
            {
                y = Group(updates, pad, width, y, scroll);
                if (installedCards.Count > 0) y = Grid(installedCards, pad, width, y, scroll) + Px(32);
                y = Group(turnedOff, pad, width, y, scroll);
            }
            content.AutoScrollMinSize = new Size(0, y + Px(32));
            content.ResumeLayout();
        }

        // Cards flow left to right and wrap; medium widgets have double-width cards.
        int Grid<T>(List<T> cards, int pad, int width, int y, int scroll) where T : Control
        {
            if (cards.Count == 0) return y;
            int gap = Px(20), x = pad, h = cards[0].Height;
            foreach (var card in cards)
            {
                if (x > pad && x + card.Width > pad + width) { x = pad; y += h + gap; }
                card.Location = new Point(x, y + scroll);
                x += card.Width + gap;
            }
            return y + h;
        }

        // Rows stacked into one grouped inset list.
        int Group(List<PluginRow> rows, int pad, int width, int y, int scroll)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].First = i == 0;
                rows[i].Last = i == rows.Count - 1;
                rows[i].SetBounds(pad, y + scroll, width, rows[i].Height);
                y += rows[i].Height;
            }
            return rows.Count > 0 ? y + Px(32) : y;
        }

        // ---- Explore ----

        void LoadExplore()
        {
            if (!Community.Configured)
            {
                exploreState.Set("Explore Isn’t Connected Yet", "Community widgets appear here once the gallery is online.");
                exploreLoaded = true;
                return;
            }
            exploreLoading = true;
            exploreState.Set("Loading…", "");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<Community.Listing> list = null;
                var previews = new Dictionary<Community.Listing, Bitmap>();
                try
                {
                    list = Community.Explore();
                    foreach (var l in list) previews[l] = ExploreCard.DecodePreview(l.Preview);
                }
                catch (Exception) { list = null; }
                App.RunOnUi(delegate
                {
                    exploreLoading = false;
                    if (IsDisposed) { foreach (var b in previews.Values) if (b != null) b.Dispose(); return; }
                    if (list == null)
                        exploreState.Set("Explore Couldn’t Load", "Check your connection, then open Explore again.");
                    else
                    {
                        exploreLoaded = true;
                        if (list.Count == 0) exploreState.Set("Nothing Here Yet", "Reviewed community widgets will appear here.");
                        foreach (var l in list)
                        {
                            var c = new ExploreCard(l, previews[l]);
                            exploreCards.Add(c);
                            content.Controls.Add(c);
                        }
                    }
                    if (page == ExplorePage) LayoutContent();
                });
            });
        }

        static void OpenWebsite(string path)
        {
            try { System.Diagnostics.Process.Start(Community.Website + path); }
            catch (Exception) { }
        }

        // ---- changes ----

        void OnWidgetsChanged()
        {
            foreach (Control c in content.Controls) c.Invalidate();
        }

        // Community widgets were installed, removed, turned on or off: rebuild the cards. Posted, so a
        // preview that crashes (and turns its widget off) mid-rebuild doesn't start a rebuild inside it.
        bool rebuildPosted;

        void OnRegistryChanged()
        {
            if (rebuildPosted || !IsHandleCreated) return;
            rebuildPosted = true;
            BeginInvoke(new Action(Rebuild));
        }

        void Rebuild()
        {
            rebuildPosted = false;
            if (IsDisposed) return;
            content.SuspendLayout();
            ClearCards();
            BuildCards();
            content.ResumeLayout();
            BuildNav();
            foreach (var c in exploreCards) c.RefreshState();
            ShowPage(page == InstalledPage || page == ExplorePage || sections.Exists(s => s.Category.Id == page) ? page : null);
        }

        // Windows switched light/dark: recolor everything.
        void OnThemeChanged()
        {
            BackColor = content.BackColor = Theme.WindowBg;
            sidebar.BackColor = Theme.SidebarBg;
            Theme.ApplyToWindow(Handle);
            Theme.ApplyToScrollbars(content.Handle);
            BuildNav();
            ShowPage(page);
            foreach (Control c in content.Controls) c.Invalidate();
            startup.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            App.Current.WidgetsChanged -= OnWidgetsChanged;
            WidgetRegistry.Changed -= OnRegistryChanged;
            Theme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }

    // Base for the library's painted parts: double-buffered, cleared to the parent's color.
    abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected static int Px(float v) { return WidgetWindow.Px(v); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(e.Graphics);
        }

        protected abstract void Draw(Graphics g);
    }

    // Large Title (34/700) over one line of secondary text.
    sealed class PageHeader : PaintedControl
    {
        string title = "", subtitle = "";

        public void Set(string title, string subtitle)
        {
            this.title = title;
            this.subtitle = subtitle;
            Height = Px(41) + Px(4) + Theme.Footnote.Height;
            Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            TextRenderer.DrawText(g, title, Theme.LargeTitle, Point.Empty, Theme.Label, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, subtitle, Theme.Footnote, new Rectangle(0, Px(45), Width, Theme.Footnote.Height + Px(4)),
                Theme.Label2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    // Nothing to show yet: a centered headline and one line of help, both secondary.
    sealed class EmptyState : PaintedControl
    {
        string title, message;

        public EmptyState(string title, string message)
        {
            this.title = title;
            this.message = message;
            Height = Px(28) + Px(6) + Theme.Footnote.Height;
        }

        public void Set(string title, string message)
        {
            this.title = title;
            this.message = message;
            Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, title, Theme.Font(22, 700), new Rectangle(0, 0, Width, Px(28)), Theme.Label2, flags);
            TextRenderer.DrawText(g, message, Theme.Footnote, new Rectangle(0, Px(34), Width, Theme.Footnote.Height), Theme.Label2, flags);
        }
    }

    // A section title with a chevron (opens the category) and its description.
    sealed class SectionHeader : PaintedControl
    {
        readonly string title, description;

        public SectionHeader(string title, string description)
        {
            this.title = title;
            this.description = description;
            Cursor = Cursors.Hand;
            Height = Px(22) + Px(2) + Theme.Footnote.Height;
        }

        protected override void Draw(Graphics g)
        {
            var flags = TextFormatFlags.NoPadding;
            Size t = TextRenderer.MeasureText(g, title, Theme.SectionTitle, Size.Empty, flags);
            TextRenderer.DrawText(g, title, Theme.SectionTitle, Point.Empty, Theme.Label, flags);
            TextRenderer.DrawText(g, "›", Theme.SectionTitle, new Point(t.Width + Px(6), 0), Theme.Label2, flags);
            TextRenderer.DrawText(g, description, Theme.Footnote, new Rectangle(0, Px(24), Width, Theme.Footnote.Height + Px(2)),
                Theme.Label2, flags | TextFormatFlags.EndEllipsis);
        }
    }

    // A source-list row: a blue line icon and a label; the selection is a subtle fill (§8.11).
    // As a toggle it shows a switch on the right instead.
    sealed class SidebarItem : PaintedControl
    {
        public readonly char Glyph;
        public bool Selected, Toggle, On;
        bool hover;

        public SidebarItem(char glyph, string text)
        {
            Glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
        }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (Selected) Theme.FillRound(g, Theme.Selection, r, Px(Theme.RadiusSmall));
            else if (hover) Theme.FillRound(g, Color.FromArgb(Theme.Selection.A / 2, Theme.Selection), r, Px(Theme.RadiusSmall));

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Glyph.ToString(), Theme.Icons, new Rectangle(Px(12), 0, Px(20), Height), Theme.Link, flags);
            TextRenderer.DrawText(g, Text, Selected ? Theme.Font(14, 600) : Theme.Font(14),
                new Rectangle(Px(42), 0, Width - Px(46) - (Toggle ? Px(44) : 0), Height), Theme.Label, flags | TextFormatFlags.EndEllipsis);

            if (Toggle) // Apple switch: green-free, blue when on (macOS uses the accent color)
            {
                var track = new RectangleF(Width - Px(44), (Height - Px(20)) / 2f, Px(34), Px(20));
                Theme.FillRound(g, On ? Theme.PrimaryFill : Theme.Fill, track, track.Height / 2);
                float d = track.Height - Px(4);
                using (var knob = new SolidBrush(Color.White))
                    g.FillEllipse(knob, On ? track.Right - d - Px(2) : track.Left + Px(2), track.Top + Px(2), d, d);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // One row of a grouped inset list on the Installed page: a widget that was turned off (Try Again)
    // or one with a newer approved version (Update). The rest is on right-click.
    sealed class PluginRow : PaintedControl
    {
        readonly PluginInfo plugin;
        readonly bool update;
        readonly PillButton button;
        public bool First, Last;

        public PluginRow(PluginInfo plugin, bool update)
        {
            this.plugin = plugin;
            this.update = update;
            Height = Px(64);
            button = new PillButton(update ? "Update" : "Try Again", ButtonStyle.Tinted);
            button.Click += delegate { Act(); };
            Controls.Add(button);
        }

        // Updates are never installed on their own: Update goes through the same check and prompt.
        void Act()
        {
            if (update) Community.Install(plugin.Id, plugin.UpdateVersion);
            else Plugins.TryAgain(plugin);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (button == null) return; // setting the size in the constructor lays out before the button exists
            button.Location = new Point(Width - Px(16) - button.Width, (Height - button.Height) / 2);
        }

        public override Color BackColor { get { return Theme.Card; } set { } } // children clear to the row color

        protected override void Draw(Graphics g)
        {
            float rad = Px(Theme.RadiusField);
            var r = new RectangleF(0, First ? 0 : -rad, Width, Height + (First ? 0 : rad) + (Last ? 0 : rad));
            g.SetClip(new Rectangle(0, 0, Width, Height));
            Theme.FillRound(g, Theme.Card, r, rad);
            if (!Last)
                using (var pen = new Pen(Theme.Separator, 1f)) g.DrawLine(pen, Px(16), Height - 1, Width, Height - 1);
            int textW = Width - Px(32) - button.Width - Px(16);
            string name = Plugins.DisplayName(plugin);
            string title = update ? name + " " + plugin.UpdateVersion + " is available" : name + " is turned off";
            string detail = update ? "You have version " + plugin.Version + ". Updates are new code, so they’re never installed on their own."
                                   : plugin.Disabled ?? "";
            TextRenderer.DrawText(g, title, Theme.BodyEmphasized,
                new Rectangle(Px(16), Px(12), textW, Px(22)), Theme.Label, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, detail, Theme.Footnote,
                new Rectangle(Px(16), Px(36), textW, Px(18)), Theme.Label2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right) return;
            var menu = new PopupMenu();
            menu.Add(update ? "Update" : "Try Again", Act);
            menu.Add("Show Source", delegate { AlertDialog.ShowSource(Plugins.DisplayName(plugin), plugin.SourcePath); });
            menu.AddSeparator();
            menu.AddDestructive("Uninstall…", delegate { App.ConfirmUninstall(plugin); });
            menu.Show(Cursor.Position);
        }
    }

    // A community widget in Explore: its preview, name, author and category, and a Get button
    // (Update when a newer version than yours is out, Add when you have it).
    sealed class ExploreCard : PaintedControl
    {
        readonly Community.Listing listing;
        readonly Bitmap preview; // may be null
        readonly PillButton button;
        bool hover;

        public ExploreCard(Community.Listing listing, Bitmap preview)
        {
            this.listing = listing;
            this.preview = preview;
            Size = new Size(Px(176), Px(240));
            button = new PillButton("Get", ButtonStyle.Tinted);
            button.Click += delegate { Act(); };
            Controls.Add(button);
            RefreshState();
        }

        PluginInfo Installed { get { return Plugins.Get(listing.Id); } }

        public void RefreshState()
        {
            PluginInfo p = Installed;
            button.Text = p == null ? "Get" : p.Version < listing.Version ? "Update" : "Add";
            button.Invalidate();
        }

        void Act()
        {
            PluginInfo p = Installed;
            if (p != null && p.Version >= listing.Version && p.Type != null) App.Current.AddWidget(p.Type, null);
            else Community.Install(listing.Id, listing.Version);
        }

        // Untrusted image data: size-capped, decoded into a copy, and dropped if it's odd in any way.
        public static Bitmap DecodePreview(string base64)
        {
            if (string.IsNullOrEmpty(base64) || base64.Length > 140000) return null;
            try
            {
                using (var ms = new System.IO.MemoryStream(Convert.FromBase64String(base64)))
                using (var img = Image.FromStream(ms, false, true))
                {
                    if (img.Width < 1 || img.Height < 1 || img.Width > 1024 || img.Height > 1024) return null;
                    return new Bitmap(img);
                }
            }
            catch (Exception) { return null; }
        }

        public override Color BackColor { get { return hover ? Theme.CardHover : Theme.Card; } set { } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (button == null) return; // setting the size in the constructor lays out before the button exists
            button.Location = new Point(Width - Px(12) - button.Width, Height - Px(12) - button.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Clear to the page, not to this card's own color.
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(e.Graphics);
        }

        protected override void Draw(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int pad = Px(12), box = Px(176) - pad * 2;
            Theme.FillRound(g, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), Px(Theme.RadiusCard));
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (preview != null)
            {
                float scale = Math.Min(box / (float)preview.Width, box / (float)preview.Height);
                int pw = (int)(preview.Width * scale), ph = (int)(preview.Height * scale);
                g.DrawImage(preview, new Rectangle((Width - pw) / 2, pad + (box - ph) / 2, pw, ph));
            }
            else
                TextRenderer.DrawText(g, "No Preview", Theme.Footnote, new Rectangle(pad, pad, box, box), Theme.Label2, BackColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int textTop = pad + box + Px(8), lineW = Width - pad * 2;
            TextRenderer.DrawText(g, listing.Name, Theme.BodyEmphasized, new Rectangle(pad, textTop, lineW, Px(20)), Theme.Label, BackColor, flags);
            string category = WidgetRegistry.Categories.Where(c => c.Id == listing.Category).Select(c => c.Name).FirstOrDefault() ?? "";
            TextRenderer.DrawText(g, listing.Author + (category.Length > 0 ? " · " + category : ""), Theme.Footnote,
                new Rectangle(pad, textTop + Px(21), lineW - button.Width - Px(8), Px(18)), Theme.Label2, BackColor, flags);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); button.Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return; // moved onto the button
            hover = false; Invalidate(); button.Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right) return;
            var menu = new PopupMenu();
            menu.Add(button.Text == "Add" ? "Add to Desktop" : button.Text, Act);
            menu.Add("Open on the Website ↗", delegate
            {
                try { System.Diagnostics.Process.Start(Community.Website + "widget.html?id=" + listing.Id); }
                catch (Exception) { }
            });
            menu.Show(Cursor.Position);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && preview != null) preview.Dispose();
            base.Dispose(disposing);
        }
    }

    // One widget: its live preview, name and description. Drag or double-click to add; right-click
    // for the rest.
    sealed class WidgetCard : PaintedControl
    {
        readonly WidgetType type;
        readonly Bitmap preview;   // rendered at the real widget size, reused for the drag ghost
        bool hover, dragging;
        Point downAt;
        DragGhost ghost;

        public WidgetCard(WidgetType type, Bitmap preview)
        {
            this.type = type;
            this.preview = preview;
            // Wide widgets (medium, 364 x 170) get a card two columns wide, with the same gap as the grid.
            bool wide = preview.Width > preview.Height * 1.5f;
            Size = new Size(Px(wide ? 176 * 2 + 20 : 176), Px(240));
            Cursor = Cursors.Hand;
        }

        protected override void Draw(Graphics g)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int pad = Px(12);
            Theme.FillRound(g, hover ? Theme.CardHover : Theme.Card, new RectangleF(0, 0, Width - 1, Height - 1), Px(Theme.RadiusCard));

            int boxW = Width - pad * 2, box = Px(176) - pad * 2;
            float scale = Math.Min(1f, Math.Min(boxW / (float)preview.Width, box / (float)preview.Height));
            int pw = (int)(preview.Width * scale), ph = (int)(preview.Height * scale);
            g.DrawImage(preview, new Rectangle((Width - pw) / 2, pad + (box - ph) / 2, pw, ph));

            int textTop = pad + box + Px(8), lineW = Width - pad * 2;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            Color bg = hover ? Theme.CardHover : Theme.Card;
            TextRenderer.DrawText(g, type.Name, Theme.BodyEmphasized, new Rectangle(pad, textTop, lineW, Px(20)), Theme.Label, bg, flags);
            TextRenderer.DrawText(g, type.Description, Theme.Footnote, new Rectangle(pad, textTop + Px(21), lineW, Px(18)), Theme.Label2, bg, flags);
            int count = App.Current.CountOf(type.Id);
            if (count > 0)
                TextRenderer.DrawText(g, count + " on desktop", Theme.Footnote, new Rectangle(pad, textTop + Px(39), lineW, Px(18)),
                    Theme.Label2, bg, flags);
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
            if (e.Button == MouseButtons.Right) ShowMenu();
            else Drop();
        }

        void ShowMenu()
        {
            var menu = new PopupMenu();
            menu.Add("Add to Desktop", delegate { App.Current.AddWidget(type, null); });
            PluginInfo plugin = type.PluginId != null ? Plugins.Get(type.PluginId) : null;
            if (plugin != null)
            {
                menu.Add("Show Source", delegate { AlertDialog.ShowSource(Plugins.DisplayName(plugin), plugin.SourcePath); });
                menu.AddSeparator();
                menu.AddDestructive("Uninstall…", delegate { App.ConfirmUninstall(plugin); });
            }
            menu.Show(Cursor.Position);
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
