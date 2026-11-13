# Run after build.bat on an interactive Windows desktop. Opens isolated test menus/dialogs;
# does not launch the app or change widget settings. Each confirmation is answered automatically.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'bin\MenuDialogRegression.cs'
$exe = Join-Path $root 'bin\MenuDialogRegression.exe'
$app = Join-Path $root 'bin\DeskWidgets.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $app)) { throw 'Run build.bat first.' }
@'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskWidgets;

static class MenuDialogRegression
{
    static readonly Type Session = typeof(PopupMenu).Assembly.GetType("DeskWidgets.MenuSession");
    static readonly Type Alert = typeof(PopupMenu).Assembly.GetType("DeskWidgets.AlertDialog");
    static readonly List<string> failures = new List<string>();
    static int scenario;
    static Form library;
    static string label;

    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            Console.WriteLine(e.Exception);
            Environment.Exit(1);
        };
        Later(Next, 1);
        Later(delegate { Console.WriteLine("FAIL: test timed out"); Environment.Exit(1); }, 15000);
        Application.Run();
        foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
        if (failures.Count == 0) Console.WriteLine("PASS: all 8 menu/dialog scenarios");
        return failures.Count == 0 ? 0 : 1;
    }

    static void Later(Action action, int delay)
    {
        var timer = new Timer { Interval = delay };
        timer.Tick += delegate { timer.Dispose(); action(); };
        timer.Start();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) failures.Add(label + ": " + message);
    }

    static void Key(object session, Keys key)
    {
        Session.GetMethod("OnKey").Invoke(session, new object[] { key });
    }

    static void Next()
    {
        if (library != null) { library.Dispose(); library = null; }
        if (scenario == 8) { Application.ExitThread(); return; }
        int current = scenario++;
        bool submenu = current == 4 || current == 5;
        bool remove = current % 2 == 1;
        label = "scenario " + (current + 1);
        if (current == 2 || current == 3)
        {
            library = new Form { Text = "Menu dialog regression", ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Size = new Size(220, 100) };
            library.Show();
            library.Activate();
        }
        var menu = new PopupMenu();
        var items = submenu ? menu.AddSubmenu("Actions") : menu;
        int calls = 0;
        items.AddDestructive("Remove Widget", delegate
        {
            calls++;
            Check(calls == 1, "action ran more than once");
            Check(!Application.OpenForms.Cast<Form>().Any(f => f.GetType().Name == "MenuLevel"),
                "action started before menu windows were disposed");
            bool answered = false;
            var answer = new Timer { Interval = 500 };
            answer.Tick += delegate
            {
                answer.Stop();
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType() == Alert);
                Check(dialog != null && dialog.Visible, "confirmation did not stay visible");
                if (dialog != null)
                {
                    Check(dialog.Owner == null || dialog.Owner.GetType().Name != "MenuLevel", "menu owns confirmation");
                    answered = true;
                    dialog.Controls.OfType<Button>().Single(b => b.Text == (remove ? "Remove" : "Cancel")).PerformClick();
                }
            };
            answer.Start();
            bool result = (bool)Alert.GetMethod("Confirm").Invoke(null, new object[] {
                "Remove Widget?", "This widget and its settings will be removed.", "Remove" });
            answer.Dispose();
            Check(answered, "confirmation disappeared before it was answered");
            Check(result == remove, "wrong confirmation result");
            Later(Next, 50);
        });
        menu.Show(new Point(80, 100));
        object session = Session.GetField("current", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Later(delegate
        {
            if (current == 6 || current == 7)
            {
                if (current == 6) Key(session, Keys.Escape);
                else Session.GetMethod("Close").Invoke(session, new object[] { true });
                Later(delegate
                {
                    Check(calls == 0, "dismissal invoked an action");
                    Check(!Application.OpenForms.Cast<Form>().Any(f => f.GetType().Name == "MenuLevel"), "dismissal left a menu open");
                    Next();
                }, 250);
                return;
            }
            Key(session, Keys.Down);
            if (submenu) { Key(session, Keys.Right); }
            if (current < 2)
            {
                Form level = Application.OpenForms.Cast<Form>().Single(f => f.GetType().Name == "MenuLevel");
                Session.GetMethod("OnMouseUp").Invoke(session, new object[] { level, level.PointToScreen(new Point(40, 30)) });
            }
            else Key(session, Keys.Enter);
        }, 250);
    }
}
'@ | Set-Content -LiteralPath $source -Encoding UTF8
& $compiler /nologo /target:exe /out:$exe /r:$app /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Regression test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Menu/dialog regression failed.' }
