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
        const string InstalledPage = "@installed", ExplorePage = "@explore", SettingsPage = "@settings";

        sealed class Section
        {
            public WidgetCategory Category;
            public SectionHeader Header;
            public List<WidgetCard> Cards = new List<WidgetCard>();
        }

        readonly SmoothPanel content;
        readonly Panel sidebar;
        readonly PageHeader header;
        readonly PillButton installButton;
        readonly EmptyState noneInstalled, exploreState;
        readonly PillButton uploadButton;
        readonly List<ExploreCard> exploreCards = new List<ExploreCard>();
        readonly List<PluginRow> updates = new List<PluginRow>();
        bool exploreLoading;
        readonly List<Section> sections = new List<Section>();
        readonly List<WidgetCard> installedCards = new List<WidgetCard>();
        readonly List<PluginRow> turnedOff = new List<PluginRow>();
        readonly List<SidebarItem> nav = new List<SidebarItem>();
        readonly SidebarItem settings, account;
        SidebarItem startupToggle, highPriorityToggle;
        Label settingsAppearanceLabel;
        SidebarItem[] themeItems;
        PillButton uninstallButton;
        string page; // category id, InstalledPage, SettingsPage, or null for all widgets

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

            content = new SmoothPanel { Dock = DockStyle.Fill, BackColor = Theme.WindowBg };
            content.HandleCreated += delegate { Theme.ApplyToScrollbars(content.Handle); };
            header = new PageHeader();
            content.Controls.Add(header);
            installButton = new PillButton("Install from File…", ButtonStyle.Tinted);
            installButton.Click += delegate { App.InstallFromFile(); };
            content.Controls.Add(installButton);
            noneInstalled = new EmptyState("No Installed Widgets", "Widgets you install from a file appear here.");
            content.Controls.Add(noneInstalled);
            uploadButton = new PillButton("Upload a Widget…", ButtonStyle.Tinted);
            uploadButton.Click += delegate { App.UploadWidget(null); };
            content.Controls.Add(uploadButton);
            exploreState = new EmptyState("", "");
            content.Controls.Add(exploreState);
            BuildCards();
            content.Resize += delegate { LayoutContent(); };

            sidebar = new Panel { Dock = DockStyle.Left, Width = Px(260), BackColor = Theme.SidebarBg };
            BuildNav();
            settings = new SidebarItem('', "Settings");
            settings.SetBounds(Px(12), sidebar.Height - Px(50), sidebar.Width - Px(24), Px(34));
            settings.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            settings.Click += delegate { ShowPage(SettingsPage); };
            sidebar.Controls.Add(settings);
            BuildSettingsControls();
            // The account sits at the bottom of the sidebar (§8.11): your photo and name, or Sign In.
            account = new SidebarItem('\uE77B', "");
            account.SetBounds(Px(12), sidebar.Height - Px(88), sidebar.Width - Px(24), Px(34));
            account.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            account.Click += delegate { AccountClicked(); };
            sidebar.Controls.Add(account);
            UpdateAccount();
            if (Account.SignedIn && Account.Photo == null) System.Threading.ThreadPool.QueueUserWorkItem(delegate { Account.LoadProfile(); });

            Controls.Add(content);
            Controls.Add(sidebar);
            App.Current.WidgetsChanged += OnWidgetsChanged;
            WidgetRegistry.Changed += OnRegistryChanged;
            Account.Changed += UpdateAccount;
            Theme.Changed += OnThemeChanged;
            ShowPage(null);
        }

        static int Px(float v) { return WidgetWindow.Px(v); }

        public void OpenSettings() { ShowPage(SettingsPage); }

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
            foreach (var c in sidebar.Controls.Cast<Control>().Where(c => c != settings && c != account).ToArray()) { sidebar.Controls.Remove(c); c.Dispose(); }
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
            settings.Selected = target == SettingsPage;
            settings.Invalidate();
            var cat = sections.Select(s => s.Category).FirstOrDefault(c => c.Id == target);
            if (target == SettingsPage)
                header.Set("Settings", "");
            else if (target == ExplorePage)
            {
                header.Set("Explore", "Widgets made by the community. Each one is reviewed before it appears here.");
                if (!exploreLoading) LoadExplore(); // every visit, so newly approved widgets show up
            }
            else if (target == InstalledPage)
                header.Set("Installed", "Widgets you installed. Drag one onto your desktop to use it.");
            else if (cat != null)
                header.Set(cat.Name, cat.Description + ". Drag a widget onto your desktop, or double-click to add it.");
            else
                header.Set("All Widgets", "Drag a widget onto your desktop, or double-click to add it.");
            content.ScrollToTop();
            LayoutContent();
        }

        // ---- layout: header, then wrapping grids of cards ----

        void LayoutContent()
        {
            int pad = Px(32), gap = Px(20);
            int width = Math.Max(Px(200), content.ClientSize.Width - pad * 2);
            int y = Px(28);
            content.SuspendLayout();
            bool installed = page == InstalledPage, explore = page == ExplorePage, settingsOpen = page == SettingsPage;

            PillButton action = installed ? installButton : explore ? uploadButton : null;
            header.Width = width - (action != null ? action.Width + gap : 0);
            header.Location = new Point(pad, y);
            installButton.Visible = installed;
            uploadButton.Visible = explore;
            if (action != null) action.Location = new Point(pad + width - action.Width, y + Px(6));
            y += header.Height + Px(24);

            // Settings page controls.
            startupToggle.Visible = settingsOpen;
            highPriorityToggle.Visible = settingsOpen;
            settingsAppearanceLabel.Visible = settingsOpen;
            uninstallButton.Visible = settingsOpen;
            foreach (var t in themeItems) t.Visible = settingsOpen;
            if (settingsOpen) y = LayoutSettings(pad, width, y);

            foreach (var card in exploreCards) card.Visible = explore;
            exploreState.Visible = explore && exploreCards.Count == 0;
            if (exploreState.Visible)
            {
                exploreState.SetBounds(pad, y + Px(80), width, exploreState.Height);
                y += Px(80) + exploreState.Height;
            }
            if (explore) y = Grid(exploreCards, pad, width, y) + Px(32);

            foreach (var sec in sections)
            {
                bool show = page == null || sec.Category.Id == page;
                sec.Header.Visible = show && page == null;
                foreach (var card in sec.Cards) card.Visible = show;
                if (!show) continue;
                if (page == null)
                {
                    sec.Header.SetBounds(pad, y, width, sec.Header.Height);
                    y += sec.Header.Height + Px(12);
                }
                y = Grid(sec.Cards, pad, width, y) + Px(32);
            }

            foreach (var card in installedCards) card.Visible = installed;
            foreach (var row in turnedOff) row.Visible = installed;
            foreach (var row in updates) row.Visible = installed;
            noneInstalled.Visible = installed && installedCards.Count == 0 && turnedOff.Count == 0;
            if (noneInstalled.Visible)
            {
                noneInstalled.SetBounds(pad, y + Px(80), width, noneInstalled.Height);
                y += Px(80) + noneInstalled.Height;
            }
            if (installed)
            {
                y = Group(updates, pad, width, y);
                if (installedCards.Count > 0) y = Grid(installedCards, pad, width, y) + Px(32);
                y = Group(turnedOff, pad, width, y);
            }
            content.ContentHeight = y + Px(32);
            content.ResumeLayout();
        }

        // Cards flow left to right and wrap; medium widgets have double-width cards.
        int Grid<T>(List<T> cards, int pad, int width, int y) where T : Control
        {
            if (cards.Count == 0) return y;
            int gap = Px(20), x = pad, h = cards[0].Height;
            foreach (var card in cards)
            {
                if (x > pad && x + card.Width > pad + width) { x = pad; y += h + gap; }
                card.Location = new Point(x, y);
                x += card.Width + gap;
            }
            return y + h;
        }

        // Rows stacked into one grouped inset list.
        int Group(List<PluginRow> rows, int pad, int width, int y)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].First = i == 0;
                rows[i].Last = i == rows.Count - 1;
                rows[i].SetBounds(pad, y, width, rows[i].Height);
                y += rows[i].Height;
            }
            return rows.Count > 0 ? y + Px(32) : y;
        }

        // ---- Settings page ----

        void BuildSettingsControls()
        {
            startupToggle = new SidebarItem('', "Start with Windows") { Toggle = true, On = App.StartWithWindows };
            startupToggle.Click += delegate
            {
                App.StartWithWindows = !App.StartWithWindows;
                startupToggle.On = App.StartWithWindows;
                startupToggle.Invalidate();
            };
            content.Controls.Add(startupToggle);

            highPriorityToggle = new SidebarItem('', "High Priority") { Toggle = true, On = App.Current.HighPriority };
            highPriorityToggle.Click += delegate
            {
                App.Current.HighPriority = !App.Current.HighPriority;
                highPriorityToggle.On = App.Current.HighPriority;
                highPriorityToggle.Invalidate();
            };
            content.Controls.Add(highPriorityToggle);

            settingsAppearanceLabel = new Label { Text = "Appearance", Font = Theme.CaptionEmphasized, ForeColor = Theme.Label2, AutoSize = true };
            content.Controls.Add(settingsAppearanceLabel);

            string[] labels = { "System", "Light", "Dark" };
            AppTheme[] values = { AppTheme.System, AppTheme.Light, AppTheme.Dark };
            themeItems = new SidebarItem[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                bool active = App.Current.AppThemeChoice == values[i];
                var item = new SidebarItem(active ? '' : '\0', labels[i]) { Selected = active };
                item.Click += delegate
                {
                    App.Current.AppThemeChoice = values[idx];
                    for (int j = 0; j < themeItems.Length; j++)
                    {
                        bool on = j == idx;
                        themeItems[j].Selected = on;
                        themeItems[j].Glyph = on ? '' : '\0';
                        themeItems[j].Invalidate();
                    }
                };
                content.Controls.Add(item);
                themeItems[i] = item;
            }

            uninstallButton = new PillButton("Uninstall Desk Widgets", ButtonStyle.Destructive);
            uninstallButton.Click += delegate { App.Current.UninstallApp(); };
            content.Controls.Add(uninstallButton);
        }

        int LayoutSettings(int pad, int width, int y)
        {
            int rowW = Math.Min(width, Px(500));

            startupToggle.SetBounds(pad, y, rowW, Px(44));
            y += Px(44);
            highPriorityToggle.SetBounds(pad, y, rowW, Px(44));
            y += Px(44) + Px(24);

            settingsAppearanceLabel.Location = new Point(pad, y);
            y += settingsAppearanceLabel.Height + Px(8);
            for (int i = 0; i < themeItems.Length; i++)
            {
                themeItems[i].SetBounds(pad, y, rowW, Px(38));
                y += Px(38);
            }
            y += Px(24);

            uninstallButton.Location = new Point(pad, y);
            y += uninstallButton.Height + Px(16);

            return y;
        }

        // ---- Explore ----

        void LoadExplore()
        {
            if (!Community.Configured)
            {
                exploreState.Set("Explore Isn’t Connected Yet", "Community widgets appear here once the gallery is online.");
                return;
            }
            exploreLoading = true;
            // The cards from the last visit stay up while the list reloads.
            if (exploreCards.Count == 0) exploreState.Set("Loading…", "");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<Community.Listing> list = null;
                var previews = new Dictionary<Community.Listing, Bitmap>();
                try
                {
                    list = Community.Explore();
                    foreach (var l in list) previews[l] = Community.Preview(l); // disk cache after the first time
                    Community.PrunePreviews(list);
                }
                catch (Exception) { list = null; }
                App.RunOnUi(delegate
                {
                    exploreLoading = false;
                    if (IsDisposed) { foreach (var b in previews.Values) if (b != null) b.Dispose(); return; }
                    if (list == null)
                    {
                        // Offline: keep the cards from the last visit if there are any.
                        if (exploreCards.Count == 0)
                            exploreState.Set("Explore Couldn’t Load", "Check your connection, then open Explore again.");
                    }
                    else
                    {
                        foreach (var old in exploreCards) { content.Controls.Remove(old); old.Dispose(); }
                        exploreCards.Clear();
                        if (list.Count == 0) exploreState.Set("Nothing Here Yet", "Reviewed community widgets will appear here.");
                        foreach (var l in list)
                        {
                            var c = new ExploreCard(l, previews[l]);
                            exploreCards.Add(c);
                            content.Controls.Add(c);
                        }
                    }
                    LayoutContent();
                });
            });
        }

        // ---- account ----

        void UpdateAccount()
        {
            if (IsDisposed) return;
            account.Text = Account.SignedIn ? Account.Username : "Sign In";
            account.Photo = Account.SignedIn ? Account.Photo : null;
            account.Invalidate();
        }

        void AccountClicked()
        {
            if (!Account.SignedIn) { App.SignIn(null); return; }
            var menu = new PopupMenu();
            menu.Add("Upload a Widget…", delegate { App.UploadWidget(null); });
            menu.Add("Your Uploads ↗", delegate { App.OpenWebsite("upload.html#uploads"); });
            menu.Add("Edit Profile ↗", delegate { App.OpenWebsite("account.html"); });
            if (Account.IsAdmin) menu.Add("Admin Console ↗", delegate { App.OpenWebsite("admin.html"); });
            menu.AddSeparator();
            menu.Add("Sign Out", Account.SignOut);
            menu.Show(account.PointToScreen(new Point(0, account.Height)));
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
            ShowPage(page == InstalledPage || page == ExplorePage || page == SettingsPage || sections.Exists(s => s.Category.Id == page) ? page : null);
        }

        // Windows switched light/dark: recolor everything.
        void OnThemeChanged()
        {
            BackColor = content.BackColor = Theme.WindowBg;
            sidebar.BackColor = Theme.SidebarBg;
            Theme.ApplyToWindow(Handle);
            Theme.ApplyToScrollbars(content.Handle);
            settingsAppearanceLabel.ForeColor = Theme.Label2;
            BuildNav();
            ShowPage(page);
            foreach (Control c in content.Controls) c.Invalidate();
            settings.Invalidate();
            account.Invalidate();
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
            Account.Changed -= UpdateAccount;
            Theme.Changed -= OnThemeChanged;
            base.OnFormClosed(e);
        }
    }

    // Base for the library's painted parts: double-buffered, cleared to the parent's color.
    // Subclasses that want smooth hover transitions call AnimateHover(true/false) from their mouse
    // handlers and read HoverBlend (0..1) in Draw. A single shared timer drives all active fades.
    abstract class PaintedControl : Control
    {
        static readonly Timer hoverAnim = new Timer { Interval = 12 };
        static readonly List<PaintedControl> hoverQueue = new List<PaintedControl>();
        protected float HoverBlend;
        bool hoverTarget;

        static PaintedControl()
        {
            hoverAnim.Tick += delegate
            {
                for (int i = hoverQueue.Count - 1; i >= 0; i--)
                {
                    var c = hoverQueue[i];
                    float to = c.hoverTarget ? 1f : 0f;
                    float diff = to - c.HoverBlend;
                    if (Math.Abs(diff) < 0.02f)
                    {
                        c.HoverBlend = to;
                        hoverQueue.RemoveAt(i);
                    }
                    else
                        c.HoverBlend += diff * 0.22f;
                    c.Invalidate();
                    foreach (Control child in c.Controls) child.Invalidate();
                }
                if (hoverQueue.Count == 0) hoverAnim.Stop();
            };
        }

        protected PaintedControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected void AnimateHover(bool on)
        {
            hoverTarget = on;
            if (!hoverQueue.Contains(this)) hoverQueue.Add(this);
            hoverAnim.Start();
        }

        protected static int Px(float v) { return WidgetWindow.Px(v); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.WindowBg);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(e.Graphics);
        }

        protected abstract void Draw(Graphics g);

        protected override void Dispose(bool disposing)
        {
            if (disposing) hoverQueue.Remove(this);
            base.Dispose(disposing);
        }
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
        public char Glyph;
        public bool Selected, Toggle, On;
        public Bitmap Photo; // drawn round in place of the glyph (the account row); not owned

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
            else if (HoverBlend > 0f)
            {
                Color sel = Theme.Selection;
                Theme.FillRound(g, Color.FromArgb((int)(sel.A / 2 * HoverBlend), sel), r, Px(Theme.RadiusSmall));
            }

            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            if (Photo != null)
            {
                int d = Px(22);
                using (var clip = new GraphicsPath())
                {
                    clip.AddEllipse(Px(11), (Height - d) / 2, d, d);
                    g.SetClip(clip);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Photo, Px(11), (Height - d) / 2, d, d);
                    g.ResetClip();
                }
            }
            else
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

        protected override void OnMouseEnter(EventArgs e) { AnimateHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { AnimateHover(false); base.OnMouseLeave(e); }
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

    // A community widget in Explore: its preview, name, author and category. Get installs it,
    // Update installs a newer version, and Uninstall removes it.
    sealed class ExploreCard : PaintedControl
    {
        readonly Community.Listing listing;
        readonly Bitmap preview; // may be null
        readonly PillButton button;

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
            bool installed = p != null && p.Version >= listing.Version;
            button.Text = p == null ? "Get" : p.Version < listing.Version ? "Update" : "Uninstall";
            button.Style = installed ? ButtonStyle.Destructive : ButtonStyle.Tinted;
            PerformLayout();
            button.Invalidate();
            Invalidate();
        }

        void Act()
        {
            PluginInfo p = Installed;
            if (p != null && p.Version >= listing.Version) App.ConfirmUninstall(p);
            else Community.Install(listing.Id, listing.Version);
        }

        // Untrusted image data: size-capped, decoded into a copy, and dropped if it's odd in any way.
        public static Bitmap DecodePreview(string base64)
        {
            if (string.IsNullOrEmpty(base64) || base64.Length > 140000) return null;
            try { return Community.DecodePreview(Convert.FromBase64String(base64)); }
            catch (FormatException) { return null; }
        }

        public override Color BackColor { get { return HoverBlend > 0f ? Theme.Blend(Theme.Card, Theme.CardHover, HoverBlend) : Theme.Card; } set { } }

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
            // Installed: a checkmark and, when an update is out, the version you have.
            PluginInfo mine = Installed;
            if (mine != null)
            {
                int y = textTop + Px(40), w = lineW - button.Width - Px(8);
                TextRenderer.DrawText(g, "\uE73E", Theme.Icons, new Rectangle(pad, y, Px(14), Px(18)), Theme.Label2, BackColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, mine.Version < listing.Version ? "Installed · v" + mine.Version : "Installed", Theme.Footnote,
                    new Rectangle(pad + Px(17), y, w - Px(17), Px(18)), Theme.Label2, BackColor, flags | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { AnimateHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (ClientRectangle.Contains(PointToClient(Cursor.Position))) return; // moved onto the button
            AnimateHover(false); base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right) return;
            var menu = new PopupMenu();
            PluginInfo p = Installed;
            bool installed = p != null && p.Version >= listing.Version;
            if (!installed)
                menu.Add(p != null ? "Update" : "Get", Act);
            menu.Add("Open on the Website ↗", delegate
            {
                try { System.Diagnostics.Process.Start(Community.Website + "widget.html?id=" + listing.Id); }
                catch (Exception) { }
            });
            if (installed)
            {
                menu.AddSeparator();
                menu.AddDestructive("Uninstall…", Act);
            }
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
        bool dragging;
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
            Color bg = HoverBlend > 0f ? Theme.Blend(Theme.Card, Theme.CardHover, HoverBlend) : Theme.Card;
            Theme.FillRound(g, bg, new RectangleF(0, 0, Width - 1, Height - 1), Px(Theme.RadiusCard));

            int boxW = Width - pad * 2, box = Px(176) - pad * 2;
            float scale = Math.Min(1f, Math.Min(boxW / (float)preview.Width, box / (float)preview.Height));
            int pw = (int)(preview.Width * scale), ph = (int)(preview.Height * scale);
            g.DrawImage(preview, new Rectangle((Width - pw) / 2, pad + (box - ph) / 2, pw, ph));

            int textTop = pad + box + Px(8), lineW = Width - pad * 2;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, type.Name, Theme.BodyEmphasized, new Rectangle(pad, textTop, lineW, Px(20)), Theme.Label, bg, flags);
            TextRenderer.DrawText(g, type.Description, Theme.Footnote, new Rectangle(pad, textTop + Px(21), lineW, Px(18)), Theme.Label2, bg, flags);
            int count = App.Current.CountOf(type.Id);
            if (count > 0)
                TextRenderer.DrawText(g, count + " on desktop", Theme.Footnote, new Rectangle(pad, textTop + Px(39), lineW, Px(18)),
                    Theme.Label2, bg, flags);
        }

        protected override void OnMouseEnter(EventArgs e) { AnimateHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { AnimateHover(false); base.OnMouseLeave(e); }

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
                if (plugin.Author == "local") menu.Add("Upload to the Gallery…", delegate { App.UploadWidget(plugin); });
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
