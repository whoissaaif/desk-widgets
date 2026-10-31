using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using DeskWidgets;
using DeskWidgets.Widgets;

[DeskWidget(Name = "GitHub", Description = "Profile, streak and contribution graph")]
public sealed class GitHubWidget : CardWidget
{
    const int Weeks = 30;
    const string Query = "query { viewer { login name avatarUrl(size: 96) followers { totalCount } " +
        "repositories(ownerAffiliations: OWNER) { totalCount } contributionsCollection { contributionCalendar { " +
        "totalContributions weeks { contributionDays { date contributionCount contributionLevel } } } } } }";

    sealed class Data
    {
        public string Login, Name;
        public int Repos, Followers, Total;
        public List<int> Counts = new List<int>(), Levels = new List<int>();
        public List<int> FullYear = new List<int>();

        public int FullYearStreak
        {
            get
            {
                int i = FullYear.Count - 1, s = 0;
                if (i >= 0 && FullYear[i] == 0) i--;
                while (i >= 0 && FullYear[i] > 0) { s++; i--; }
                return s;
            }
        }
    }

    Data data;
    Bitmap avatar;
    string status;
    bool needsToken, busy;
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };

    public GitHubWidget(Section settings) : base(settings, true)
    {
        LoadCache();
        refresh.Tick += delegate { Fetch(); };
    }

    static string CacheDir { get { return Path.Combine(PluginApi.DataDir, "cache"); } }
    static string CacheJson { get { return Path.Combine(CacheDir, "github.json"); } }
    static string CacheAvatar { get { return Path.Combine(CacheDir, "github-avatar.png"); } }

    protected override void OnStart()
    {
        refresh.Start();
        Fetch();
    }

    void LoadCache()
    {
        try
        {
            if (File.Exists(CacheJson)) data = Parse(File.ReadAllText(CacheJson));
            if (File.Exists(CacheAvatar)) avatar = LoadBitmap(File.ReadAllBytes(CacheAvatar));
        }
        catch (Exception) { data = null; }
    }

    void Fetch()
    {
        if (busy) return;
        busy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string json = null, error = null, avatarUrl = null;
            byte[] avatarBytes = null;
            bool tokenProblem = false;
            try
            {
                string token = Token();
                if (token == null) tokenProblem = true;
                else
                {
                    json = Post(token, new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "query", Query } }));
                    avatarUrl = AvatarUrl(json);
                    if (avatarUrl != null) avatarBytes = Download(avatarUrl);
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.Unauthorized) tokenProblem = true;
                else error = "Offline";
            }
            catch (Exception) { error = "Couldn't refresh"; }

            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate
            {
                busy = false;
                needsToken = tokenProblem;
                status = tokenProblem ? "Click to connect" : error;
                if (json != null)
                {
                    try
                    {
                        data = Parse(json);
                        Directory.CreateDirectory(CacheDir);
                        File.WriteAllText(CacheJson, json);
                        if (avatarBytes != null)
                        {
                            File.WriteAllBytes(CacheAvatar, avatarBytes);
                            if (avatar != null) avatar.Dispose();
                            avatar = LoadBitmap(avatarBytes);
                        }
                    }
                    catch (Exception) { status = "Couldn't read GitHub's reply"; }
                }
                Render();
            }); }
            catch (InvalidOperationException) { }
        });
    }

    string Token()
    {
        string saved = Settings.Get("token", "");
        if (saved.Length > 0)
        {
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception) { return null; }
        }
        return Settings.Get("auth", "") == "gh" ? GhCliToken() : null;
    }

    static bool GhCliInstalled
    {
        get
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Length > 0 && File.Exists(Path.Combine(dir.Trim(), "gh.exe"))) return true; }
                catch (ArgumentException) { }
            }
            return false;
        }
    }

    static string GhCliToken()
    {
        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return p.ExitCode == 0 && output.Length > 0 ? output : null;
            }
        }
        catch (Exception) { return null; }
    }

    static string Post(string token, string body)
    {
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/graphql");
        req.Method = "POST";
        req.UserAgent = "DeskWidgets";
        req.ContentType = "application/json";
        req.Timeout = 20000;
        req.Headers["Authorization"] = "bearer " + token;
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
        using (var resp = req.GetResponse())
        using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            return r.ReadToEnd();
    }

    static byte[] Download(string url)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = "DeskWidgets";
        req.Timeout = 20000;
        using (var resp = req.GetResponse())
        using (var s = resp.GetResponseStream())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static Dictionary<string, object> Viewer(string json)
    {
        var root = (Dictionary<string, object>)new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(json);
        return (Dictionary<string, object>)((Dictionary<string, object>)root["data"])["viewer"];
    }

    static string AvatarUrl(string json)
    {
        return Viewer(json)["avatarUrl"] as string;
    }

    static int Count(Dictionary<string, object> o, string key)
    {
        return Convert.ToInt32(((Dictionary<string, object>)o[key])["totalCount"]);
    }

    static Data Parse(string json)
    {
        var v = Viewer(json);
        var cal = (Dictionary<string, object>)((Dictionary<string, object>)v["contributionsCollection"])["contributionCalendar"];
        var d = new Data
        {
            Login = (string)v["login"], Name = (v["name"] as string) ?? (string)v["login"],
            Repos = Count(v, "repositories"), Followers = Count(v, "followers"),
            Total = Convert.ToInt32(cal["totalContributions"])
        };
        var weeks = (object[])cal["weeks"];
        for (int w = Math.Max(0, weeks.Length - Weeks); w < weeks.Length; w++)
            foreach (Dictionary<string, object> day in (object[])((Dictionary<string, object>)weeks[w])["contributionDays"])
            {
                d.Counts.Add(Convert.ToInt32(day["contributionCount"]));
                d.Levels.Add(LevelOf((string)day["contributionLevel"]));
            }
        var all = new List<int>();
        foreach (Dictionary<string, object> week in weeks)
            foreach (Dictionary<string, object> day in (object[])week["contributionDays"])
                all.Add(Convert.ToInt32(day["contributionCount"]));
        d.FullYear = all;
        return d;
    }

    static int LevelOf(string level)
    {
        switch (level)
        {
            case "FIRST_QUARTILE": return 1;
            case "SECOND_QUARTILE": return 2;
            case "THIRD_QUARTILE": return 3;
            case "FOURTH_QUARTILE": return 4;
            default: return 0;
        }
    }

    static Bitmap LoadBitmap(byte[] bytes)
    {
        using (var ms = new MemoryStream(bytes))
        using (var img = Image.FromStream(ms))
            return new Bitmap(img);
    }

    void Connect()
    {
        bool useCli = false;
        bool cli = GhCliInstalled;
        var values = InputDialog.Ask("Connect GitHub",
            "Paste a GitHub personal access token (github.com/settings/tokens). No scopes are needed for public " +
            "contributions; add read:user to include private ones. It's stored encrypted for your Windows account only." +
            (cli ? " Or use the account you're signed in to with the GitHub CLI." : ""),
            new[] { new InputDialog.Field { Label = "Personal access token", Secret = true } }, "Connect",
            cli ? "Use my GitHub CLI login" : null,
            d => { useCli = true; d.DialogResult = System.Windows.Forms.DialogResult.OK; });
        if (useCli)
        {
            Settings.Remove("token");
            Settings.Set("auth", "gh");
            Fetch();
            return;
        }
        if (values == null || values[0].Trim().Length == 0) return;
        byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(values[0].Trim()), null, DataProtectionScope.CurrentUser);
        Settings.Set("token", Convert.ToBase64String(enc));
        Settings.Remove("auth");
        Fetch();
    }

    bool Connected { get { return Settings.Get("token", "").Length > 0 || Settings.Get("auth", "") == "gh"; } }

    protected override void OnWidgetClick(Point p)
    {
        if (needsToken || data == null) { Connect(); return; }
        try { Process.Start("https://github.com/" + data.Login); }
        catch (Exception) { }
    }

    protected override void BuildWidgetMenu(PopupMenu menu)
    {
        menu.Add("Refresh Now", Fetch);
        if (data != null) menu.Add("Open Profile on GitHub", delegate { OnWidgetClick(Point.Empty); });
        menu.Add(Connected ? "Change Account…" : "Connect GitHub…", Connect);
        if (Connected)
            menu.AddDestructive("Disconnect", delegate { Settings.Remove("token"); Settings.Remove("auth"); Fetch(); });
    }

    static readonly Color[] DarkLevels = { Rgb(0x2C2C2E), Rgb(0x0E4429), Rgb(0x006D32), Rgb(0x26A641), Rgb(0x39D353) };
    static readonly Color[] LightLevels = { Rgb(0xEBEDF0), Rgb(0x9BE9A8), Rgb(0x40C463), Rgb(0x30A14E), Rgb(0x216E39) };
    static readonly Color[] VibrantLevels =
    {
        Color.FromArgb(36, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), Color.FromArgb(140, 255, 255, 255),
        Color.FromArgb(195, 255, 255, 255), Color.FromArgb(245, 255, 255, 255)
    };

    protected override void DrawContent(Graphics g, Palette pal)
    {
        Data d = data ?? (IsHandleCreated ? null : Sample());
        float m = P(16);
        if (d == null)
        {
            Label(g, needsToken ? "Connect GitHub" : "Loading GitHub…", F(15, "semibold"), pal.Fg, W / 2, H / 2 - P(22), W - m * 2, StringAlignment.Center);
            Label(g, needsToken ? "Click to add a token, or sign in with the GitHub CLI." : (status ?? "Fetching your contributions"),
                F(11, "regular"), pal.Fg2, W / 2, H / 2 + P(2), W - m * 2, StringAlignment.Center);
            return;
        }

        float av = P(34);
        if (avatar != null)
            using (var clip = new GraphicsPath())
            {
                clip.AddEllipse(m, m, av, av);
                var saved = g.Save();
                g.SetClip(clip);
                if (IsVibrant) DrawGray(g, avatar, new RectangleF(m, m, av, av)); else g.DrawImage(avatar, new RectangleF(m, m, av, av));
                g.Restore(saved);
            }
        else FillCircle(g, pal.Fill, m + av / 2, m + av / 2, av / 2);

        float streakW = P(70);
        float tx = m + av + P(9), textW = W - tx - m - streakW;
        Label(g, d.Name, F(13, "semibold"), pal.Fg, tx, m + P(1), textW, StringAlignment.Near);
        Label(g, "@" + d.Login + " · " + d.Repos + " repos · " + d.Followers + " followers", F(11, "regular"), pal.Fg2, tx, m + P(18), textW, StringAlignment.Near);

        string streak = d.FullYearStreak.ToString();
        Font big = F(22, "semibold");
        float numW = TextWidth(g, streak, big);
        Label(g, streak, big, pal.Fg, W - m, m - P(4), streakW, StringAlignment.Far);
        Flame(g, pal.Orange, W - m - numW - P(15), m + P(2), P(13));
        Label(g, "day streak", F(11, "regular"), pal.Fg2, W - m, m + P(23), streakW, StringAlignment.Far);

        Color[] levels = IsVibrant ? VibrantLevels : IsDark ? DarkLevels : LightLevels;
        float gap = P(2.2f), top = P(60), cell = (W - m * 2 - gap * (Weeks - 1)) / Weeks, r = Math.Max(1f, P(2));
        int n = d.Levels.Count;
        for (int i = 0; i < n; i++)
        {
            float x = m + (i / 7) * (cell + gap), y = top + (i % 7) * (cell + gap);
            FillRound(g, levels[d.Levels[i]], new RectangleF(x, y, cell, cell), r);
            if (i == n - 1)
                using (var pen = new Pen(pal.Fg, Math.Max(1f, P(1.2f))))
                using (var path = RoundRect(new RectangleF(x, y, cell, cell), r))
                    g.DrawPath(pen, path);
        }

        float fy = top + 7 * cell + 6 * gap + P(6);
        Label(g, d.Total.ToString("N0") + " contributions in the last year", F(11, "regular"), pal.Fg2, m, fy, W * 0.66f, StringAlignment.Near);
        string right = status ?? (d.Counts.Count > 0 ? d.Counts[d.Counts.Count - 1] + " today" : "");
        Label(g, right, F(11, status != null ? "semibold" : "regular"), status != null ? pal.Orange : pal.Fg2, W - m, fy, W * 0.34f, StringAlignment.Far);
    }

    static void Flame(Graphics g, Color c, float x, float y, float size)
    {
        float k = size / 24f;
        using (var p = new GraphicsPath())
        {
            p.AddBezier(x + 12 * k, y + 2 * k, x + 12.5f * k, y + 6 * k, x + 19 * k, y + 8.5f * k, x + 19 * k, y + 14.5f * k);
            p.AddBezier(x + 19 * k, y + 14.5f * k, x + 19 * k, y + 18.6f * k, x + 15.9f * k, y + 22 * k, x + 12 * k, y + 22 * k);
            p.AddBezier(x + 12 * k, y + 22 * k, x + 8.1f * k, y + 22 * k, x + 5 * k, y + 18.9f * k, x + 5 * k, y + 15 * k);
            p.AddBezier(x + 5 * k, y + 15 * k, x + 5 * k, y + 11.5f * k, x + 7.5f * k, y + 9.5f * k, x + 8.5f * k, y + 7.5f * k);
            p.AddBezier(x + 8.5f * k, y + 7.5f * k, x + 9 * k, y + 10 * k, x + 10.2f * k, y + 11 * k, x + 11 * k, y + 11.2f * k);
            p.AddBezier(x + 11 * k, y + 11.2f * k, x + 10.6f * k, y + 7.5f * k, x + 11.2f * k, y + 4.5f * k, x + 12 * k, y + 2 * k);
            p.CloseFigure();
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }

    static Data Sample()
    {
        var d = new Data { Login = "you", Name = "Your GitHub", Repos = 12, Followers = 8, Total = 640 };
        var rnd = new Random(7);
        for (int i = 0; i < Weeks * 7; i++)
        {
            int lvl = rnd.Next(10) < 4 ? 0 : rnd.Next(1, 5);
            d.Levels.Add(lvl);
            d.Counts.Add(lvl * 3);
        }
        d.FullYear = d.Counts;
        return d;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose();
            if (avatar != null) { avatar.Dispose(); avatar = null; }
        }
        base.Dispose(disposing);
    }
}
