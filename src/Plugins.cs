using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.CSharp;

namespace DeskWidgets
{
    // One installed community widget, as described by plugins\<id>\plugin.ini.
    sealed class PluginInfo
    {
        public string Id, Name, Category, Description, Author, Sha256;
        public int Version, Api;
        public string Disabled; // why it was turned off, or null
        public int UpdateVersion; // a newer approved version, found by Community.CheckForUpdates (not saved)
        public string Dir;
        public WidgetType Type; // registered while enabled and loaded

        public string VersionDir { get { return Path.Combine(Dir, Version.ToString(CultureInfo.InvariantCulture)); } }
        public string SourcePath { get { return Path.Combine(VersionDir, "source.cs"); } }
        string ManifestPath { get { return Path.Combine(Dir, "plugin.ini"); } }

        public static PluginInfo Load(string dir)
        {
            var values = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "plugin.ini"), Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            Func<string, string> get = k => { string v; return values.TryGetValue(k, out v) && v.Length > 0 ? v : null; };
            int version, api;
            int.TryParse(get("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
            int.TryParse(get("api"), NumberStyles.Integer, CultureInfo.InvariantCulture, out api);
            return new PluginInfo
            {
                Dir = dir, Id = get("id"), Name = get("name"), Category = get("category"), Description = get("description"),
                Author = get("author"), Sha256 = get("sha256"), Version = version, Api = api, Disabled = get("disabled")
            };
        }

        public void Save()
        {
            var sb = new StringBuilder();
            Action<string, object> add = (k, v) =>
            {
                string s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(s)) sb.Append(k).Append('=').Append(s.Replace("\r", " ").Replace("\n", " ")).Append("\r\n");
            };
            add("id", Id); add("version", Version); add("api", Api); add("sha256", Sha256);
            add("name", Name); add("category", Category); add("description", Description); add("author", Author);
            add("disabled", Disabled);
            File.WriteAllText(ManifestPath, sb.ToString(), new UTF8Encoding(false));
        }
    }

    // Community widgets: C# source compiled on this PC, only when its hash matches the reviewed one.
    // Layout: %LOCALAPPDATA%\DeskWidgets\plugins\<id>\plugin.ini and <id>\<version>\{source.cs, widget.dll}.
    static class Plugins
    {
        public const int MaxSourceBytes = 512 * 1024;
        static readonly Regex IdPattern = new Regex(@"^u\.[a-z0-9-]+\.[a-z0-9-]+\z");

        static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets", "plugins");
        static string MarkerPath { get { return Path.Combine(Root, "crash.marker"); } }

        static readonly Dictionary<string, PluginInfo> installed = new Dictionary<string, PluginInfo>();
        static readonly Dictionary<string, string> assemblyToId = new Dictionary<string, string>();
        static string guarding; // plugin whose code is running under the crash marker right now
        static string exeHash;

        // Ids are also folder names, so every id entering the app goes through this.
        public static bool IsValidId(string id)
        {
            return id != null && id.Length <= 64 && IdPattern.IsMatch(id);
        }

        public static IEnumerable<PluginInfo> Installed { get { return installed.Values.OrderBy(p => p.Name ?? p.Id); } }

        public static PluginInfo Get(string id)
        {
            PluginInfo p;
            return installed.TryGetValue(id, out p) ? p : null;
        }

        // ---- startup ----

        public static void LoadAll()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHost;
            try { Directory.CreateDirectory(Root); }
            catch (Exception) { return; }

            string crashed = ReadMarker();
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string id = Path.GetFileName(dir);
                // Uninstall deletes plugin.ini first; finish removing folders whose delete failed last time.
                if (!File.Exists(Path.Combine(dir, "plugin.ini")) || !IsValidId(id))
                {
                    if (!File.Exists(Path.Combine(dir, "plugin.ini"))) TryDelete(dir);
                    continue;
                }
                PluginInfo p;
                try { p = PluginInfo.Load(dir); }
                catch (Exception) { continue; }
                if (p.Id != id) continue;
                installed[id] = p;
                if (id == crashed && p.Disabled == null)
                {
                    SetDisabled(p, "Desk Widgets closed unexpectedly while this widget was running.");
                    Notify(DisplayName(p) + " was turned off", p.Disabled + " You can try it again under Installed.");
                }
                if (p.Disabled != null) continue;
                string error = Load(p);
                if (error != null)
                {
                    SetDisabled(p, error);
                    Notify(DisplayName(p) + " couldn't start", error);
                }
            }
        }

        static Assembly ResolveHost(object sender, ResolveEventArgs e)
        {
            Assembly host = typeof(Plugins).Assembly;
            return new AssemblyName(e.Name).Name == host.GetName().Name ? host : null;
        }

        // Exception messages often lack a final period; reasons are shown as sentences.
        static string Sentence(string text)
        {
            text = (text ?? "").Trim();
            return text.Length == 0 || ".!?".IndexOf(text[text.Length - 1]) >= 0 ? text : text + ".";
        }

        public static string DisplayName(PluginInfo p)
        {
            return p.Name ?? p.Id;
        }

        // ---- build and load ----

        // Compiles (or reuses the cached build of) a plugin and registers it. Returns an error, or null.
        static string Load(PluginInfo p)
        {
            WidgetType type;
            string error = Build(p, out type);
            if (error != null) return error;
            if (!WidgetRegistry.Register(type)) return "Another widget already uses the id " + p.Id + ".";
            p.Type = type;
            return null;
        }

        static string Build(PluginInfo p, out WidgetType type)
        {
            type = null;
            if (p.Api > PluginApi.Version) return "It needs a newer version of Desk Widgets.";
            byte[] source;
            try { source = File.ReadAllBytes(p.SourcePath); }
            catch (Exception) { return "Its source file is missing. Install it again."; }
            string hash = Sha256(source);
            if (p.Sha256 == null || hash != p.Sha256) return "Its code changed after it was reviewed, so it wasn't loaded.";
            string hidden = HiddenCharacter(new UTF8Encoding(false).GetString(source));
            if (hidden != null) return HiddenMessage(hidden) + " It wasn't loaded.";

            string dll = Path.Combine(p.VersionDir, "widget.dll"), keyPath = Path.Combine(p.VersionDir, "build.key");
            string key = hash + " " + ExeHash();
            string assemblyName = p.Id + "_" + hash.Substring(0, 8);
            byte[] image = null;
            try { if (File.Exists(dll) && File.ReadAllText(keyPath) == key) image = File.ReadAllBytes(dll); }
            catch (Exception) { }
            if (image == null)
            {
                string error = Compile(p.SourcePath, p.VersionDir, assemblyName, out image);
                if (error != null) return "It doesn't compile with this version of Desk Widgets.\n\n" + error;
                try
                {
                    File.WriteAllBytes(dll, image);
                    File.WriteAllText(keyPath, key);
                }
                catch (Exception) { } // compiles again next start
            }

            Assembly asm;
            Type[] marked;
            try
            {
                asm = Assembly.Load(image); // from bytes: the file is never locked
                marked = asm.GetTypes().Where(t => t.IsDefined(typeof(DeskWidgetAttribute), false)).ToArray();
            }
            catch (Exception e) { return "It couldn't be loaded: " + Sentence(e.Message); }
            if (marked.Length != 1) return "Its code must have exactly one [DeskWidget] class.";
            Type cls = marked[0];
            ConstructorInfo ctor = cls.GetConstructor(new[] { typeof(Section) });
            if (cls.IsAbstract || !typeof(WidgetWindow).IsAssignableFrom(cls) || ctor == null)
                return "Its [DeskWidget] class must derive from WidgetWindow and take a Section.";

            // A compiled delegate rather than ConstructorInfo.Invoke, so exceptions aren't wrapped.
            var settings = Expression.Parameter(typeof(Section), "s");
            var create = Expression.Lambda<Func<Section, WidgetWindow>>(
                Expression.Convert(Expression.New(ctor, settings), typeof(WidgetWindow)), settings).Compile();
            var attr = (DeskWidgetAttribute)cls.GetCustomAttributes(typeof(DeskWidgetAttribute), false)[0];
            if (p.Name == null) p.Name = attr.Name;
            if (p.Description == null) p.Description = attr.Description;
            assemblyToId[asm.GetName().Name] = p.Id;
            type = new WidgetType
            {
                Id = p.Id, Category = p.Category, Name = p.Name ?? cls.Name, Description = p.Description ?? "",
                PluginId = p.Id, Create = create
            };
            return null;
        }

        // The C# compiler that ships with Windows (same csc as build.bat), against the running exe.
        static string Compile(string sourcePath, string outDir, string assemblyName, out byte[] image)
        {
            image = null;
            string output = Path.Combine(outDir, assemblyName + ".dll"); // the file name sets the assembly name
            var options = new CompilerParameters
            {
                GenerateExecutable = false, GenerateInMemory = false, IncludeDebugInformation = false,
                OutputAssembly = output, CompilerOptions = "/optimize+ /codepage:65001"
            };
            options.ReferencedAssemblies.AddRange(new[]
            {
                "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll",
                "System.Xml.dll", "System.Xml.Linq.dll", "System.Web.Extensions.dll", HostPath
            });
            try
            {
                using (var csc = new CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v4.0" } }))
                {
                    CompilerResults result = csc.CompileAssemblyFromFile(options, sourcePath);
                    var errors = result.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).ToList();
                    if (errors.Count > 0)
                        return string.Join("\n", errors.Take(5).Select(e => "Line " + e.Line + ": " + e.ErrorText));
                }
                image = File.ReadAllBytes(output);
                return null;
            }
            catch (Exception e) { return "The C# compiler couldn't run: " + e.Message; }
            finally { TryDeleteFile(output); }
        }

        static string HostPath { get { return typeof(Plugins).Assembly.Location; } }

        static string ExeHash()
        {
            if (exeHash == null) exeHash = Sha256(File.ReadAllBytes(HostPath));
            return exeHash;
        }

        // ---- source must read the way it compiles ----

        // Characters that make code look different from what the compiler sees ("Trojan Source",
        // CVE-2021-42574): line breaks only the compiler honors (lone CR, U+0085, U+2028, U+2029),
        // invisible characters and bidi controls. A hash proves code didn't change after review, not
        // that the reviewer saw what runs, so code containing any of these is never compiled. Same
        // rule as source_is_plain (server/schema.sql) and hiddenCharacters (docs/assets/site.js).
        public static bool IsHidden(string text, int i)
        {
            char c = text[i];
            if (c == '\r') return i + 1 >= text.Length || text[i + 1] != '\n'; // CR only as part of CRLF
            return (c < 0x20 && c != '\t' && c != '\n') || (c >= 0x7F && c <= 0x9F) ||
                   (c >= 0x200B && c <= 0x200F) || c == 0x2028 || c == 0x2029 ||
                   (c >= 0x202A && c <= 0x202E) || (c >= 0x2060 && c <= 0x2069) || c == 0xFEFF;
        }

        // The first hidden character as "U+2028 on line 3", or null when the text is plain.
        public static string HiddenCharacter(string text)
        {
            int line = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (IsHidden(text, i)) return string.Format(CultureInfo.InvariantCulture, "U+{0:X4} on line {1}", (int)text[i], line);
                if (text[i] == '\n') line++;
            }
            return null;
        }

        public static string HiddenMessage(string where)
        {
            return "Its code contains a hidden character (" + where + ") that can make code look different from what runs.";
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        // ---- crash isolation ----

        // Runs code that calls into a plugin (constructor, first render, preview) under the crash
        // marker. If it throws, the plugin is turned off and null is returned. Built-ins run as is.
        public static T Guard<T>(WidgetType type, Func<T> run) where T : class
        {
            if (type.PluginId == null) return run();
            string outer = guarding;
            guarding = type.PluginId;
            WriteMarker(type.PluginId);
            try
            {
                T result = run();
                // A crash inside a window message is reported through Application.ThreadException
                // instead of thrown here; that turns the plugin off, so drop what it half-built.
                if (WidgetRegistry.Find(type.Id) != type)
                {
                    var d = result as IDisposable;
                    if (d != null) d.Dispose();
                    return null;
                }
                return result;
            }
            catch (Exception e)
            {
                Crashed(type.PluginId, e);
                return null;
            }
            finally
            {
                guarding = outer;
                if (outer == null) TryDeleteFile(MarkerPath); else WriteMarker(outer);
            }
        }

        static void WriteMarker(string id)
        {
            try { File.WriteAllText(MarkerPath, "loading=" + id); }
            catch (Exception) { }
        }

        static string ReadMarker()
        {
            try
            {
                if (!File.Exists(MarkerPath)) return null;
                string text = File.ReadAllText(MarkerPath).Trim();
                File.Delete(MarkerPath);
                return text.StartsWith("loading=") ? text.Substring(8) : null;
            }
            catch (Exception) { return null; }
        }

        // The plugin whose code threw, from the exception's stack (or the one under the marker).
        public static string FromException(Exception e)
        {
            for (; e != null; e = e.InnerException)
            {
                foreach (StackFrame frame in new StackTrace(e, false).GetFrames() ?? new StackFrame[0])
                {
                    MethodBase m = frame.GetMethod();
                    string id;
                    if (m != null && m.DeclaringType != null &&
                        assemblyToId.TryGetValue(m.DeclaringType.Assembly.GetName().Name, out id)) return id;
                }
            }
            return guarding;
        }

        // A plugin threw on the UI thread: close it, turn it off and say so. The app keeps running.
        public static void Crashed(string id, Exception e)
        {
            // Outside Guard the crash may come from inside one of the plugin's own window messages:
            // close its windows once that message has unwound, not from within it.
            if (guarding == null) { Ui.Later(() => { guarding = id; try { Crashed(id, e); } finally { guarding = null; } }); return; }
            PluginInfo p = Get(id);
            if (p == null || p.Disabled != null) return;
            Disable(p, "It stopped with an error: " + Sentence(e.Message));
            Notify(DisplayName(p) + " was turned off", p.Disabled + " You can try it again under Installed.");
        }

        // The process is about to die: just remember which plugin to keep off next time.
        public static void CrashedFatally(string id, Exception e)
        {
            PluginInfo p = Get(id);
            if (p != null && p.Disabled == null)
                SetDisabled(p, "Desk Widgets closed because of an error in this widget: " + Sentence(e != null ? e.Message : "unknown"));
        }

        // ---- install, enable, uninstall ----

        static void SetDisabled(PluginInfo p, string reason)
        {
            p.Disabled = reason;
            try { p.Save(); }
            catch (Exception) { }
        }

        static void Disable(PluginInfo p, string reason)
        {
            SetDisabled(p, reason);
            p.Type = null;
            App.Current.CloseType(p.Id); // its sections stay, so turning it back on restores them
            WidgetRegistry.Unregister(p.Id);
            WidgetRegistry.RaiseChanged();
        }

        public static void TryAgain(PluginInfo p)
        {
            SetDisabled(p, null);
            string error = Load(p);
            if (error != null) SetDisabled(p, error);
            else App.Current.OpenType(p.Id);
            WidgetRegistry.RaiseChanged();
            if (error != null) AlertDialog.Show(DisplayName(p) + " still can't start", error);
        }

        public static void Uninstall(PluginInfo p)
        {
            App.Current.RemoveType(p.Id);
            WidgetRegistry.Unregister(p.Id);
            installed.Remove(p.Id);
            TryDeleteFile(Path.Combine(p.Dir, "plugin.ini")); // so it never loads again, even if the folder stays
            TryDelete(p.Dir);
            WidgetRegistry.RaiseChanged();
        }

        // Install (or update) a widget from a .cs file on this PC. Always asks first.
        public static void InstallFromFile(string path)
        {
            byte[] raw;
            string text;
            try
            {
                raw = File.ReadAllBytes(path);
                if (raw.Length > MaxSourceBytes) throw new InvalidDataException("The file is larger than 512 KB.");
                text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF');
            }
            catch (Exception e)
            {
                AlertDialog.Show("This file can't be installed.", e is DecoderFallbackException ? "It isn't UTF-8 text." : e.Message);
                return;
            }

            string hidden = HiddenCharacter(text);
            if (hidden != null) { AlertDialog.Show("This file can't be installed.", HiddenMessage(hidden)); return; }

            string file = Path.GetFileName(path), id = "u.local." + Slug(Path.GetFileNameWithoutExtension(path));
            PluginInfo old = Get(id);
            int version = old == null ? 1 : old.Version + 1;
            var facts = new[]
            {
                new KeyValuePair<string, string>("File", file),
                new KeyValuePair<string, string>("From", "This PC (not reviewed)"),
                new KeyValuePair<string, string>("Version", old == null ? "1" : version + " (replaces " + old.Version + ")"),
                new KeyValuePair<string, string>("Size", (raw.Length + 1023) / 1024 + " KB"),
            };
            if (!AlertDialog.ConfirmInstall(file, "Install " + file + "?", facts, text)) return;

            byte[] source = new UTF8Encoding(false).GetBytes(text);
            // No category: local widgets show under Installed only. You read it, so its hash counts as reviewed.
            var p = new PluginInfo { Id = id, Version = version, Api = PluginApi.Version, Author = "local", Sha256 = Sha256(source) };
            InstallChecked(p, source, file);
        }

        // Writes, compiles and registers a version whose hash was already checked and that the person
        // confirmed. Replaces an installed version of the same widget.
        public static void InstallChecked(PluginInfo p, byte[] source, string displayName)
        {
            if (!IsValidId(p.Id) || p.Sha256 != Sha256(source)) return;
            p.Dir = Path.Combine(Root, p.Id);
            PluginInfo old = Get(p.Id);
            bool sameFolder = old != null && old.Version == p.Version;
            WidgetType type;
            string error;
            try
            {
                Directory.CreateDirectory(p.VersionDir);
                File.WriteAllBytes(p.SourcePath, source);
                error = Build(p, out type);
            }
            catch (Exception e) { error = e.Message; type = null; }
            if (error != null)
            {
                if (!sameFolder) TryDelete(p.VersionDir);
                if (old == null) TryDelete(p.Dir);
                AlertDialog.Show(displayName + " wasn't installed.", error);
                return;
            }

            // Swap in the new version: the old assembly stays in memory until restart (.NET can't unload).
            if (old != null)
            {
                App.Current.CloseType(p.Id);
                WidgetRegistry.Unregister(p.Id);
                if (!sameFolder) TryDelete(old.VersionDir);
            }
            WidgetRegistry.Register(type);
            p.Type = type;
            p.Save();
            installed[p.Id] = p;
            if (old != null) App.Current.OpenType(p.Id);
            WidgetRegistry.RaiseChanged();
        }

        static string Slug(string name)
        {
            string s = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            int room = 64 - "u.local.".Length;
            if (s.Length > room) s = s.Substring(0, room).Trim('-');
            return s.Length == 0 ? "widget" : s;
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception) { } // finished on the next start
        }

        static void TryDeleteFile(string file)
        {
            try { File.Delete(file); }
            catch (Exception) { }
        }

        // ---- messages ----

        // Shown once the app is idle, never from inside a crash or while windows are being built.
        static readonly Queue<KeyValuePair<string, string>> notices = new Queue<KeyValuePair<string, string>>();
        static Timer noticeTimer;

        static void Notify(string title, string message)
        {
            notices.Enqueue(new KeyValuePair<string, string>(title, message));
            if (noticeTimer != null) return;
            noticeTimer = Ui.Later(delegate
            {
                noticeTimer = null;
                while (notices.Count > 0)
                {
                    var n = notices.Dequeue();
                    AlertDialog.Show(n.Key, n.Value);
                }
            }, 500);
        }
    }
}
