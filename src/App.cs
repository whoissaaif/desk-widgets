using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskWidgets
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --background: started by Windows at sign-in, so restore widgets without opening the window.
            bool background = args.Contains("--background");
            // deskwidgets://install/<id>/<version>, opened from the website (Community.HandleUrl checks it).
            string url = args.FirstOrDefault(a => a.StartsWith("deskwidgets:", StringComparison.OrdinalIgnoreCase));
            bool first;
            using (new Mutex(true, "DeskWidgets.SingleInstance", out first))
            using (var showLibrary = new EventWaitHandle(false, EventResetMode.AutoReset, "DeskWidgets.ShowLibrary"))
            {
                // Launching the app again (Start menu, exe) just opens the window of the running instance.
                if (!first)
                {
                    if (url != null)
                    {
                        Native.AllowSetForegroundWindow(-1); // so its install prompt can come forward
                        App.SendUrlToRunningInstance(url);
                    }
                    else if (!background)
                    {
                        Native.AllowSetForegroundWindow(-1); // let the running instance bring its window forward
                        showLibrary.Set();
                    }
                    return;
                }
                Native.SetProcessDPIAware();
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) WidgetWindow.Dpi = g.DpiX / 96f;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // A crash in a community widget turns that widget off instead of taking the app down.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                Application.Run(new App(showLibrary, !background && url == null, url));
            }
        }

        static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            string plugin = Plugins.FromException(e.Exception);
            if (plugin != null) { Plugins.Crashed(plugin, e.Exception); return; }
            // Not a plugin: same choice WinForms offers without a handler.
            using (var d = new ThreadExceptionDialog(e.Exception))
                if (d.ShowDialog() == DialogResult.Abort) Environment.Exit(1);
        }

        static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            string plugin = ex != null ? Plugins.FromException(ex) : null;
            if (plugin != null) Plugins.CrashedFatally(plugin, ex);
        }
    }

    // Owns the tray icon, the config file and all widget windows (one process for all widgets).
    sealed class App : ApplicationContext
    {
        const string AppName = "Desk Widgets";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DeskWidgets";

        public static App Current { get; private set; }
        // Folder holding widgets.ini and caches (AppData, or next to the exe in portable mode).
        public static string DataDir { get; private set; }
        public static readonly Icon AppIcon = LoadIcon(Size.Empty);

        public event Action WidgetsChanged;

        // Events published by Calendar widgets, for widgets that show them too (Reminders).
        public event Action CalendarEventsChanged;
        readonly Dictionary<WidgetWindow, List<CalEvent>> calendarFeeds = new Dictionary<WidgetWindow, List<CalEvent>>();

        public void PublishCalendar(WidgetWindow source, List<CalEvent> events)
        {
            if (events == null) calendarFeeds.Remove(source); else calendarFeeds[source] = events;
            if (CalendarEventsChanged != null) CalendarEventsChanged();
        }

        public List<CalEvent> CalendarEvents { get { return calendarFeeds.Values.SelectMany(e => e).ToList(); } }
        public bool HasCalendar { get { return calendarFeeds.Count > 0; } }

        readonly Config config;
        readonly NotifyIcon tray;
        readonly List<WidgetWindow> widgets = new List<WidgetWindow>();
        readonly MessageWindow messageWindow;
        readonly RegisteredWaitHandle showLibraryWait;
        LibraryWindow library;
        readonly Control invoker = new Control(); // brings background results to the UI thread
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 };

        public App(EventWaitHandle showLibrary, bool openWindow, string url)
        {
            Current = this;
            invoker.CreateControl();
            messageWindow = new MessageWindow(ReopenAll, OpenLibrary, u => Ui.Later(delegate { Community.HandleUrl(u); }));

            string configPath = ConfigPath();
            DataDir = Path.GetDirectoryName(configPath);
            config = Config.Load(configPath);
            Plugins.LoadAll();
            OpenAll();

            tray = new NotifyIcon { Icon = LoadIcon(SystemInformation.SmallIconSize), Text = AppName, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenLibrary();
                else if (e.Button == MouseButtons.Right) ShowAppMenu();
            };

            // Never touch the autostart entry on launch: it only changes when the user toggles it.
            EnsureStartMenuShortcut();
            EnsureUrlProtocol();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            showLibraryWait = ThreadPool.RegisterWaitForSingleObject(showLibrary,
                delegate { messageWindow.PostOpenLibrary(); }, null, Timeout.Infinite, false);

            if (openWindow || config.Sections.Count == 0) OpenLibrary();
            if (url != null) Ui.Later(delegate { Community.HandleUrl(url); }, 300);

            // Newer approved versions of installed community widgets: shortly after start, then daily.
            Ui.Later(Community.CheckForUpdates, 30000);
            updateTimer.Tick += delegate { Community.CheckForUpdates(); };
            updateTimer.Start();
        }

        // Runs on the UI thread (from a background thread, or later when already on it).
        public static void RunOnUi(Action action)
        {
            if (Current != null && Current.invoker.IsHandleCreated) Current.invoker.BeginInvoke(action);
            else action();
        }

        // ---- deskwidgets:// ----

        const string ProtocolKey = @"Software\Classes\deskwidgets";

        // Registers deskwidgets:// for this user (no admin rights). Like the Start menu entry, it's created
        // when missing or when the exe it points to is gone, and never taken over from another copy.
        static void EnsureUrlProtocol()
        {
            try
            {
                string exe = Application.ExecutablePath;
                using (var cmd = Registry.CurrentUser.OpenSubKey(ProtocolKey + @"\shell\open\command"))
                {
                    string current = cmd != null ? cmd.GetValue(null) as string : null;
                    int end = current != null && current.StartsWith("\"") ? current.IndexOf('"', 1) : -1;
                    if (end > 1 && File.Exists(current.Substring(1, end - 1))) return;
                }
                using (var k = Registry.CurrentUser.CreateSubKey(ProtocolKey))
                {
                    k.SetValue(null, "URL:Desk Widgets");
                    k.SetValue("URL Protocol", "");
                    using (var icon = k.CreateSubKey("DefaultIcon")) icon.SetValue(null, "\"" + exe + "\",0");
                    using (var cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, "\"" + exe + "\" \"%1\"");
                }
            }
            catch (Exception) { } // without it, Get on the website does nothing; not worth failing startup
        }

        // A second copy was started with a URL: hand it to the running one, which shows the prompt.
        public static void SendUrlToRunningInstance(string url)
        {
            if (url.Length > 512) return;
            IntPtr target = IntPtr.Zero;
            for (int i = 0; i < 50 && target == IntPtr.Zero; i++) // the running copy may still be starting
            {
                target = Native.FindWindow(null, MessageWindow.Caption);
                if (target == IntPtr.Zero) Thread.Sleep(100);
            }
            if (target == IntPtr.Zero) return;
            byte[] text = System.Text.Encoding.Unicode.GetBytes(url);
            IntPtr data = Marshal.AllocHGlobal(text.Length);
            IntPtr cds = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.COPYDATASTRUCT)));
            try
            {
                Marshal.Copy(text, 0, data, text.Length);
                Marshal.StructureToPtr(new Native.COPYDATASTRUCT { dwData = (IntPtr)MessageWindow.UrlMessage, cbData = text.Length, lpData = data }, cds, false);
                Native.SendMessage(target, Native.WM_COPYDATA, IntPtr.Zero, cds);
            }
            finally
            {
                Marshal.FreeHGlobal(cds);
                Marshal.FreeHGlobal(data);
            }
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
            foreach (var section in config.Sections.ToArray()) Open(section);
        }

        // Sections of a type that isn't registered (plugin removed or turned off) are kept but not shown.
        WidgetWindow Open(Section section)
        {
            var type = WidgetRegistry.Find(section.Type);
            if (type == null) return null;
            var w = Plugins.Guard(type, () =>
            {
                var created = type.Create(section);
                created.Show(); // first render runs here
                return created;
            });
            if (w != null) widgets.Add(w);
            return w;
        }

        // Closes the windows of one widget type but keeps their settings (plugin turned off or updated).
        public void CloseType(string typeId)
        {
            foreach (var w in widgets.Where(w => w.Settings.Type == typeId).ToArray())
            {
                widgets.Remove(w);
                w.Dispose();
            }
            RaiseWidgetsChanged();
        }

        // Opens every saved widget of a type that isn't on screen yet.
        public void OpenType(string typeId)
        {
            foreach (var section in config.Sections.Where(s => s.Type == typeId).ToArray())
                if (!widgets.Any(w => w.Settings == section)) Open(section);
            RaiseWidgetsChanged();
        }

        // Closes a type's windows and forgets their settings (plugin uninstalled).
        public void RemoveType(string typeId)
        {
            CloseType(typeId);
            foreach (var section in config.Sections.Where(s => s.Type == typeId).ToArray()) config.Remove(section);
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
            if (WidgetRegistry.Find(type.Id) != type) return; // turned off or uninstalled meanwhile
            var section = config.Add(type.Id);
            if (type.Defaults != null) type.Defaults(section);
            var w = Plugins.Guard(type, () => type.Create(section));
            if (w == null) { config.Remove(section); return; }
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Point c = center ?? new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
            Size sz = w.WidgetSize;
            w.MoveTo(new Point(c.X - sz.Width / 2, c.Y - sz.Height / 2));
            if (Plugins.Guard(type, () => { w.Show(); return w; }) == null) { w.Dispose(); config.Remove(section); return; }
            widgets.Add(w);
            RaiseWidgetsChanged();
        }

        public void RemoveWidget(WidgetWindow w)
        {
            widgets.Remove(w);
            config.Remove(w.Settings);
            w.Close();
            RaiseWidgetsChanged();
        }

        void ShowAppMenu()
        {
            var menu = new PopupMenu();
            menu.Add("Open " + AppName, OpenLibrary);
            menu.AddCheck("Start with Windows", StartWithWindows, delegate { StartWithWindows = !StartWithWindows; });
            menu.AddSeparator();
            menu.Add("Quit " + AppName, Exit);
            menu.Show(Cursor.Position);
        }

        public static void InstallFromFile()
        {
            using (var pick = new OpenFileDialog { Title = "Install Widget from File", Filter = "C# widget (*.cs)|*.cs" })
                if (pick.ShowDialog() == DialogResult.OK) Plugins.InstallFromFile(pick.FileName);
        }

        public static void ConfirmUninstall(PluginInfo plugin)
        {
            if (AlertDialog.Confirm("Uninstall " + Plugins.DisplayName(plugin) + "?",
                "Its widgets are removed from your desktop, along with their settings.", "Uninstall"))
                Plugins.Uninstall(plugin);
        }

        static string RunCommand { get { return "\"" + Application.ExecutablePath + "\" --background"; } }

        // On only if the entry starts this exe (an entry left by a moved or other copy shows as off).
        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && string.Equals(k.GetValue(RunName) as string, RunCommand, StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, RunCommand);
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        // Start menu entry (so the app is searchable). Created when missing, or when the exe it
        // pointed to no longer exists (the folder was moved); never taken over by another copy.
        static void EnsureStartMenuShortcut()
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
            string exe = Application.ExecutablePath;
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type t = shortcut.GetType();
                if (File.Exists(lnk) && File.Exists((string)t.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null)))
                    return;
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exe) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Lightweight desktop widgets" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch (Exception) { } // no Start menu entry is not worth failing startup over
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
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
            DesktopBackdrop.Invalidate();
            foreach (var w in widgets) { w.Reposition(); w.BackdropChanged(); }
        }

        // New wallpaper: vibrant widgets re-sample what's behind them.
        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.Desktop) return;
            DesktopBackdrop.Invalidate();
            foreach (var w in widgets) w.BackdropChanged();
        }

        void Exit()
        {
            if (library != null) library.Close();
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            showLibraryWait.Unregister(null);
            messageWindow.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            foreach (var w in widgets) w.Dispose();
            widgets.Clear();
            ExitThread();
        }

        // The app icon is embedded by build.bat (assets\app.ico).
        static Icon LoadIcon(Size size)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeskWidgets.app.ico"))
                return size.IsEmpty ? new Icon(stream) : new Icon(stream, size);
        }

        // Hidden top-level window: owns popup menus, hears the "TaskbarCreated" broadcast after
        // Explorer restarts, and brings "open the window" requests from other threads to the UI thread.
        sealed class MessageWindow : NativeWindow
        {
            public const string Caption = "DeskWidgets.Messages"; // how a second copy finds us
            public const int UrlMessage = 0x4457; // WM_COPYDATA tag: a deskwidgets:// URL
            const int WM_OPEN_LIBRARY = 0x8001; // WM_APP + 1
            readonly int taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            readonly Action onExplorerRestart, onOpenLibrary;
            readonly Action<string> onUrl;

            public MessageWindow(Action onExplorerRestart, Action onOpenLibrary, Action<string> onUrl)
            {
                this.onExplorerRestart = onExplorerRestart;
                this.onOpenLibrary = onOpenLibrary;
                this.onUrl = onUrl;
                CreateHandle(new CreateParams { Caption = Caption });
            }

            public void PostOpenLibrary()
            {
                Native.PostMessage(Handle, WM_OPEN_LIBRARY, IntPtr.Zero, IntPtr.Zero);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == taskbarCreated) onExplorerRestart();
                else if (m.Msg == WM_OPEN_LIBRARY) onOpenLibrary();
                else if (m.Msg == Native.WM_COPYDATA && m.LParam != IntPtr.Zero)
                {
                    // Untrusted text from another process: bounded here, parsed strictly by Community.
                    var cds = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                    if (cds.dwData == (IntPtr)UrlMessage && cds.cbData > 0 && cds.cbData <= 1024 && cds.lpData != IntPtr.Zero)
                        onUrl(Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2)); // handled later, not while the sender waits
                    m.Result = (IntPtr)1;
                    return;
                }
                else if (m.Msg == Native.WM_SETTINGCHANGE && m.LParam != IntPtr.Zero &&
                         Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                    Theme.Refresh(); // Windows light/dark switch: the app's own UI follows
                base.WndProc(ref m);
            }
        }
    }
}
