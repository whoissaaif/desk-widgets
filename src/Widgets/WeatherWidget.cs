using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace DeskWidgets.Widgets
{
    // Medium: current temperature, conditions, high/low and the next hours, from Open-Meteo
    // (free, no account). Only the place's coordinates are sent. Refreshes every 15 minutes;
    // the last forecast is cached so it shows at startup and offline.
    sealed class WeatherWidget : CardWidget
    {
        sealed class Hour { public DateTime Time; public double Temp; public int Code; public bool IsDay; }

        sealed class Forecast
        {
            public double Temp, Hi, Lo;
            public int Code;
            public bool IsDay;
            public DateTime FetchedUtc;
            public List<Hour> Hours = new List<Hour>(); // from the current hour on
        }

        Forecast data;
        string status;
        bool busy;
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };

        public WeatherWidget(Section settings) : base(settings, true)
        {
            refresh.Tick += delegate { Fetch(); };
            LoadCache();
        }

        string Place { get { return Settings.GetText("place", ""); } }
        bool HasPlace { get { return Settings.Get("lat", "").Length > 0; } }
        bool Fahrenheit { get { return Settings.Get("units", "c") == "f"; } }
        protected override string DefaultFace { get { return "sky"; } }
        protected override string[][] AppearanceChoices
        {
            get { return new[] { new[] { "sky", "Sky" }, new[] { "vibrant", "Vibrant" } }; }
        }

        protected override void OnStart()
        {
            refresh.Start();
            Fetch();
        }

        // Redraw at the top of each hour so "Now" and the hourly row move along even offline.
        protected override int NextFrameDelay()
        {
            if (!IsHandleCreated) return -1;
            DateTime now = DateTime.Now;
            return (int)(now.Date.AddHours(now.Hour + 1) - now).TotalMilliseconds + 500;
        }

        // ---------- data ----------
        void LoadCache()
        {
            string file = CacheFile("weather");
            if (file == null || !File.Exists(file)) return;
            try
            {
                string[] parts = File.ReadAllText(file).Split(new[] { '\n' }, 2);
                data = Parse(parts[1], new DateTime(long.Parse(parts[0]), DateTimeKind.Utc));
            }
            catch (Exception) { data = null; }
        }

        void Fetch()
        {
            if (busy || !HasPlace) { Render(); return; }
            busy = true;
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}" +
                "&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day" +
                "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2{2}",
                Settings.Get("lat", ""), Settings.Get("lon", ""), Fahrenheit ? "&temperature_unit=fahrenheit" : "");
            string file = CacheFile("weather");
            Background(() => Http.Get(url), (json, error) =>
            {
                busy = false;
                if (error != null) status = "Offline";
                else
                {
                    try
                    {
                        DateTime now = DateTime.UtcNow;
                        data = Parse(json, now);
                        status = null;
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        File.WriteAllText(file, now.Ticks + "\n" + json);
                    }
                    catch (Exception) { status = "No forecast"; }
                }
                Render();
            });
        }

        static Forecast Parse(string json, DateTime fetchedUtc)
        {
            var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            var cur = (Dictionary<string, object>)root["current"];
            var daily = (Dictionary<string, object>)root["daily"];
            var hourly = (Dictionary<string, object>)root["hourly"];
            var f = new Forecast
            {
                Temp = Convert.ToDouble(cur["temperature_2m"]), Code = Convert.ToInt32(cur["weather_code"]),
                IsDay = Convert.ToInt32(cur["is_day"]) == 1, FetchedUtc = fetchedUtc,
                Hi = Convert.ToDouble(((object[])daily["temperature_2m_max"])[0]),
                Lo = Convert.ToDouble(((object[])daily["temperature_2m_min"])[0])
            };
            // Hourly times are in the place's own time zone, like "current.time".
            DateTime curHour = ParseTime((string)cur["time"]);
            curHour = curHour.Date.AddHours(curHour.Hour);
            var times = (object[])hourly["time"];
            var temps = (object[])hourly["temperature_2m"];
            var codes = (object[])hourly["weather_code"];
            var days = (object[])hourly["is_day"];
            for (int i = 0; i < times.Length; i++)
            {
                DateTime t = ParseTime((string)times[i]);
                if (t < curHour) continue;
                f.Hours.Add(new Hour { Time = t, Temp = Convert.ToDouble(temps[i]), Code = Convert.ToInt32(codes[i]), IsDay = Convert.ToInt32(days[i]) == 1 });
            }
            return f;
        }

        static DateTime ParseTime(string s)
        {
            return DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        }

        void SetPlace()
        {
            var values = InputDialog.Ask("Weather location",
                "Type a city. Its name is looked up once with Open-Meteo; after that only its coordinates are sent to get the forecast.",
                new[] { new InputDialog.Field { Label = "City", Value = Place } }, "Find");
            if (values == null || values[0].Trim().Length == 0) return;
            string query = values[0].Trim();
            status = "Looking up " + query + "…";
            Render();
            Background(() => Http.Get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=" + Uri.EscapeDataString(query)),
                (json, error) =>
                {
                    if (error != null) { status = "Offline"; Render(); return; }
                    var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                    object[] results = root.ContainsKey("results") ? root["results"] as object[] : null;
                    if (results == null || results.Length == 0) { status = "Couldn't find " + query; Render(); return; }
                    var r = (Dictionary<string, object>)results[0];
                    Settings.SetText("place", (string)r["name"]);
                    Settings.Set("lat", Convert.ToDouble(r["latitude"]).ToString(CultureInfo.InvariantCulture));
                    Settings.Set("lon", Convert.ToDouble(r["longitude"]).ToString(CultureInfo.InvariantCulture));
                    data = null;
                    status = null;
                    Fetch();
                });
        }

        // ---------- interaction ----------
        protected override void OnWidgetClick(Point p)
        {
            if (!HasPlace) SetPlace();
        }

        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            menu.Add(HasPlace ? "Change Location…" : "Set Location…", SetPlace);
            menu.Add("Refresh Now", Fetch);
            var units = menu.AddSubmenu("Units");
            units.AddChoice("Celsius (°C)", !Fahrenheit, delegate { Settings.Set("units", "c"); Fetch(); });
            units.AddChoice("Fahrenheit (°F)", Fahrenheit, delegate { Settings.Set("units", "f"); Fetch(); });
        }

        // ---------- drawing ----------
        static readonly Color DayTop = Rgb(0x3D8CE0), DayBottom = Rgb(0x2A6CC4), NightTop = Rgb(0x23305C), NightBottom = Rgb(0x0F1733);

        protected override Color CardBackground(Palette pal) { return DayBottom; }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            Forecast f = data ?? (IsHandleCreated ? null : Sample());
            bool day = f == null || f.IsDay;
            if (!IsVibrant)
                using (var card = CardPath())
                using (var sky = new LinearGradientBrush(new PointF(0, 0), new PointF(0, H), day ? DayTop : NightTop, day ? DayBottom : NightBottom))
                    g.FillPath(sky, card);

            float m = P(16);
            Color white = Color.White, soft = Color.FromArgb(200, 255, 255, 255);
            if (f == null)
            {
                bool placeSet = HasPlace;
                Label(g, placeSet ? (status ?? "Loading weather…") : "Set your city", F(15, "semibold"), white, W / 2, H / 2 - P(22), W - m * 2, StringAlignment.Center);
                Label(g, placeSet ? Place : (status ?? "Click to choose where to show the weather for"), F(11, "regular"), soft, W / 2, H / 2 + P(2), W - m * 2, StringAlignment.Center);
                return;
            }

            string place = IsHandleCreated ? Place : "Paris";
            Label(g, place + (status != null ? "  ·  " + status : ""), F(15, "semibold"), white, m, m - P(2), W * 0.55f, StringAlignment.Near);
            Label(g, Deg(f.Temp), F(40, "light"), white, m - P(2), m + P(14), W * 0.5f, StringAlignment.Near);

            float right = W - m;
            DrawIcon(g, f.Code, f.IsDay, right - P(11), m + P(9), P(22));
            Label(g, Describe(f.Code), F(13, "semibold"), white, right, m + P(24), W * 0.45f, StringAlignment.Far);
            Label(g, "H:" + Deg(f.Hi) + " L:" + Deg(f.Lo), F(13, "regular"), soft, right, m + P(42), W * 0.45f, StringAlignment.Far);

            // Hourly row: Now (current conditions) and the next five hours. Without a fresh
            // fetch, the row shifts along with the clock.
            int start = Math.Max(0, (int)(DateTime.UtcNow - f.FetchedUtc).TotalHours);
            float colW = (W - m * 2) / 6f;
            for (int i = 0; i < 6; i++)
            {
                int idx = start + i;
                if (idx >= f.Hours.Count) break;
                Hour h = f.Hours[idx];
                bool now = i == 0;
                float cx = m + colW * i + colW / 2;
                string label = now ? "Now" : h.Time.ToString("h tt", CultureInfo.InvariantCulture).Replace(" ", "");
                Label(g, label, F(11, "semibold"), soft, cx, P(100), colW, StringAlignment.Center);
                DrawIcon(g, now ? f.Code : h.Code, now ? f.IsDay : h.IsDay, cx, P(126), P(18));
                Label(g, Deg(now ? f.Temp : h.Temp), F(13, "semibold"), white, cx, P(140), colW, StringAlignment.Center);
            }
        }

        static string Deg(double t) { return Math.Round(t).ToString(CultureInfo.InvariantCulture) + "°"; }

        // WMO weather codes, as Open-Meteo reports them.
        static string Describe(int code)
        {
            if (code == 0) return "Clear";
            if (code == 1) return "Mostly Clear";
            if (code == 2) return "Partly Cloudy";
            if (code == 3) return "Cloudy";
            if (code == 45 || code == 48) return "Fog";
            if (code >= 51 && code <= 57) return "Drizzle";
            if (code >= 61 && code <= 67) return "Rain";
            if (code >= 71 && code <= 77) return "Snow";
            if (code >= 80 && code <= 82) return "Showers";
            if (code == 85 || code == 86) return "Snow Showers";
            if (code >= 95) return "Thunderstorms";
            return "—";
        }

        static readonly Color Sun = Rgb(0xFFD60A), RainBlue = Rgb(0x8CCBFF);

        // Icon centred on (cx, cy), roughly s points across.
        void DrawIcon(Graphics g, int code, bool isDay, float cx, float cy, float s)
        {
            Color cloud = isDay ? Color.White : Rgb(0xD5DBE8);
            if (code == 0) { if (isDay) SunGlyph(g, cx, cy, s); else MoonGlyph(g, cx, cy, s * 0.9f); return; }
            if (code == 1 || code == 2)
            {
                if (isDay) SunGlyph(g, cx - s * 0.18f, cy - s * 0.16f, s * 0.7f); else MoonGlyph(g, cx - s * 0.16f, cy - s * 0.16f, s * 0.6f);
                CloudGlyph(g, cloud, cx + s * 0.08f, cy + s * 0.12f, s * 0.78f);
                return;
            }
            bool precip = code >= 51;
            CloudGlyph(g, cloud, cx, precip ? cy - s * 0.14f : cy, s);
            if (code == 45 || code == 48)
                using (var pen = new Pen(cloud, Math.Max(1f, s * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int k = 0; k < 2; k++) g.DrawLine(pen, cx - s * 0.4f + k * s * 0.1f, cy + s * 0.3f + k * s * 0.14f, cx + s * 0.4f - k * s * 0.1f, cy + s * 0.3f + k * s * 0.14f);
            else if ((code >= 71 && code <= 77) || code == 85 || code == 86)
                for (int k = 0; k < 3; k++) FillCircle(g, Color.White, cx - s * 0.25f + k * s * 0.25f, cy + s * 0.3f + (k % 2) * s * 0.1f, s * 0.06f);
            else if (code >= 95)
                using (var b = new SolidBrush(Sun))
                    g.FillPolygon(b, new[] { new PointF(cx + s * 0.05f, cy + s * 0.05f), new PointF(cx - s * 0.12f, cy + s * 0.34f), new PointF(cx, cy + s * 0.34f),
                                             new PointF(cx - s * 0.06f, cy + s * 0.55f), new PointF(cx + s * 0.16f, cy + s * 0.24f), new PointF(cx + s * 0.04f, cy + s * 0.24f) });
            else if (precip)
                using (var pen = new Pen(RainBlue, Math.Max(1f, s * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int k = 0; k < 3; k++)
                    {
                        float x = cx - s * 0.25f + k * s * 0.25f;
                        g.DrawLine(pen, x, cy + s * 0.2f, x - s * 0.07f, cy + s * 0.42f);
                    }
        }

        static void SunGlyph(Graphics g, float cx, float cy, float s)
        {
            FillCircle(g, Sun, cx, cy, s * 0.22f);
            using (var pen = new Pen(Sun, Math.Max(1f, s * 0.08f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4;
                    float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                    g.DrawLine(pen, cx + dx * s * 0.33f, cy + dy * s * 0.33f, cx + dx * s * 0.46f, cy + dy * s * 0.46f);
                }
        }

        static void MoonGlyph(Graphics g, float cx, float cy, float s)
        {
            float r = s * 0.42f;
            using (var region = new Region(new RectangleF(cx - r, cy - r, r * 2, r * 2)))
            using (var cut = new GraphicsPath())
            using (var b = new SolidBrush(Rgb(0xF2F2F7)))
            {
                cut.AddEllipse(cx - r * 0.35f, cy - r * 1.25f, r * 2, r * 2);
                var whole = new GraphicsPath();
                whole.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                region.Intersect(whole);
                region.Exclude(cut);
                g.FillRegion(b, region);
                whole.Dispose();
            }
        }

        static void CloudGlyph(Graphics g, Color c, float cx, float cy, float s)
        {
            using (var b = new SolidBrush(c))
            {
                g.FillEllipse(b, cx - s * 0.48f, cy - s * 0.08f, s * 0.42f, s * 0.36f);
                g.FillEllipse(b, cx - s * 0.24f, cy - s * 0.32f, s * 0.5f, s * 0.5f);
                g.FillEllipse(b, cx + s * 0.08f, cy - s * 0.12f, s * 0.4f, s * 0.4f);
                using (var path = RoundRect(new RectangleF(cx - s * 0.46f, cy + s * 0.02f, s * 0.92f, s * 0.26f), s * 0.13f))
                    g.FillPath(b, path);
            }
        }

        // Library preview.
        static Forecast Sample()
        {
            var f = new Forecast { Temp = 14, Hi = 16, Lo = 9, Code = 2, IsDay = true, FetchedUtc = DateTime.UtcNow };
            int[] codes = { 2, 0, 2, 3, 61, 2 };
            double[] temps = { 14, 15, 16, 15, 13, 12 };
            DateTime h = DateTime.Today.AddHours(DateTime.Now.Hour);
            for (int i = 0; i < 6; i++) f.Hours.Add(new Hour { Time = h.AddHours(i), Temp = temps[i], Code = codes[i], IsDay = true });
            return f;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }
    }
}
