using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskWidgets
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (new Mutex(true, "DeskWidgets.SingleInstance", out first))
            using (var showLibrary = new EventWaitHandle(false, EventResetMode.AutoReset, "DeskWidgets.ShowLibrary"))
            {
                // Launching the exe again just opens the library in the running instance.
                if (!first)
                {
                    Native.AllowSetForegroundWindow(-1); // let the running instance bring its window forward
                    showLibrary.Set();
                    return;
                }
                Native.SetProcessDPIAware();
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) WidgetWindow.Dpi = g.DpiX / 96f;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App(showLibrary));
            }
        }
    }

    // Owns the tray icon, the config file and all widget windows (one process for all widgets).
    sealed class App : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DeskWidgets";

        public static App Current { get; private set; }
        public static readonly Icon AppIcon = MakeTrayIcon();

        public event Action WidgetsChanged;

        readonly Config config;
        readonly NotifyIcon tray;
        readonly List<WidgetWindow> widgets = new List<WidgetWindow>();
        readonly MessageWindow messageWindow;
        readonly RegisteredWaitHandle showLibraryWait;
        LibraryWindow library;

        public App(EventWaitHandle showLibrary)
        {
            Current = this;
            config = Config.Load(ConfigPath());
            OpenAll();

            var trayMenu = new ContextMenuStrip();
            trayMenu.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                trayMenu.Items.Clear();
                FillAppMenu(trayMenu.Items);
                e.Cancel = false;
            };
            tray = new NotifyIcon { Icon = AppIcon, Text = "Desk Widgets", ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenLibrary(); };

            if (StartWithWindows) StartWithWindows = true; // exe may have moved: refresh the path
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            messageWindow = new MessageWindow(ReopenAll, OpenLibrary);
            showLibraryWait = ThreadPool.RegisterWaitForSingleObject(showLibrary,
                delegate { messageWindow.PostOpenLibrary(); }, null, Timeout.Infinite, false);

            if (config.Sections.Count == 0) OpenLibrary(); // first run on this machine
        }

        // Portable mode if a widgets.ini sits next to the exe (e.g. on a USB stick),
        // otherwise per-user settings in %APPDATA%\DeskWidgets.
        static string ConfigPath()
        {
            string portable = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "widgets.ini");
            if (File.Exists(portable)) return portable;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskWidgets", "widgets.ini");
        }

        public void OpenLibrary()
        {
            if (library != null)
            {
                if (library.WindowState == FormWindowState.Minimized) library.WindowState = FormWindowState.Normal;
                library.Activate();
                return;
            }
            library = new LibraryWindow();
            library.FormClosed += delegate
            {
                library.Dispose();
                library = null;
                GC.Collect(); // hand the previews' memory back right away
            };
            library.Show();
            library.Activate();
        }

        public int CountOf(string typeId)
        {
            return widgets.Count(w => w.Settings.Type == typeId);
        }

        void RaiseWidgetsChanged()
        {
            if (WidgetsChanged != null) WidgetsChanged();
        }

        void OpenAll()
        {
            foreach (var section in config.Sections.ToArray())
            {
                var type = WidgetRegistry.Find(section.Type);
                if (type == null) continue;
                var w = type.Create(section);
                widgets.Add(w);
                w.Show();
            }
        }

        // Explorer restarted: our windows were owned by the old desktop window, so rebuild them.
        void ReopenAll()
        {
            foreach (var w in widgets) w.Dispose();
            widgets.Clear();
            OpenAll();
            RaiseWidgetsChanged();
        }

        // Adds a widget centred on 'center' (screen coordinates), or mid-screen when null.
        public void AddWidget(WidgetType type, Point? center)
        {
            var section = config.Add(type.Id);
            if (type.Defaults != null) type.Defaults(section);
            var w = type.Create(section);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Point c = center ?? new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
            Size sz = w.WidgetSize;
            w.MoveTo(new Point(c.X - sz.Width / 2, c.Y - sz.Height / 2));
            widgets.Add(w);
            w.Show();
            RaiseWidgetsChanged();
        }

        public void RemoveWidget(WidgetWindow w)
        {
            widgets.Remove(w);
            config.Remove(w.Settings);
            w.Close();
            RaiseWidgetsChanged();
        }

        public void FillAppMenu(ToolStripItemCollection items)
        {
            var open = new ToolStripMenuItem("Widget library…", null, delegate { OpenLibrary(); });
            open.Font = new Font(open.Font, FontStyle.Bold);
            items.Add(open);
            var startup = new ToolStripMenuItem("Start with Windows", null, delegate { StartWithWindows = !StartWithWindows; });
            startup.Checked = StartWithWindows;
            items.Add(startup);
            items.Add(new ToolStripSeparator());
            items.Add("Exit Desk Widgets", null, delegate { Exit(); });
        }

        static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        // Stop all drawing while the PC is locked.
        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionLock && e.Reason != SessionSwitchReason.SessionUnlock) return;
            bool locked = e.Reason == SessionSwitchReason.SessionLock;
            foreach (var w in widgets) w.SetPaused(locked);
        }

        // Resolution/monitor changed: re-apply each widget's relative position.
        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            foreach (var w in widgets) w.Reposition();
        }

        void Exit()
        {
            if (library != null) library.Close();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            showLibraryWait.Unregister(null);
            messageWindow.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            foreach (var w in widgets) w.Dispose();
            widgets.Clear();
            ExitThread();
        }

        static Icon MakeTrayIcon()
        {
            int s = 32;
            using (var bmp = new Bitmap(s, s))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(Brushes.White, 1, 1, s - 2, s - 2);
                using (var pen = new Pen(Color.FromArgb(28, 28, 30), 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawEllipse(pen, 2.5f, 2.5f, s - 5, s - 5);
                    g.DrawLine(pen, s / 2f, s / 2f, s / 2f, 8);
                    g.DrawLine(pen, s / 2f, s / 2f, s - 10, s / 2f);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        // Hidden top-level window: hears the "TaskbarCreated" broadcast after Explorer restarts,
        // and brings "open the library" requests from other threads onto the UI thread.
        sealed class MessageWindow : NativeWindow
        {
            const int WM_OPEN_LIBRARY = 0x8001; // WM_APP + 1
            readonly int taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            readonly Action onExplorerRestart, onOpenLibrary;

            public MessageWindow(Action onExplorerRestart, Action onOpenLibrary)
            {
                this.onExplorerRestart = onExplorerRestart;
                this.onOpenLibrary = onOpenLibrary;
                CreateHandle(new CreateParams());
            }

            public void PostOpenLibrary()
            {
                Native.PostMessage(Handle, WM_OPEN_LIBRARY, IntPtr.Zero, IntPtr.Zero);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == taskbarCreated) onExplorerRestart();
                else if (m.Msg == WM_OPEN_LIBRARY) onOpenLibrary();
                base.WndProc(ref m);
            }
        }
    }
}
