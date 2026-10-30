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
            bool first;
            using (new Mutex(true, "DeskWidgets.SingleInstance", out first))
            using (var showLibrary = new EventWaitHandle(false, EventResetMode.AutoReset, "DeskWidgets.ShowLibrary"))
            {
                // Launching the app again (Start menu, exe) just opens the window of the running instance.
                if (!first)
                {
                    if (!background)
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
                Application.Run(new App(showLibrary, !background));
            }
        }
    }

    // Owns the tray icon, the config file and all widget windows (one process for all widgets).
    sealed class App : ApplicationContext
    {
        const string AppName = "Desk Widgets";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "DeskWidgets";

        public static App Current { get; private set; }
        public static readonly Icon AppIcon = LoadIcon(Size.Empty);

        public event Action WidgetsChanged;

        readonly Config config;
        readonly NotifyIcon tray;
        readonly List<WidgetWindow> widgets = new List<WidgetWindow>();
        readonly MessageWindow messageWindow;
        readonly RegisteredWaitHandle showLibraryWait;
        LibraryWindow library;

        public App(EventWaitHandle showLibrary, bool openWindow)
        {
            Current = this;
            messageWindow = new MessageWindow(ReopenAll, OpenLibrary);
            PopupMenu.Owner = messageWindow.Handle;
            Native.FollowSystemThemeForMenus(messageWindow.Handle);

            config = Config.Load(ConfigPath());
            OpenAll();

            tray = new NotifyIcon { Icon = LoadIcon(SystemInformation.SmallIconSize), Text = AppName, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) OpenLibrary();
                else if (e.Button == MouseButtons.Right) ShowAppMenu();
            };

            if (StartWithWindows) StartWithWindows = true; // exe may have moved: refresh the path
            EnsureStartMenuShortcut();
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            showLibraryWait = ThreadPool.RegisterWaitForSingleObject(showLibrary,
                delegate { messageWindow.PostOpenLibrary(); }, null, Timeout.Infinite, false);

            if (openWindow || config.Sections.Count == 0) OpenLibrary();
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

        public void FillAppMenu(PopupMenu menu)
        {
            menu.AddBold("Open " + AppName, OpenLibrary);
            menu.AddCheck("Start with Windows", StartWithWindows, delegate { StartWithWindows = !StartWithWindows; });
            menu.AddSeparator();
            menu.Add("Exit " + AppName, Exit);
        }

        void ShowAppMenu()
        {
            var menu = new PopupMenu();
            FillAppMenu(menu);
            menu.Show(Cursor.Position);
        }

        public static bool StartWithWindows
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
                    if (value) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" --background");
                    else k.DeleteValue(RunName, false);
                }
            }
        }

        // Start menu entry (so the app is searchable), created or re-pointed at this exe.
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
                if (File.Exists(lnk) && string.Equals((string)t.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null),
                        exe, StringComparison.OrdinalIgnoreCase))
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
                else if (m.Msg == Native.WM_SETTINGCHANGE && m.LParam != IntPtr.Zero &&
                         Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                    Native.RefreshMenuTheme(); // Windows light/dark switch: menus follow
                base.WndProc(ref m);
            }
        }
    }
}
