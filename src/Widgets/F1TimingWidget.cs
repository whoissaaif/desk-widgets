using System;
using System.Drawing;
using System.Globalization;
using System.Net;

namespace DeskWidgets.Widgets
{
    // Medium: the top five of the current F1 session, with tyre and gap. While a session runs it
    // follows F1's own live timing feed (F1LiveFeed, no account); between sessions it shows the
    // latest results, which OpenF1 serves free 30 minutes after a session ends. A paid OpenF1
    // account is an optional alternative for live data (polled every 6 s, paused while the widget
    // is hidden); its password is kept encrypted with Windows DPAPI.
    sealed class F1TimingWidget : CardWidget
    {
        const int LiveMs = 6000, IdleMs = 10 * 60 * 1000, LockedMs = 5 * 60 * 1000;

        F1.Board board;
        string status;
        bool busy;
        readonly F1.LiveState live = new F1.LiveState();
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();

        // F1's feed, connected only while a session is on. The tick turns its state into a board
        // at most once a second, and only when something changed and the widget can be seen.
        F1LiveFeed feed;
        F1.Board feedBoard;
        int feedVersion;
        readonly System.Windows.Forms.Timer feedTick = new System.Windows.Forms.Timer { Interval = 1000 };

        // One OpenF1 token for every timing widget; it lasts an hour.
        static readonly object TokenLock = new object();
        static string token;
        static DateTime tokenExpires;

        public F1TimingWidget(Section settings) : base(settings, true)
        {
            poll.Tick += delegate { poll.Stop(); Fetch(); };
            feedTick.Tick += delegate { FeedTick(); };
        }

        bool SignedIn { get { return Settings.Get("of1-pass", "").Length > 0; } }

        protected override void OnStart()
        {
            Fetch();
        }

        // ---------- data ----------
        static string Token(string user, string password, bool renew)
        {
            lock (TokenLock)
            {
                if (renew || token == null || DateTime.UtcNow > tokenExpires) token = F1.SignIn(user, password, out tokenExpires);
                return token;
            }
        }

        static bool Unauthorized(Exception ex)
        {
            var resp = ex is WebException ? ((WebException)ex).Response as HttpWebResponse : null;
            return resp != null && (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden);
        }

        void Fetch()
        {
            if (busy) return;
            // Live and nobody can see it: don't spend requests, look again next tick.
            if (board != null && board.Live && IsCovered()) { Schedule(LiveMs); return; }
            busy = true;
            string user = Settings.GetText("of1-user", "");
            string password = SignedIn ? Secrets.Unprotect(Settings.Get("of1-pass", "")) : null;
            bool[] signInFailed = { false };
            Background(() =>
            {
                string tok = null;
                if (password != null)
                    try { tok = Token(user, password, false); }
                    catch (Exception) { signInFailed[0] = true; }
                try { return F1.FetchBoard(tok, live); }
                catch (Exception ex)
                {
                    if (tok == null || !Unauthorized(ex)) throw;
                    return F1.FetchBoard(Token(user, password, true), live); // token expired early
                }
            }, (b, error) =>
            {
                busy = false;
                if (error != null) status = "Offline";
                else
                {
                    board = b;
                    status = signInFailed[0] ? "OpenF1 sign-in failed" : null;
                    if (b != null && b.LiveLocked) StartFeed(); else StopFeed();
                }
                Render();
                Schedule(board != null && board.Live ? LiveMs : board != null && board.LiveLocked ? LockedMs : error != null ? LockedMs : IdleMs);
            });
        }

        void StartFeed()
        {
            if (feed != null) return;
            feed = new F1LiveFeed();
            feedVersion = 0;
            feedTick.Start();
        }

        void StopFeed()
        {
            if (feed == null) return;
            feedTick.Stop();
            feed.Dispose();
            feed = null;
            feedBoard = null;
        }

        void FeedTick()
        {
            if (feed == null) return;
            if (!feed.Running)
            {
                // Dropped: reconnect after half a minute.
                if (DateTime.UtcNow - feed.StoppedUtc > TimeSpan.FromSeconds(30))
                {
                    var old = feed;
                    feed = new F1LiveFeed();
                    feedVersion = 0;
                    old.Dispose();
                }
                return;
            }
            if (feed.Version == feedVersion || IsCovered()) return;
            feedVersion = feed.Version;
            feedBoard = feed.Snapshot();
            Render();
        }

        void Schedule(int ms)
        {
            poll.Stop();
            poll.Interval = ms;
            poll.Start();
        }

        void SignIn()
        {
            var values = InputDialog.Ask("OpenF1 account",
                "Live timing comes from F1's own feed, no account needed. If that stops working, a paid OpenF1 account " +
                "(openf1.org) is the alternative: sign in and the widget uses it instead. Your password is stored " +
                "encrypted for your Windows account only.",
                new[]
                {
                    new InputDialog.Field { Label = "Username", Value = Settings.GetText("of1-user", "") },
                    new InputDialog.Field { Label = "Password", Secret = true }
                }, "Sign In");
            if (values == null || values[0].Trim().Length == 0 || values[1].Length == 0) return;
            string user = values[0].Trim(), password = values[1];
            status = "Signing in…";
            Render();
            Background(() => Token(user, password, true), (tok, error) =>
            {
                if (error != null) { status = Unauthorized(error) ? "Wrong username or password" : "Offline"; Render(); return; }
                Settings.SetText("of1-user", user);
                Settings.Set("of1-pass", Secrets.Protect(password));
                status = null;
                Fetch();
            });
        }

        void SignOut()
        {
            Settings.Remove("of1-pass");
            lock (TokenLock) token = null;
            Fetch();
        }

        // ---------- interaction ----------
        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            if (SignedIn) menu.Add("Sign Out of OpenF1", SignOut);
            else menu.Add("Sign In to OpenF1…", SignIn);
            menu.Add("Refresh Now", Fetch);
        }

        // ---------- drawing ----------
        protected override void DrawContent(Graphics g, Palette pal)
        {
            F1.Board b = !IsHandleCreated ? Sample() : feedBoard ?? board;
            float m = P(16);
            if (b == null || b.Rows.Count == 0)
            {
                string msg = status ?? (b == null ? "Loading timing…" : "No results yet");
                Label(g, msg, F(15, "semibold"), pal.Fg, W / 2, H / 2 - P(12), W - m * 2, StringAlignment.Center);
                return;
            }

            // Header: LIVE pill and place, or the session that's shown.
            float y = P(14), x = m;
            Font head = F(13, "semibold"), cap = F(11, "semibold");
            if (b.Live)
            {
                float pw = TextWidth(g, "LIVE", cap) + P(12);
                FillRound(g, pal.Red, new RectangleF(x, y, pw, P(17)), P(8.5f));
                Label(g, "LIVE", cap, pal.OnAccent, x + pw / 2, y + P(2), pw, StringAlignment.Center);
                x += pw + P(8);
                Label(g, b.Place + (b.IsRace ? "" : " · " + b.Session), head, pal.Fg, x, y, W - x - m - P(70), StringAlignment.Near);
            }
            else
            {
                Label(g, b.Session, head, pal.Fg, x, y, W * 0.45f, StringAlignment.Near);
                x += Math.Min(W * 0.45f, TextWidth(g, b.Session, head)) + P(6);
                Label(g, b.Place, F(13, "regular"), pal.Fg2, x, y, W - x - m - P(110), StringAlignment.Near);
            }
            string right = status != null ? status
                : b.Live ? (b.IsRace && b.Lap > 0 ? "Lap " + b.Lap + (b.TotalLaps > 0 ? "/" + b.TotalLaps : "") : "")
                : b.LiveLocked ? (feed != null && feed.Running ? "Connecting to live…" : "Live unavailable")
                : b.IsRace && b.Lap > 0 ? b.Lap + " laps" : "Final";
            Label(g, right, b.Live ? head : F(11, "regular"), b.Live && status == null ? pal.Fg : pal.Fg2, W - m, b.Live ? y : y + P(2), P(130), StringAlignment.Far);

            float y0 = P(40), rh = P(22.8f);
            Font body = F(13, "regular"), bold = F(13, "semibold");
            float tyreX = W - m - P(80);
            for (int i = 0; i < Math.Min(5, b.Rows.Count); i++)
            {
                F1.Row r = b.Rows[i];
                float ry = y0 + rh * i;
                if (i > 0) using (var pen = new Pen(pal.Sep, 1f)) g.DrawLine(pen, m + P(27), ry - P(2), W - m, ry - P(2));
                Label(g, r.Position.ToString(CultureInfo.CurrentCulture), bold, pal.Fg2, m + P(12), ry + P(1), P(20), StringAlignment.Far);
                FillRound(g, IsVibrant ? pal.Fg : r.Team, new RectangleF(m + P(18), ry + P(3), P(3), P(14)), P(1.5f));
                Label(g, r.Code, bold, pal.Fg, m + P(27), ry + P(1), P(40), StringAlignment.Near);
                Label(g, r.Family, body, pal.Fg2, m + P(64), ry + P(1), tyreX - P(14) - (m + P(64)), StringAlignment.Near);
                if (r.Tyre != '\0') DrawTyre(g, pal, r.Tyre, tyreX, ry + P(10));
                Label(g, r.Value, bold, pal.Fg, W - m, ry + P(1), P(66), StringAlignment.Far);
            }
        }

        // Compound as a coloured ring with its letter, so the colour never stands alone.
        void DrawTyre(Graphics g, Palette pal, char c, float cx, float cy)
        {
            Color ring = c == 'S' ? pal.Red : c == 'M' ? pal.Yellow : c == 'I' ? pal.Green : c == 'W' ? pal.Blue
                : IsDark ? pal.Fg : pal.Fg2; // hard is white; on a light card it needs the grey
            float r = P(8.5f);
            using (var pen = new Pen(ring, P(2f))) g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            Label(g, c.ToString(), F(11, "semibold"), pal.Fg, cx, cy - P(7), r * 2, StringAlignment.Center);
        }

        // ---------- library preview ----------
        static F1.Board Sample()
        {
            var b = new F1.Board { Session = "Race", Place = "Mexico City", Live = true, IsRace = true, Lap = 42 };
            object[][] rows =
            {
                new object[] { "ANT", "Antonelli", "mercedes", 'M', "Leader" }, new object[] { "RUS", "Russell", "mercedes", 'H', "+1.842" },
                new object[] { "HAM", "Hamilton", "ferrari", 'M', "+3.106" }, new object[] { "NOR", "Norris", "mclaren", 'H', "+0.874" },
                new object[] { "LEC", "Leclerc", "ferrari", 'S', "+5.411" }
            };
            for (int i = 0; i < rows.Length; i++)
                b.Rows.Add(new F1.Row
                {
                    Position = i + 1, Code = (string)rows[i][0], Family = (string)rows[i][1],
                    Team = F1.TeamColor((string)rows[i][2]), Tyre = (char)rows[i][3], Value = (string)rows[i][4]
                });
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopFeed(); poll.Dispose(); feedTick.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
