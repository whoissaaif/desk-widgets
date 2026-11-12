using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DeskWidgets
{
    static class Backup
    {
        const string Extension = ".dwbackup";
        const string Filter = "Desk Widgets Backup (*" + Extension + ")|*" + Extension;

        public static void Export()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Export Backup",
                Filter = Filter,
                FileName = "DeskWidgets-" + DateTime.Now.ToString("yyyy-MM-dd") + Extension,
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    WriteBackup(dlg.FileName);
                    Toast.Show("Backup Saved", "Your widgets and plugins were exported.");
                }
                catch (Exception ex)
                {
                    AlertDialog.Show("Backup Failed", ex.Message);
                }
            }
        }

        static void WriteBackup(string path)
        {
            string configPath = Path.Combine(App.DataDir, "widgets.ini");
            string pluginRoot = PluginRoot();

            string tmp = path + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    if (File.Exists(configPath))
                    {
                        var entry = zip.CreateEntry("widgets.ini", CompressionLevel.Optimal);
                        using (var s = entry.Open())
                        {
                            byte[] data = File.ReadAllBytes(configPath);
                            s.Write(data, 0, data.Length);
                        }
                    }

                    if (Directory.Exists(pluginRoot))
                    {
                        foreach (string dir in Directory.GetDirectories(pluginRoot))
                        {
                            string id = Path.GetFileName(dir);
                            string ini = Path.Combine(dir, "plugin.ini");
                            if (!File.Exists(ini)) continue;
                            AddFile(zip, "plugins/" + id + "/plugin.ini", ini);
                            foreach (string verDir in Directory.GetDirectories(dir))
                            {
                                string src = Path.Combine(verDir, "source.cs");
                                if (File.Exists(src))
                                    AddFile(zip, "plugins/" + id + "/" + Path.GetFileName(verDir) + "/source.cs", src);
                            }
                        }
                    }

                    var manifest = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (var s = manifest.Open())
                    {
                        string json = "{ \"version\": 1, \"app\": \"Desk Widgets\", \"date\": \"" +
                            DateTime.UtcNow.ToString("o") + "\" }";
                        byte[] data = Encoding.UTF8.GetBytes(json);
                        s.Write(data, 0, data.Length);
                    }
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }

        static void AddFile(ZipArchive zip, string entryName, string filePath)
        {
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (var s = entry.Open())
            {
                byte[] data = File.ReadAllBytes(filePath);
                s.Write(data, 0, data.Length);
            }
        }

        public static void Import()
        {
            using (var dlg = new OpenFileDialog { Title = "Restore from Backup", Filter = Filter })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                if (!AlertDialog.Confirm("Restore Backup?",
                    "Widgets from the backup that aren’t already on your desktop will be added. Your existing widgets stay as they are.",
                    "Restore"))
                    return;
                try
                {
                    int added = ReadBackup(dlg.FileName);
                    string msg = added > 0
                        ? added + " widget" + (added == 1 ? "" : "s") + " restored."
                        : "Everything in the backup was already here.";
                    Toast.Show("Restore Complete", msg);
                }
                catch (Exception ex)
                {
                    AlertDialog.Show("Restore Failed", ex.Message);
                }
            }
        }

        static int ReadBackup(string path)
        {
            string configPath = Path.Combine(App.DataDir, "widgets.ini");
            string pluginRoot = PluginRoot();

            Config current = Config.Load(configPath);
            var existingTypes = new HashSet<string>();
            foreach (var s in current.Sections)
            {
                string t = s.Type;
                if (t.Length > 0) existingTypes.Add(t);
            }

            List<Section> toAdd = new List<Section>();
            var pluginEntries = new Dictionary<string, List<ZipArchiveEntry>>();

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                // Read the backed-up config to find widget sections to merge.
                var iniEntry = zip.GetEntry("widgets.ini");
                if (iniEntry != null)
                {
                    string tmpIni = Path.GetTempFileName();
                    try
                    {
                        using (var src = iniEntry.Open())
                        using (var dst = File.Create(tmpIni))
                            src.CopyTo(dst);
                        Config backup = Config.Load(tmpIni);
                        foreach (var s in backup.Sections)
                        {
                            if (s.Type.Length > 0 && !existingTypes.Contains(s.Type))
                                toAdd.Add(s);
                        }
                    }
                    finally { try { File.Delete(tmpIni); } catch { } }
                }

                // Collect plugin files grouped by id.
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("plugins/", StringComparison.Ordinal)) continue;
                    string rel = entry.FullName.Substring("plugins/".Length);
                    int slash = rel.IndexOf('/');
                    if (slash <= 0) continue;
                    string id = rel.Substring(0, slash);
                    List<ZipArchiveEntry> list;
                    if (!pluginEntries.TryGetValue(id, out list))
                    {
                        list = new List<ZipArchiveEntry>();
                        pluginEntries[id] = list;
                    }
                    list.Add(entry);
                }

                // Install plugins that aren't already present.
                foreach (var kv in pluginEntries)
                {
                    string id = kv.Key;
                    string dest = Path.Combine(pluginRoot, id);
                    if (Directory.Exists(dest) && File.Exists(Path.Combine(dest, "plugin.ini")))
                        continue;
                    Directory.CreateDirectory(dest);
                    foreach (var entry in kv.Value)
                    {
                        string rel = entry.FullName.Substring(("plugins/" + id + "/").Length);
                        if (rel.Length == 0) continue;
                        string target = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = entry.Open())
                        using (var dst = File.Create(target))
                            src.CopyTo(dst);
                    }
                }
            }

            if (App.Current == null) return 0;

            // Load newly restored plugins so their types register before we open widgets.
            Plugins.LoadAll();

            // Add the merged widget sections to the live config.
            int count = 0;
            foreach (var s in toAdd)
            {
                App.Current.MergeSection(s);
                count++;
            }

            // Open any config sections that don't have a window yet.
            App.Current.OpenMerged();

            return count;
        }

        static string PluginRoot()
        {
            if (App.IsPackaged)
            {
                try { return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "DeskWidgets", "plugins"); }
                catch { }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets", "plugins");
        }
    }
}
