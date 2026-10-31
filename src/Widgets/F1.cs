using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;

namespace DeskWidgets.Widgets
{
    // Formula 1 data for the F1 widgets. Schedule and standings come from the Jolpica F1 API (the
    // Ergast successor; free, no account). Track outlines, results and timing come from OpenF1: free
    // once a session has been over for 30 minutes, live only with a paid OpenF1 account.
    // Everything that touches the network runs on a background thread.
    public static class F1
    {
        const string Jolpica = "https://api.jolpi.ca/ergast/f1/";
        const string OpenF1 = "https://api.openf1.org/v1/";

        public sealed class Session
        {
            public string Name, Short;
            public DateTime Start, End; // UTC
        }

        public sealed class Weekend
        {
            public int Round;
            public string Name, Place;
            public List<Session> Sessions = new List<Session>(); // in order; the race is last
            public Session Race { get { return Sessions[Sessions.Count - 1]; } }
        }

        public sealed class Standing
        {
            public int Position;
            public string Code, Family, Team;
            public double Points;
        }

        // ---------- JSON ----------
        static object Parse(string json) { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json); }

        static object Dig(object o, params string[] keys)
        {
            foreach (string k in keys)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }

        static string Str(object o, string key) { return Dig(o, key) as string; }

        static double Num(object o, string key)
        {
            object v = Dig(o, key);
            return v == null || v is string ? double.NaN : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }

        static DateTime Utc(string iso)
        {
            return DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        // ---------- schedule and standings (Jolpica) ----------
        static readonly string[][] SessionKeys =
        {
            new[] { "FirstPractice", "Practice 1", "FP1" }, new[] { "SecondPractice", "Practice 2", "FP2" },
            new[] { "ThirdPractice", "Practice 3", "FP3" }, new[] { "SprintQualifying", "Sprint Qualifying", "SQ" },
            new[] { "SprintShootout", "Sprint Shootout", "SQ" }, new[] { "Sprint", "Sprint", "Sprint" },
            new[] { "Qualifying", "Qualifying", "Quali" }
        };

        public static string FetchNextJson() { return Http.Get(Jolpica + "current/next.json"); }
        public static string FetchStandingsJson() { return Http.Get(Jolpica + "current/driverStandings.json?limit=5"); }

        // Null when the season is over.
        public static Weekend ParseNext(string json)
        {
            var races = Dig(Parse(json), "MRData", "RaceTable", "Races") as object[];
            if (races == null || races.Length == 0) return null;
            object r = races[0];
            var w = new Weekend
            {
                Round = int.Parse(Str(r, "round"), CultureInfo.InvariantCulture),
                Name = Str(r, "raceName"),
                Place = Str(Dig(r, "Circuit", "Location"), "locality")
            };
            foreach (var k in SessionKeys)
            {
                object s = Dig(r, k[0]);
                if (s != null) w.Sessions.Add(MakeSession(k[1], k[2], s));
            }
            w.Sessions.Sort((a, b) => a.Start.CompareTo(b.Start));
            w.Sessions.Add(MakeSession("Race", "Race", r));
            return w;
        }

        static Session MakeSession(string name, string shortName, object o)
        {
            DateTime start = Utc(Str(o, "date") + "T" + (Str(o, "time") ?? "00:00:00Z"));
            int minutes = name == "Race" ? 120 : name.StartsWith("Sprint Q") || name == "Sprint Shootout" ? 45 : 60;
            return new Session { Name = name, Short = shortName, Start = start, End = start.AddMinutes(minutes) };
        }

        public static List<Standing> ParseStandings(string json, out int afterRound)
        {
            object table = Dig(Parse(json), "MRData", "StandingsTable");
            var list = new List<Standing>();
            afterRound = 0;
            var lists = Dig(table, "StandingsLists") as object[];
            if (lists == null || lists.Length == 0) return list;
            int.TryParse(Str(lists[0], "round"), out afterRound);
            foreach (object d in (object[])Dig(lists[0], "DriverStandings"))
            {
                object driver = Dig(d, "Driver");
                var teams = Dig(d, "Constructors") as object[];
                string family = Str(driver, "familyName");
                list.Add(new Standing
                {
                    Position = int.Parse(Str(d, "position") ?? "0", CultureInfo.InvariantCulture),
                    Points = double.Parse(Str(d, "points"), CultureInfo.InvariantCulture),
                    Code = Str(driver, "code") ?? family.Substring(0, Math.Min(3, family.Length)).ToUpperInvariant(),
                    Family = family,
                    Team = teams != null && teams.Length > 0 ? Str(teams[teams.Length - 1], "constructorId") : ""
                });
            }
            return list;
        }

        // Team colours as the teams publish them (OpenF1 carries the same values).
        static readonly Dictionary<string, int> TeamColors = new Dictionary<string, int>
        {
            { "mclaren", 0xF47600 }, { "ferrari", 0xED1131 }, { "red_bull", 0x4781D7 }, { "mercedes", 0x00D7B6 },
            { "williams", 0x1868DB }, { "aston_martin", 0x229971 }, { "alpine", 0x00A1E8 }, { "haas", 0x9C9FA2 },
            { "rb", 0x6C98FF }, { "audi", 0xF50537 }, { "sauber", 0x52E252 }, { "cadillac", 0x909090 }
        };

        public static Color TeamColor(string constructorId)
        {
            int rgb = 0x8E8E93;
            return Color.FromArgb(unchecked((int)0xFF000000) | (TeamColors.TryGetValue(constructorId ?? "", out rgb) ? rgb : 0x8E8E93));
        }

        // ---------- OpenF1 ----------
        // OpenF1 allows 3 requests a second on the free tier: calls from every widget queue here,
        // 400 ms apart, and a 429 is retried once after a pause.
        static readonly object Gate = new object();
        static DateTime lastCall = DateTime.MinValue;

        // OpenF1 answers 404 for a query with no rows; that's an empty list, not an error.
        static object[] Rows(string query, string token)
        {
            for (int attempt = 0; ; attempt++)
            {
                lock (Gate)
                {
                    TimeSpan wait = lastCall.AddMilliseconds(400) - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero) System.Threading.Thread.Sleep(wait);
                    lastCall = DateTime.UtcNow;
                }
                try { return (Parse(Http.Get(OpenF1 + query, token)) as object[]) ?? new object[0]; }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return new object[0];
                    if (resp != null && (int)resp.StatusCode == 429 && attempt == 0) { System.Threading.Thread.Sleep(1500); continue; }
                    throw;
                }
            }
        }

        static string Stamp(DateTime utc) { return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture); }

        // OAuth2 password grant. Tokens last an hour.
        public static string SignIn(string user, string password, out DateTime expiresUtc)
        {
            object r = Parse(Http.PostForm(OpenF1.Replace("/v1/", "/token"),
                "username=" + Uri.EscapeDataString(user) + "&password=" + Uri.EscapeDataString(password)));
            double secs;
            object exp = Dig(r, "expires_in");
            if (exp == null || !double.TryParse(Convert.ToString(exp, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out secs)) secs = 3600;
            expiresUtc = DateTime.UtcNow.AddSeconds(secs - 120);
            string token = Str(r, "access_token");
            if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("No token");
            return token;
        }

        // Track outline: one clean lap of car positions from the most recent session held at this
        // race's circuit, cached for good. Starts at the timing line. Points are 0..1 on the longer
        // side, y pointing down. Null if OpenF1 has never seen a lap there.
        public static PointF[] Track(Weekend w, string cacheDir)
        {
            int circuit = -1;
            foreach (object m in Rows("meetings?year=" + w.Race.Start.Year, null))
            {
                DateTime s = Utc(Str(m, "date_start"));
                if (w.Race.Start >= s.AddDays(-1) && w.Race.Start <= s.AddDays(5)) circuit = (int)Num(m, "circuit_key");
            }
            if (circuit < 0) return null;

            string file = cacheDir == null ? null : Path.Combine(cacheDir, "f1-track-" + circuit + ".txt");
            if (file != null && File.Exists(file)) return ReadTrack(file);

            DateTime cutoff = DateTime.UtcNow.AddHours(-1);
            var sessions = Rows("sessions?circuit_key=" + circuit, null)
                .Where(s => Utc(Str(s, "date_end")) < cutoff && !(Str(s, "session_name") ?? "").StartsWith("Day"))
                .OrderByDescending(s => Utc(Str(s, "date_start"))).Take(3);
            foreach (object s in sessions)
            {
                int key = (int)Num(s, "session_key");
                object lap = Rows("laps?session_key=" + key + "&lap_number=4", null)
                    .Where(l => !double.IsNaN(Num(l, "lap_duration")) && !(Dig(l, "is_pit_out_lap") is bool && (bool)Dig(l, "is_pit_out_lap")))
                    .OrderBy(l => Num(l, "lap_duration")).FirstOrDefault();
                if (lap == null) continue;
                DateTime start = Utc(Str(lap, "date_start"));
                DateTime end = start.AddSeconds(Num(lap, "lap_duration"));
                var pts = Rows("location?session_key=" + key + "&driver_number=" + (int)Num(lap, "driver_number") +
                    "&date%3E=" + Stamp(start) + "&date%3C=" + Stamp(end), null)
                    .Select(p => new PointF((float)Num(p, "x"), (float)Num(p, "y"))).ToList();
                if (pts.Count < 50) continue;
                PointF[] track = Normalize(pts);
                if (file != null)
                {
                    Directory.CreateDirectory(cacheDir);
                    File.WriteAllLines(file, track.Select(p => p.X.ToString("0.####", CultureInfo.InvariantCulture) + "," + p.Y.ToString("0.####", CultureInfo.InvariantCulture)));
                }
                return track;
            }
            return null;
        }

        static PointF[] Normalize(List<PointF> pts)
        {
            float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            float span = Math.Max(maxX - minX, maxY - minY);
            int step = Math.Max(1, pts.Count / 160);
            var outPts = new List<PointF>();
            for (int i = 0; i < pts.Count; i += step)
                outPts.Add(new PointF((pts[i].X - minX) / span, (maxY - pts[i].Y) / span));
            return outPts.ToArray();
        }

        static PointF[] ReadTrack(string file)
        {
            return File.ReadAllLines(file).Where(l => l.Contains(",")).Select(l =>
            {
                string[] xy = l.Split(',');
                return new PointF(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }).ToArray();
        }

        // ---------- timing ----------
        public sealed class Row
        {
            public int Number, Position;
            public string Code, Family, Value;
            public Color Team;
            public char Tyre; // S M H I W, or '\0' if unknown
        }

        public sealed class Board
        {
            public string Session, Place;
            public bool Live, IsRace;
            public int Lap, TotalLaps;
            public DateTime End;     // UTC, when the session is scheduled to finish
            public bool LiveLocked;  // a session is on now, but there's no live data for it
            public bool Finished;    // live feed: the chequered flag is out
            public List<Row> Rows = new List<Row>();
        }

        // Live-session state carried between polls, so each poll only asks for what's new.
        public sealed class LiveState
        {
            public int SessionKey;
            public Dictionary<int, object> Drivers = new Dictionary<int, object>();
            public Dictionary<int, int> Position = new Dictionary<int, int>();
            public Dictionary<int, string> Interval = new Dictionary<int, string>();
            public Dictionary<int, double> Best = new Dictionary<int, double>();
            public Dictionary<int, char> Tyre = new Dictionary<int, char>();
            public DateTime PosSince = DateTime.MinValue, IntSince = DateTime.MinValue, LapSince = DateTime.MinValue, TyresAt = DateTime.MinValue;
            public int Lap;
        }

        // The session to show: one running now (live from OpenF1 with a token; LiveLocked without, for
        // F1's own feed to fill in), else the latest one OpenF1 serves free (ended over 30 minutes ago).
        public static Board FetchBoard(string token, LiveState live)
        {
            DateTime now = DateTime.UtcNow;
            var all = Rows("sessions?year=" + now.Year, null).ToList();
            if (now.Month <= 3) all.AddRange(Rows("sessions?year=" + (now.Year - 1), null));
            all = all.Where(s => !(Dig(s, "is_cancelled") is bool && (bool)Dig(s, "is_cancelled")) && !(Str(s, "session_name") ?? "").StartsWith("Day")).ToList();

            // "Running" lasts until OpenF1 serves the result for free, so live data bridges the gap.
            object running = all.FirstOrDefault(s => Utc(Str(s, "date_start")) <= now && now <= Utc(Str(s, "date_end")).AddMinutes(31));
            if (running != null && token != null) return Live(running, token, live);

            object done = all.Where(s => Utc(Str(s, "date_end")).AddMinutes(31) <= now).OrderByDescending(s => Utc(Str(s, "date_end"))).FirstOrDefault();
            if (done == null) return null;
            Board b = Final(done);
            b.LiveLocked = running != null;
            return b;
        }

        static Board NewBoard(object session)
        {
            return new Board
            {
                Session = Str(session, "session_name"), Place = Str(session, "location"),
                IsRace = Str(session, "session_type") == "Race", End = Utc(Str(session, "date_end"))
            };
        }

        static Board Final(object session)
        {
            int key = (int)Num(session, "session_key");
            Board b = NewBoard(session);
            var drivers = Rows("drivers?session_key=" + key, null).ToDictionary(d => (int)Num(d, "driver_number"));
            var tyres = LastTyres(Rows("stints?session_key=" + key, null));
            foreach (object r in Rows("session_result?session_key=" + key + "&position%3C=5", null).OrderBy(r => Num(r, "position")))
            {
                int n = (int)Num(r, "driver_number"), pos = (int)Num(r, "position");
                object d;
                drivers.TryGetValue(n, out d);
                Row row = MakeRow(n, pos, d);
                char t;
                if (tyres.TryGetValue(n, out t)) row.Tyre = t;
                row.Value = ResultValue(Dig(r, "duration"), Dig(r, "gap_to_leader"), pos, b.IsRace);
                if (Dig(r, "dnf") is bool && (bool)Dig(r, "dnf")) row.Value = "DNF";
                double laps = Num(r, "number_of_laps");
                if (pos == 1 && b.IsRace && !double.IsNaN(laps)) b.Lap = (int)laps;
                b.Rows.Add(row);
            }
            return b;
        }

        // Practice and qualifying give lap times (qualifying: one per part, the last one counts);
        // races give the gap, a number of seconds or text like "+1 LAP".
        static string ResultValue(object duration, object gap, int pos, bool race)
        {
            if (duration is object[]) duration = ((object[])duration).LastOrDefault(x => x != null);
            if (gap is object[])
            {
                var parts = (object[])gap;
                int i = Array.FindLastIndex(parts, x => x != null);
                gap = i < 0 ? null : parts[i];
            }
            if (pos == 1) return race ? "Leader" : LapTime(duration);
            if (gap is string) return (string)gap;
            if (gap == null) return "";
            return "+" + Convert.ToDouble(gap, CultureInfo.InvariantCulture).ToString("0.000", CultureInfo.InvariantCulture);
        }

        static string LapTime(object seconds)
        {
            if (seconds == null) return "";
            double s = Convert.ToDouble(seconds, CultureInfo.InvariantCulture);
            return ((int)(s / 60)) + ":" + (s % 60).ToString("00.000", CultureInfo.InvariantCulture);
        }

        static Row MakeRow(int number, int pos, object driver)
        {
            string colour = Str(driver, "team_colour");
            int rgb = 0x8E8E93;
            bool hasColour = colour != null && int.TryParse(colour, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
            if (!hasColour) rgb = 0x8E8E93; // TryParse zeroes it on failure
            string last = Str(driver, "last_name") ?? "";
            return new Row
            {
                Number = number, Position = pos,
                Code = Str(driver, "name_acronym") ?? number.ToString(CultureInfo.InvariantCulture),
                Family = last.Length > 0 ? char.ToUpper(last[0]) + last.Substring(1).ToLowerInvariant() : "",
                Team = Color.FromArgb(unchecked((int)0xFF000000) | rgb)
            };
        }

        static Dictionary<int, char> LastTyres(object[] stints)
        {
            var map = new Dictionary<int, char>();
            foreach (object s in stints.OrderBy(s => Num(s, "stint_number")))
            {
                string c = Str(s, "compound");
                if (!string.IsNullOrEmpty(c) && c != "UNKNOWN") map[(int)Num(s, "driver_number")] = c == "INTERMEDIATE" ? 'I' : c[0];
            }
            return map;
        }

        static Board Live(object session, string token, LiveState st)
        {
            int key = (int)Num(session, "session_key");
            if (st.SessionKey != key)
            {
                // New session: start over.
                var fresh = new LiveState { SessionKey = key };
                st.SessionKey = key; st.Drivers = fresh.Drivers; st.Position = fresh.Position; st.Interval = fresh.Interval;
                st.Best = fresh.Best; st.Tyre = fresh.Tyre; st.Lap = 0;
                st.PosSince = st.IntSince = st.LapSince = st.TyresAt = DateTime.MinValue;
            }
            Board b = NewBoard(session);
            b.Live = true;
            string k = "session_key=" + key;
            if (st.Drivers.Count == 0)
                foreach (object d in Rows("drivers?" + k, token)) st.Drivers[(int)Num(d, "driver_number")] = d;

            foreach (object p in Rows("position?" + k + Since(st.PosSince), token).OrderBy(p => Str(p, "date")))
            {
                st.Position[(int)Num(p, "driver_number")] = (int)Num(p, "position");
                st.PosSince = Utc(Str(p, "date"));
            }
            if (b.IsRace)
                foreach (object i in Rows("intervals?" + k + Since(st.IntSince), token).OrderBy(i => Str(i, "date")))
                {
                    object v = Dig(i, "interval");
                    st.Interval[(int)Num(i, "driver_number")] = v == null ? "" : v is string ? (string)v :
                        "+" + Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("0.000", CultureInfo.InvariantCulture);
                    st.IntSince = Utc(Str(i, "date"));
                }
            foreach (object l in Rows("laps?" + k + (st.LapSince == DateTime.MinValue ? "" : "&date_start%3E" + Stamp(st.LapSince)), token))
            {
                int n = (int)Num(l, "driver_number");
                st.Lap = Math.Max(st.Lap, (int)Num(l, "lap_number"));
                double dur = Num(l, "lap_duration");
                double best;
                if (!double.IsNaN(dur) && (!st.Best.TryGetValue(n, out best) || dur < best)) st.Best[n] = dur;
                string ds = Str(l, "date_start");
                if (ds != null && Utc(ds) > st.LapSince) st.LapSince = Utc(ds);
            }
            if (DateTime.UtcNow - st.TyresAt > TimeSpan.FromSeconds(30))
            {
                st.Tyre = LastTyres(Rows("stints?" + k, token));
                st.TyresAt = DateTime.UtcNow;
            }

            double fastest = st.Best.Count > 0 ? st.Best.Values.Min() : double.NaN;
            foreach (var kv in st.Position.Where(kv => kv.Value <= 5).OrderBy(kv => kv.Value))
            {
                object d;
                st.Drivers.TryGetValue(kv.Key, out d);
                Row row = MakeRow(kv.Key, kv.Value, d);
                char t;
                if (st.Tyre.TryGetValue(kv.Key, out t)) row.Tyre = t;
                string iv;
                double best;
                if (b.IsRace) row.Value = kv.Value == 1 ? "Leader" : st.Interval.TryGetValue(kv.Key, out iv) ? iv : "";
                else if (st.Best.TryGetValue(kv.Key, out best))
                    row.Value = kv.Value == 1 || best == fastest ? LapTime(best) : "+" + (best - fastest).ToString("0.000", CultureInfo.InvariantCulture);
                else row.Value = "";
                b.Rows.Add(row);
            }
            b.Lap = st.Lap;
            return b;
        }

        static string Since(DateTime utc) { return utc == DateTime.MinValue ? "" : "&date%3E" + Stamp(utc); }
    }
}
