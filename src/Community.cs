using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace DeskWidgets
{
    // The online half of community widgets: reads approved widgets from Supabase with the public
    // anon key (row level security lets it read approved rows and nothing else), and installs them
    // after the same hash check and prompt as a local file. The app never signs in.
    static class Community
    {
        // Public by design (server/README.md). Empty until the backend is set up.
        public const string Url = "https://vtgsbgkjleyqlxgjdvqy.supabase.co";
        public const string AnonKey = "sb_publishable_7-z7D1GwtQJumqAPFxlJlA_d3myO4gk";
        public const string Website = "https://whoissaaif.github.io/desk-widgets/";

        public static bool Configured { get { return Url.Length > 0 && AnonKey.Length > 0; } }

        public sealed class Listing
        {
            public string Id, Name, Category, Description, Author;
            public int Version, Api;
        }

        // ---- reading ----

        static object[] Query(string pathAndQuery)
        {
            // The apikey header alone reads public rows with either key type. A classic anon key is a
            // JWT and may also go in Authorization; a newer publishable key (sb_publishable_...) must not.
            var headers = new Dictionary<string, string> { { "apikey", AnonKey } };
            if (AnonKey.StartsWith("eyJ")) headers["Authorization"] = "Bearer " + AnonKey;
            string json = Encoding.UTF8.GetString(Http.GetBytes(Url.TrimEnd('/') + "/rest/v1/" + pathAndQuery, headers));
            var parsed = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json) as object[];
            if (parsed == null) throw new FormatException("Unexpected answer from the gallery.");
            return parsed;
        }

        static string Text(object row, string key)
        {
            var d = row as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        static int Number(object row, string key)
        {
            int n;
            return int.TryParse(Text(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        // The newest approved version of every widget. Call from a background thread.
        public static List<Listing> Explore()
        {
            // Previews are fetched separately, once per version (Preview), so the list stays small.
            return Query("explore?select=id,version,name,category,description,api_version,author&order=reviewed_at.desc")
                .Select(r => new Listing
                {
                    Id = Text(r, "id"), Version = Number(r, "version"), Name = Text(r, "name") ?? "", Category = Text(r, "category"),
                    Description = Text(r, "description") ?? "", Author = Text(r, "author") ?? "", Api = Number(r, "api_version")
                })
                .Where(l => Plugins.IsValidId(l.Id) && l.Version > 0)
                .ToList();
        }

        // ---- previews: fetched once per approved version, then kept on disk ----
        // An approved version never changes, so neither does its preview: the cache never goes stale.

        static readonly string PreviewDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets", "previews");

        // File name for a version's preview; the id is validated and the version is a number.
        static string PreviewFile(Listing l, string ext) { return Path.Combine(PreviewDir, l.Id + "_" + l.Version + ext); }

        // A version's preview from the disk cache, or the gallery the first time. Null when it has none.
        // Call from a background thread.
        public static Bitmap Preview(Listing l)
        {
            if (!Plugins.IsValidId(l.Id) || l.Version < 1) return null;
            string png = PreviewFile(l, ".png"), none = PreviewFile(l, ".none");
            try
            {
                if (File.Exists(none)) return null;
                if (File.Exists(png))
                {
                    Bitmap cached = DecodePreview(File.ReadAllBytes(png)); // the disk is checked like the network
                    if (cached != null) return cached;
                    File.Delete(png);
                }
            }
            catch (Exception) { }

            string base64;
            try
            {
                object[] rows = Query("versions?select=preview&widget_id=eq." + l.Id + "&version=eq." + l.Version + "&status=eq.approved");
                base64 = rows.Length > 0 ? Text(rows[0], "preview") : null;
            }
            catch (Exception) { return null; } // offline: try again next time, nothing cached
            byte[] bytes = null;
            if (!string.IsNullOrEmpty(base64) && base64.Length <= 140000)
                try { bytes = Convert.FromBase64String(base64); } catch (FormatException) { }
            Bitmap bmp = bytes != null ? DecodePreview(bytes) : null;
            try
            {
                Directory.CreateDirectory(PreviewDir);
                if (bmp != null) File.WriteAllBytes(png, bytes);
                else File.WriteAllBytes(none, new byte[0]); // remembered, so it isn't asked for every time
            }
            catch (Exception) { }
            return bmp;
        }

        // All previews for a list of widgets: from disk cache when available, otherwise one batch
        // query for all uncached previews instead of one HTTP request per widget.
        public static Dictionary<Listing, Bitmap> FetchPreviews(List<Listing> listings)
        {
            var result = new Dictionary<Listing, Bitmap>();
            var uncached = new List<Listing>();
            foreach (var l in listings)
            {
                if (!Plugins.IsValidId(l.Id) || l.Version < 1) { result[l] = null; continue; }
                string png = PreviewFile(l, ".png"), none = PreviewFile(l, ".none");
                try
                {
                    if (File.Exists(none)) { result[l] = null; continue; }
                    if (File.Exists(png))
                    {
                        Bitmap cached = DecodePreview(File.ReadAllBytes(png));
                        if (cached != null) { result[l] = cached; continue; }
                        File.Delete(png);
                    }
                }
                catch (Exception) { }
                uncached.Add(l);
            }
            if (uncached.Count > 0)
            {
                try
                {
                    object[] rows = Query("explore?select=id,version,preview&id=in.(" +
                        string.Join(",", uncached.Select(u => u.Id)) + ")");
                    var byId = new Dictionary<string, object>();
                    foreach (var r in rows) { string id = Text(r, "id"); if (id != null) byId[id] = r; }
                    Directory.CreateDirectory(PreviewDir);
                    foreach (var l in uncached)
                    {
                        object row;
                        string base64 = byId.TryGetValue(l.Id, out row) ? Text(row, "preview") : null;
                        byte[] bytes = null;
                        if (!string.IsNullOrEmpty(base64) && base64.Length <= 140000)
                            try { bytes = Convert.FromBase64String(base64); } catch (FormatException) { }
                        Bitmap bmp = bytes != null ? DecodePreview(bytes) : null;
                        result[l] = bmp;
                        try
                        {
                            if (bmp != null) File.WriteAllBytes(PreviewFile(l, ".png"), bytes);
                            else File.WriteAllBytes(PreviewFile(l, ".none"), new byte[0]);
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception)
                {
                    foreach (var l in uncached) result[l] = null;
                }
            }
            return result;
        }

        // Deletes cached previews of versions that are no longer listed.
        public static void PrunePreviews(IEnumerable<Listing> listed)
        {
            try
            {
                if (!Directory.Exists(PreviewDir)) return;
                var keep = new HashSet<string>(listed.Select(l => l.Id + "_" + l.Version), StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(PreviewDir))
                    if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) File.Delete(f);
            }
            catch (Exception) { }
        }

        // Untrusted image data: size-capped, decoded into a copy, and dropped if it's odd in any way.
        public static Bitmap DecodePreview(byte[] png)
        {
            if (png == null || png.Length == 0 || png.Length > 105000) return null;
            try
            {
                using (var ms = new MemoryStream(png))
                using (var img = Image.FromStream(ms, false, true))
                {
                    if (img.Width < 1 || img.Height < 1 || img.Width > 1024 || img.Height > 1024) return null;
                    return new Bitmap(img);
                }
            }
            catch (Exception) { return null; }
        }

        // ---- deskwidgets://install/<id>/<version> ----

        // Any web page can open this URL, so it's only a request: the id and version are checked here,
        // everything else is fetched from the database, and installing always asks first.
        public static bool TryParseUrl(string url, out string id, out int version)
        {
            id = null;
            version = 0;
            const string prefix = "deskwidgets://install/";
            if (url == null || url.Length > 200 || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string[] parts = url.Substring(prefix.Length).TrimEnd('/').Split('/');
            if (parts.Length != 2 || !Plugins.IsValidId(parts[0])) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1 || version > 100000) return false;
            id = parts[0];
            return true;
        }

        public static void HandleUrl(string url)
        {
            string id;
            int version;
            if (TryParseUrl(url, out id, out version)) Install(id, version);
        }

        // Fetches one approved version, checks it, asks, installs. Starts on the UI thread.
        public static void Install(string id, int version)
        {
            if (!Plugins.IsValidId(id)) return;
            if (!Configured)
            {
                AlertDialog.Show("Community widgets aren't connected yet.", "This copy of Desk Widgets doesn't know where the gallery is.");
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                object row = null;
                string author = null;
                try
                {
                    object[] rows = Query("versions?select=widget_id,version,name,category,description,api_version,source,sha256,status,reviewed_at" +
                                          "&widget_id=eq." + id + "&version=eq." + version + "&status=eq.approved");
                    row = rows.FirstOrDefault();
                    if (row != null)
                    {
                        object[] who = Query("explore?select=author&id=eq." + id);
                        author = who.Length > 0 ? Text(who[0], "author") : null;
                    }
                }
                catch (Exception e) { error = "The gallery couldn't be reached. Check your connection and try again. (" + e.Message + ")"; }
                App.RunOnUi(delegate
                {
                    if (error != null) { AlertDialog.Show("Couldn't get the widget.", error); return; }
                    if (row == null) { AlertDialog.Show("This widget isn't available.", "It may have been removed, or this version wasn't approved."); return; }
                    Confirm(id, row, author);
                });
            });
        }

        static void Confirm(string id, object row, string author)
        {
            string source = Text(row, "source") ?? "";
            byte[] bytes = new UTF8Encoding(false).GetBytes(source);
            string name = Text(row, "name") ?? id;
            int version = Number(row, "version"), api = Number(row, "api_version");
            // The guarantee: these bytes are the ones the owner approved.
            if (Text(row, "widget_id") != id || Text(row, "status") != "approved" || Plugins.Sha256(bytes) != Text(row, "sha256"))
            {
                AlertDialog.Show(name + " wasn't installed.", "Its code doesn't match what was reviewed, so it wasn't installed.");
                return;
            }
            if (bytes.Length > Plugins.MaxSourceBytes) { AlertDialog.Show(name + " wasn't installed.", "Its code is too large."); return; }
            string hidden = Plugins.HiddenCharacter(source); // the gallery refuses these too; don't rely on it
            if (hidden != null) { AlertDialog.Show(name + " wasn't installed.", Plugins.HiddenMessage(hidden)); return; }
            if (api > PluginApi.Version)
            {
                AlertDialog.Show(name + " needs a newer Desk Widgets.", "Update Desk Widgets, then try again.");
                return;
            }

            PluginInfo old = Plugins.Get(id);
            DateTime reviewed;
            bool hasDate = DateTime.TryParse(Text(row, "reviewed_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out reviewed);
            var facts = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("By", author ?? "unknown"),
                new KeyValuePair<string, string>("Version", old == null ? version.ToString(CultureInfo.InvariantCulture)
                    : version + " (replaces " + old.Version + ")"),
                new KeyValuePair<string, string>("Reviewed", hasDate ? reviewed.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) : "yes"),
                new KeyValuePair<string, string>("Size", (bytes.Length + 1023) / 1024 + " KB"),
            };
            if (!AlertDialog.ConfirmInstall(name, "Install " + name + "?", facts, source)) return;

            var p = new PluginInfo
            {
                Id = id, Version = version, Api = api, Name = name, Category = Text(row, "category"),
                Description = Text(row, "description"), Author = author, Sha256 = Text(row, "sha256")
            };
            Plugins.InstallChecked(p, bytes, name);
        }

        // ---- updates ----

        // Looks for newer approved versions of installed community widgets. Never installs them:
        // a new version is new code, so the person decides (Installed shows Update).
        public static void CheckForUpdates()
        {
            if (!Configured) return;
            var ids = Plugins.Installed.Where(p => p.Author != "local").Select(p => p.Id).ToList();
            if (ids.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                object[] rows;
                try { rows = Query("explore?select=id,version&id=in.(" + string.Join(",", ids) + ")"); }
                catch (Exception) { return; } // offline: try again next time
                App.RunOnUi(delegate
                {
                    bool changed = false;
                    foreach (var r in rows)
                    {
                        PluginInfo p = Plugins.Get(Text(r, "id") ?? "");
                        int latest = Number(r, "version");
                        if (p != null && latest > p.Version && p.UpdateVersion != latest) { p.UpdateVersion = latest; changed = true; }
                    }
                    if (changed) WidgetRegistry.RaiseChanged();
                });
            });
        }
    }
}
