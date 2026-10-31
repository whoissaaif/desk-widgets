using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskWidgets.Widgets
{
    // F1's own live timing feed, the one F1's website and app use, read without an F1 TV login.
    // Public so community widgets (F1TimingWidget) can use it.
    // It's SignalR Core (JSON hub protocol) over a WebSocket: negotiate, connect, subscribe to the
    // topics, then merge each partial update into the topic's state. Unofficial and undocumented:
    // without a login F1 may send only part of the data, and it can change or stop at any time,
    // so the timing widget falls back to OpenF1 results when this yields nothing.
    public sealed class F1LiveFeed : IDisposable
    {
        const string Endpoint = "livetiming.formula1.com/signalrcore";
        const char RecordSeparator = '\u001e';
        static readonly string[] Topics = { "SessionInfo", "SessionStatus", "DriverList", "TimingData", "TimingAppData", "LapCount" };

        // Topic -> state. Lists are stored as dictionaries keyed "0", "1"… because updates address
        // list items by index that way.
        readonly Dictionary<string, Dictionary<string, object>> state = new Dictionary<string, Dictionary<string, object>>();
        readonly object sync = new object();
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        int version;
        volatile bool running = true;

        public F1LiveFeed()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            Task.Factory.StartNew(() => Run().Wait(), TaskCreationOptions.LongRunning);
        }

        // Bumped on every update, so the widget only redraws when something changed.
        public int Version { get { return version; } }
        public bool Running { get { return running; } }
        public DateTime StoppedUtc { get; private set; }
        public string Error { get; private set; }

        async Task Run()
        {
            try
            {
                var cookies = new CookieContainer();
                // Negotiate first: it sets the load balancer cookie the socket has to carry.
                var req = (HttpWebRequest)WebRequest.Create("https://" + Endpoint + "/negotiate?negotiateVersion=1");
                req.Method = "POST";
                req.ContentLength = 0;
                req.UserAgent = "DeskWidgets";
                req.CookieContainer = cookies;
                string negotiated;
                using (var resp = await req.GetResponseAsync())
                using (var reader = new StreamReader(resp.GetResponseStream())) negotiated = await reader.ReadToEndAsync();
                var neg = (Dictionary<string, object>)json.DeserializeObject(negotiated);
                object id;
                if (!neg.TryGetValue("connectionToken", out id)) id = neg["connectionId"];

                using (var ws = new ClientWebSocket())
                {
                    ws.Options.Cookies = cookies;
                    await ws.ConnectAsync(new Uri("wss://" + Endpoint + "?id=" + Uri.EscapeDataString((string)id)), cts.Token);
                    await Send(ws, "{\"protocol\":\"json\",\"version\":1}");
                    await Send(ws, "{\"type\":1,\"invocationId\":\"0\",\"target\":\"Subscribe\",\"arguments\":[" + json.Serialize(Topics) + "]}");
                    Task pings = Ping(ws);
                    await Receive(ws);
                    await pings;
                }
            }
            catch (Exception ex) { Error = (ex.InnerException ?? ex).Message; }
            finally
            {
                StoppedUtc = DateTime.UtcNow;
                running = false;
            }
        }

        Task Send(ClientWebSocket ws, string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message + RecordSeparator);
            return ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }

        // The server drops clients that stay quiet for 30 s.
        async Task Ping(ClientWebSocket ws)
        {
            try
            {
                while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    await Task.Delay(15000, cts.Token);
                    await Send(ws, "{\"type\":6}");
                }
            }
            catch (Exception) { }
        }

        async Task Receive(ClientWebSocket ws)
        {
            var buffer = new byte[64 * 1024];
            var text = new StringBuilder();
            using (var message = new MemoryStream())
                while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
                {
                    WebSocketReceiveResult r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    text.Append(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                    // One WebSocket message can hold several records, or part of one.
                    string all = text.ToString();
                    int end = all.LastIndexOf(RecordSeparator);
                    if (end < 0) continue;
                    text.Clear().Append(all, end + 1, all.Length - end - 1);
                    foreach (string record in all.Substring(0, end).Split(RecordSeparator))
                        if (record.Length > 0 && Handle(record)) return;
                }
        }

        // True when the server closed the hub.
        bool Handle(string record)
        {
            var msg = json.DeserializeObject(record) as Dictionary<string, object>;
            object type;
            if (msg == null || !msg.TryGetValue("type", out type)) return false; // the handshake reply
            int t = Convert.ToInt32(type, CultureInfo.InvariantCulture);
            if (t == 7) return true;
            lock (sync)
            {
                object payload;
                if (t == 3 && msg.TryGetValue("result", out payload) && payload is Dictionary<string, object>)
                {
                    // The subscription's answer: the current state of every topic.
                    foreach (var kv in (Dictionary<string, object>)payload)
                    {
                        var d = Normalize(kv.Value) as Dictionary<string, object>;
                        if (d != null) state[kv.Key] = d;
                    }
                }
                else if (t == 1 && msg.TryGetValue("arguments", out payload) && payload is object[] && ((object[])payload).Length >= 2)
                {
                    var args = (object[])payload;
                    string topic = args[0] as string;
                    var update = Normalize(args[1]) as Dictionary<string, object>;
                    if (topic == null || update == null) return false;
                    Dictionary<string, object> target;
                    if (!state.TryGetValue(topic, out target)) state[topic] = target = new Dictionary<string, object>();
                    Merge(target, update);
                }
                else return false;
            }
            Interlocked.Increment(ref version);
            return false;
        }

        static object Normalize(object o)
        {
            var list = o as object[];
            if (list != null)
            {
                var d = new Dictionary<string, object>();
                for (int i = 0; i < list.Length; i++) d[i.ToString(CultureInfo.InvariantCulture)] = Normalize(list[i]);
                return d;
            }
            var dict = o as Dictionary<string, object>;
            if (dict != null)
                foreach (string k in dict.Keys.ToList()) dict[k] = Normalize(dict[k]);
            return o;
        }

        static void Merge(Dictionary<string, object> target, Dictionary<string, object> update)
        {
            foreach (var kv in update)
            {
                object cur;
                var sub = kv.Value as Dictionary<string, object>;
                if (sub != null && target.TryGetValue(kv.Key, out cur) && cur is Dictionary<string, object>)
                    Merge((Dictionary<string, object>)cur, sub);
                else target[kv.Key] = kv.Value;
            }
        }

        // ---------- reading the state ----------
        static object Get(object o, params string[] keys)
        {
            foreach (string k in keys)
            {
                var d = o as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }

        static string Text(object o, params string[] keys)
        {
            object v = Get(o, keys);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static bool Flag(object o, string key) { return Get(o, key) is bool && (bool)Get(o, key); }

        // "1:17.827" or "17.827" -> seconds; NaN if it isn't a time.
        static double Seconds(string s)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;
            string[] p = s.Split(':');
            double v, m = 0;
            if (!double.TryParse(p[p.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return double.NaN;
            if (p.Length > 1 && !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return double.NaN;
            return m * 60 + v;
        }

        // The top five as a board, or null if the feed hasn't sent timing (yet, or at all).
        public F1.Board Snapshot()
        {
            lock (sync)
            {
                Dictionary<string, object> info, timing, drivers, app, laps, status;
                state.TryGetValue("SessionInfo", out info);
                state.TryGetValue("TimingData", out timing);
                state.TryGetValue("DriverList", out drivers);
                state.TryGetValue("TimingAppData", out app);
                state.TryGetValue("LapCount", out laps);
                state.TryGetValue("SessionStatus", out status);
                var lines = Get(timing, "Lines") as Dictionary<string, object>;
                if (info == null || lines == null || lines.Count == 0) return null;

                var b = new F1.Board
                {
                    Session = Text(info, "Name") ?? "Session",
                    Place = Text(info, "Meeting", "Location") ?? Text(info, "Meeting", "Name") ?? "",
                    IsRace = Text(info, "Type") == "Race",
                    Finished = Text(status, "Status") == "Finished" || Text(status, "Status") == "Finalised" || Text(status, "Status") == "Ends"
                };
                b.Live = !b.Finished; // after the flag it reads as a final result
                int lap;
                if (int.TryParse(Text(laps, "CurrentLap"), out lap)) b.Lap = lap;
                if (int.TryParse(Text(laps, "TotalLaps"), out lap)) b.TotalLaps = lap;

                var ordered = new List<KeyValuePair<int, string>>();
                foreach (var kv in lines)
                {
                    int pos;
                    if (int.TryParse(Text(kv.Value, "Position"), out pos) && pos > 0) ordered.Add(new KeyValuePair<int, string>(pos, kv.Key));
                }
                ordered.Sort((x, y) => x.Key.CompareTo(y.Key));
                double fastest = double.NaN;
                foreach (var p in ordered.Take(5))
                {
                    double s = Seconds(Text(lines[p.Value], "BestLapTime", "Value"));
                    if (!double.IsNaN(s) && (double.IsNaN(fastest) || s < fastest)) fastest = s;
                }

                foreach (var p in ordered.Take(5))
                {
                    object line = lines[p.Value], driver = Get(drivers, p.Value);
                    int rgb = 0x8E8E93;
                    string colour = Text(driver, "TeamColour");
                    if (colour != null) int.TryParse(colour, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
                    string last = Text(driver, "LastName") ?? "";
                    var row = new F1.Row
                    {
                        Position = p.Key,
                        Code = Text(driver, "Tla") ?? p.Value,
                        Family = last.Length > 0 ? char.ToUpper(last[0]) + last.Substring(1).ToLowerInvariant() : "",
                        Team = Color.FromArgb(unchecked((int)0xFF000000) | rgb)
                    };
                    int.TryParse(p.Value, out row.Number);

                    var stints = Get(app, "Lines", p.Value, "Stints") as Dictionary<string, object>;
                    if (stints != null && stints.Count > 0)
                    {
                        string c = Text(stints[stints.Keys.OrderBy(k => int.Parse(k, CultureInfo.InvariantCulture)).Last()], "Compound");
                        if (!string.IsNullOrEmpty(c) && c != "UNKNOWN") row.Tyre = c == "INTERMEDIATE" ? 'I' : c[0];
                    }

                    if (Flag(line, "Retired") || Flag(line, "Stopped")) row.Value = "Out";
                    else if (b.IsRace)
                        row.Value = p.Key == 1 ? "Leader" : Text(line, "IntervalToPositionAhead", "Value") ?? Text(line, "GapToLeader") ?? "";
                    else
                    {
                        string best = Text(line, "BestLapTime", "Value");
                        double s = Seconds(best);
                        row.Value = double.IsNaN(s) ? "" : p.Key == 1 || s == fastest ? best
                            : "+" + (s - fastest).ToString("0.000", CultureInfo.InvariantCulture);
                    }
                    b.Rows.Add(row);
                }
                return b.Rows.Count > 0 ? b : null;
            }
        }

        public void Dispose()
        {
            cts.Cancel();
        }
    }
}
