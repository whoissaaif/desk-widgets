using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskWidgets
{
    // Signing in to the community gallery from the app, to upload widgets. Same account as the
    // website: username and password (server/functions/login). Only the refresh token is kept,
    // encrypted with DPAPI for this Windows account; access tokens live in memory. Network calls
    // block, so call them from a background thread; Changed is raised on the UI thread.
    static class Account
    {
        static readonly object gate = new object();
        static string username, userId, refreshToken, accessToken;
        static DateTime accessExpires;
        static bool loaded;

        public static bool IsAdmin { get; private set; }
        public static Bitmap Photo { get; private set; } // null when there's none, or not loaded yet
        public static string Bio { get; private set; }

        // Signed in, signed out, or the profile (photo, owner) arrived.
        public static event Action Changed;

        static string FilePath { get { return Path.Combine(App.DataDir, "account.dat"); } }

        public static string Username { get { Load(); return username; } }
        public static bool SignedIn { get { Load(); return refreshToken != null; } }

        // ---- the saved sign-in ----

        static void Load()
        {
            lock (gate)
            {
                if (loaded) return;
                loaded = true;
                try
                {
                    if (!File.Exists(FilePath)) return;
                    string[] parts = (Secrets.Unprotect(File.ReadAllText(FilePath)) ?? "").Split('\n');
                    if (parts.Length != 3 || parts[2].Length == 0) return; // another PC or account: sign in again
                    username = parts[0];
                    userId = parts[1];
                    refreshToken = parts[2];
                }
                catch (Exception) { }
            }
        }

        static void Save()
        {
            try
            {
                if (refreshToken == null) { File.Delete(FilePath); return; }
                File.WriteAllText(FilePath, Secrets.Protect(username + "\n" + userId + "\n" + refreshToken));
            }
            catch (Exception) { }
        }

        static void RaiseChanged()
        {
            App.RunOnUi(delegate { if (Changed != null) Changed(); });
        }

        // ---- signing in and out ----

        static Dictionary<string, string> PublicHeaders()
        {
            var h = new Dictionary<string, string> { { "apikey", Community.AnonKey } };
            if (Community.AnonKey.StartsWith("eyJ")) h["Authorization"] = "Bearer " + Community.AnonKey;
            return h;
        }

        static Dictionary<string, object> Parse(string json)
        {
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) throw new FormatException("Unexpected answer from the gallery.");
            return d;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }

        // Keeps the tokens from a sign-in or refresh answer.
        static void TakeSession(Dictionary<string, object> s)
        {
            string access = Str(s, "access_token"), refresh = Str(s, "refresh_token");
            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) throw new FormatException("Unexpected answer from the gallery.");
            int seconds;
            if (!int.TryParse(Str(s, "expires_in"), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) seconds = 3600;
            accessToken = access;
            refreshToken = refresh;
            accessExpires = DateTime.UtcNow.AddSeconds(seconds - 60);
        }

        // Returns null when signed in, or a sentence saying why not.
        public static string SignIn(string name, string password)
        {
            string json = new JavaScriptSerializer().Serialize(new Dictionary<string, string> { { "username", name }, { "password", password } });
            Dictionary<string, object> s;
            try { s = Parse(Http.SendJson("POST", Community.Url + "/functions/v1/login", PublicHeaders(), json)); }
            catch (HttpError e) { return e.Message; }
            catch (Exception) { return "The gallery couldn't be reached. Check your connection and try again."; }
            lock (gate)
            {
                Load();
                TakeSession(s);
                username = Str(s, "username") ?? name;
                var user = s.ContainsKey("user") ? s["user"] as Dictionary<string, object> : null;
                userId = user != null ? Str(user, "id") : null;
                Save();
            }
            RaiseChanged();
            return null;
        }

        public static void SignOut()
        {
            lock (gate)
            {
                Load();
                username = userId = refreshToken = accessToken = null;
                IsAdmin = false;
                Bio = null;
                if (Photo != null) { Photo.Dispose(); Photo = null; }
                Save();
            }
            if (Changed != null) Changed();
        }

        // A current access token, refreshed when it's about to expire. Throws when the sign-in ended
        // (signed out elsewhere, or the password changed): the app is then signed out.
        static string Token()
        {
            lock (gate)
            {
                Load();
                if (refreshToken == null) throw new InvalidOperationException("Sign in first.");
                if (accessToken != null && DateTime.UtcNow < accessExpires) return accessToken;
                string json = new JavaScriptSerializer().Serialize(new Dictionary<string, string> { { "refresh_token", refreshToken } });
                try
                {
                    TakeSession(Parse(Http.SendJson("POST", Community.Url + "/auth/v1/token?grant_type=refresh_token", PublicHeaders(), json)));
                    Save(); // refresh tokens are single-use
                    return accessToken;
                }
                catch (HttpError e)
                {
                    if (e.Status != 400 && e.Status != 401) throw;
                    username = userId = refreshToken = accessToken = null;
                    Save();
                    App.RunOnUi(delegate { if (Changed != null) Changed(); });
                    throw new InvalidOperationException("Your sign-in ended. Sign in again.");
                }
            }
        }

        static Dictionary<string, string> UserHeaders()
        {
            return new Dictionary<string, string> { { "apikey", Community.AnonKey }, { "Authorization", "Bearer " + Token() } };
        }

        static object[] Rest(string method, string pathAndQuery, object body)
        {
            var headers = UserHeaders();
            string url = Community.Url + "/rest/v1/" + pathAndQuery, json;
            if (method == "GET")
                json = Encoding.UTF8.GetString(Http.GetBytes(url, headers));
            else
            {
                headers["Prefer"] = "return=representation";
                json = Http.SendJson(method, url, headers, new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(body));
            }
            var parsed = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json);
            return parsed as object[] ?? new[] { parsed };
        }

        // ---- profile ----

        // Photo, bio and whether this is the owner, for the sidebar. Quietly does nothing offline.
        public static void LoadProfile()
        {
            if (!SignedIn) return;
            try
            {
                object[] rows = Rest("GET", "profiles?select=username,bio,avatar&id=eq." + Uri.EscapeDataString(userId ?? ""), null);
                var row = rows.FirstOrDefault() as Dictionary<string, object>;
                bool admin = false;
                try { admin = Http.SendJson("POST", Community.Url + "/rest/v1/rpc/is_admin", UserHeaders(), "{}").Trim() == "true"; }
                catch (Exception) { }
                Bitmap photo = null;
                string avatar = row != null ? Str(row, "avatar") : null;
                if (!string.IsNullOrEmpty(avatar) && avatar.Length <= 60000)
                    try { photo = DecodePhoto(Convert.FromBase64String(avatar)); } catch (FormatException) { }
                lock (gate)
                {
                    if (Photo != null) Photo.Dispose();
                    Photo = photo;
                    Bio = row != null ? Str(row, "bio") : null;
                    IsAdmin = admin;
                }
                RaiseChanged();
            }
            catch (Exception) { }
        }

        // Untrusted image data, like previews: size-capped and decoded into a copy.
        static Bitmap DecodePhoto(byte[] data)
        {
            if (data.Length > 46000) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms, false, true))
                    return img.Width < 1 || img.Height < 1 || img.Width > 512 || img.Height > 512 ? null : new Bitmap(img);
            }
            catch (Exception) { return null; }
        }

        // ---- uploading ----

        public sealed class Upload
        {
            public string WidgetId; // an existing widget of yours, or null for a new one
            public string Slug;     // for a new widget: its id becomes u.<username>.<slug>
            public string Name, Category, Description, Source;
            public byte[] Preview;  // PNG, or null
        }

        // The ids of your widgets, oldest first.
        public static List<string> MyWidgets()
        {
            return Rest("GET", "widgets?select=id&owner=eq." + Uri.EscapeDataString(userId ?? "") + "&order=created_at", null)
                .Select(r => Str(r as Dictionary<string, object> ?? new Dictionary<string, object>(), "id"))
                .Where(Plugins.IsValidId).ToList();
        }

        // Sends a version for review. Returns null on success, or a sentence saying what went wrong.
        // The same rules as the website (docs/upload.html); the database enforces them all again.
        public static string Send(Upload u, out string widgetId, out int version)
        {
            widgetId = u.WidgetId;
            version = 0;
            try
            {
                if (widgetId == null)
                {
                    widgetId = "u." + Username + "." + u.Slug;
                    try { Rest("POST", "widgets", new Dictionary<string, object> { { "id", widgetId }, { "owner", userId }, { "slug", u.Slug } }); }
                    catch (HttpError e) { return "That short name can't be used: " + e.Message; }
                }
                var body = new Dictionary<string, object>
                {
                    { "widget_id", widgetId }, { "name", u.Name }, { "category", u.Category }, { "description", u.Description ?? "" },
                    { "api_version", PluginApi.Version }, { "source", u.Source },
                    { "preview", u.Preview != null ? Convert.ToBase64String(u.Preview) : null },
                };
                object[] rows = Rest("POST", "versions?select=version", body);
                var row = rows.FirstOrDefault() as Dictionary<string, object>;
                if (row != null) int.TryParse(Str(row, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                return null;
            }
            catch (HttpError e) { return "It wasn't sent: " + e.Message; }
            catch (InvalidOperationException e) { return e.Message; }
            catch (Exception) { return "The gallery couldn't be reached. Check your connection and try again."; }
        }
    }
}
