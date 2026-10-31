using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DeskWidgets
{
    // Just enough iCalendar (.ics) to list upcoming events: VEVENTs with all-day or timed starts,
    // UTC or TZID times, DTEND/DURATION, cancelled events skipped, and the common RRULEs
    // (DAILY / WEEKLY with BYDAY / MONTHLY / YEARLY with INTERVAL, COUNT, UNTIL) plus EXDATE.
    public sealed class CalEvent
    {
        public string Title;
        public DateTime Start, End; // local time
        public bool AllDay;
        public int Calendar;        // which subscribed calendar it came from (for its colour)
    }

    public static class Ics
    {
        sealed class Prop { public string Params, Value; }

        public static List<CalEvent> Parse(string text, DateTime from, DateTime to, int calendar)
        {
            var events = new List<CalEvent>();
            string unfolded = text.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
            Dictionary<string, Prop> ev = null;
            List<string> exdates = null;
            foreach (string raw in unfolded.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line == "BEGIN:VEVENT") { ev = new Dictionary<string, Prop>(); exdates = new List<string>(); continue; }
                if (line == "END:VEVENT")
                {
                    if (ev != null) Expand(ev, exdates, from, to, calendar, events);
                    ev = null;
                    continue;
                }
                if (ev == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string head = line.Substring(0, colon), value = line.Substring(colon + 1);
                int semi = head.IndexOf(';');
                string name = (semi < 0 ? head : head.Substring(0, semi)).ToUpperInvariant();
                string prms = semi < 0 ? "" : head.Substring(semi + 1);
                if (name == "EXDATE") exdates.AddRange(value.Split(',').Select(v => prms + "|" + v));
                else if (!ev.ContainsKey(name)) ev[name] = new Prop { Params = prms, Value = value };
            }
            return events;
        }

        static void Expand(Dictionary<string, Prop> ev, List<string> exdates, DateTime from, DateTime to, int calendar, List<CalEvent> output)
        {
            Prop start, p;
            if (!ev.TryGetValue("DTSTART", out start)) return;
            if (ev.TryGetValue("STATUS", out p) && p.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
            bool allDay;
            DateTime s = ParseTime(start.Params, start.Value, out allDay);
            TimeSpan length;
            bool ignored;
            if (ev.TryGetValue("DTEND", out p)) length = ParseTime(p.Params, p.Value, out ignored) - s;
            else if (ev.TryGetValue("DURATION", out p)) length = ParseDuration(p.Value);
            else length = allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            string title = ev.TryGetValue("SUMMARY", out p) ? Unescape(p.Value) : "(No title)";
            var skip = new HashSet<DateTime>(exdates.Select(x =>
            {
                int bar = x.IndexOf('|');
                bool a;
                return ParseTime(x.Substring(0, bar), x.Substring(bar + 1), out a);
            }));

            Action<DateTime> add = occ =>
            {
                if (skip.Contains(occ)) return;
                DateTime e = occ + length;
                if (e > from && occ < to) output.Add(new CalEvent { Title = title, Start = occ, End = e, AllDay = allDay, Calendar = calendar });
            };

            if (!ev.TryGetValue("RRULE", out p)) { add(s); return; }

            var rule = p.Value.Split(';').Select(kv => kv.Split('=')).Where(kv => kv.Length == 2)
                              .ToDictionary(kv => kv[0].ToUpperInvariant(), kv => kv[1].ToUpperInvariant());
            string freq;
            if (!rule.TryGetValue("FREQ", out freq)) { add(s); return; }
            int interval = rule.ContainsKey("INTERVAL") ? Math.Max(1, int.Parse(rule["INTERVAL"], CultureInfo.InvariantCulture)) : 1;
            int count = rule.ContainsKey("COUNT") ? int.Parse(rule["COUNT"], CultureInfo.InvariantCulture) : int.MaxValue;
            DateTime until = DateTime.MaxValue;
            if (rule.ContainsKey("UNTIL")) { bool a; until = ParseTime("", rule["UNTIL"], out a); if (a) until = until.AddDays(1).AddTicks(-1); }
            var byDay = rule.ContainsKey("BYDAY")
                ? new HashSet<DayOfWeek>(rule["BYDAY"].Split(',').Select(d => DayOf(d.Substring(Math.Max(0, d.Length - 2)))).Where(d => d.HasValue).Select(d => d.Value))
                : null;

            // Walk day by day. With COUNT we must count from the first occurrence; otherwise we can
            // start just before the window, because every test below is relative to the start date.
            DateTime day = s.Date;
            if (count == int.MaxValue)
            {
                DateTime jump = from.Date.AddDays(-1 - Math.Ceiling(length.TotalDays));
                if (jump > day) day = jump;
            }
            int made = 0;
            for (int guard = 0; guard < 20000 && day < to && made < count; guard++, day = day.AddDays(1))
            {
                DateTime occ = day + s.TimeOfDay;
                if (occ < s) continue;
                if (occ > until) break;
                bool hit;
                switch (freq)
                {
                    case "DAILY": hit = ((day - s.Date).Days % interval) == 0; break;
                    case "WEEKLY":
                        int weeks = (int)Math.Floor((day - StartOfWeek(s.Date)).Days / 7.0);
                        hit = weeks % interval == 0 && (byDay != null ? byDay.Contains(day.DayOfWeek) : day.DayOfWeek == s.DayOfWeek);
                        break;
                    case "MONTHLY":
                        int months = (day.Year - s.Year) * 12 + day.Month - s.Month;
                        hit = day.Day == s.Day && months % interval == 0;
                        break;
                    case "YEARLY": hit = day.Month == s.Month && day.Day == s.Day && (day.Year - s.Year) % interval == 0; break;
                    default: hit = day == s.Date; break;
                }
                if (!hit) continue;
                made++;
                add(occ);
            }
        }

        static DateTime StartOfWeek(DateTime d) { return d.AddDays(-(int)d.DayOfWeek); }

        static DayOfWeek? DayOf(string two)
        {
            switch (two)
            {
                case "SU": return DayOfWeek.Sunday; case "MO": return DayOfWeek.Monday; case "TU": return DayOfWeek.Tuesday;
                case "WE": return DayOfWeek.Wednesday; case "TH": return DayOfWeek.Thursday; case "FR": return DayOfWeek.Friday;
                case "SA": return DayOfWeek.Saturday; default: return null;
            }
        }

        // Returns local time. All-day dates come back as midnight with allDay = true.
        static DateTime ParseTime(string prms, string value, out bool allDay)
        {
            value = value.Trim();
            allDay = value.Length == 8; // VALUE=DATE: yyyyMMdd with no time part
            if (allDay) return DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture);
            bool utc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
            DateTime t = DateTime.ParseExact(value.TrimEnd('Z', 'z').Substring(0, 15), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            if (utc) return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
            string tzid = Param(prms, "TZID");
            TimeZoneInfo zone = tzid == null ? null : FindZone(tzid);
            if (zone != null)
            {
                try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local); }
                catch (ArgumentException) { } // invalid local time (DST gap): keep as is
            }
            return t; // floating time, or a zone we don't know: treat as local
        }

        static string Param(string prms, string key)
        {
            foreach (string kv in prms.Split(';'))
                if (kv.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return kv.Substring(key.Length + 1).Trim('"');
            return null;
        }

        // Calendars use IANA zone names; .NET Framework only knows Windows ones. Map the common ones.
        static readonly Dictionary<string, string> Iana = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "UTC", "UTC" }, { "Etc/UTC", "UTC" }, { "GMT", "UTC" },
            { "Europe/London", "GMT Standard Time" }, { "Europe/Dublin", "GMT Standard Time" }, { "Europe/Lisbon", "GMT Standard Time" },
            { "Europe/Paris", "Romance Standard Time" }, { "Europe/Brussels", "Romance Standard Time" }, { "Europe/Madrid", "Romance Standard Time" },
            { "Europe/Berlin", "W. Europe Standard Time" }, { "Europe/Amsterdam", "W. Europe Standard Time" }, { "Europe/Rome", "W. Europe Standard Time" },
            { "Europe/Vienna", "W. Europe Standard Time" }, { "Europe/Zurich", "W. Europe Standard Time" }, { "Europe/Stockholm", "W. Europe Standard Time" },
            { "Europe/Athens", "GTB Standard Time" }, { "Europe/Istanbul", "Turkey Standard Time" }, { "Europe/Moscow", "Russian Standard Time" },
            { "Africa/Cairo", "Egypt Standard Time" }, { "Africa/Johannesburg", "South Africa Standard Time" }, { "Africa/Lagos", "W. Central Africa Standard Time" },
            { "Asia/Dubai", "Arabian Standard Time" }, { "Asia/Riyadh", "Arab Standard Time" }, { "Asia/Karachi", "Pakistan Standard Time" },
            { "Asia/Kolkata", "India Standard Time" }, { "Asia/Calcutta", "India Standard Time" }, { "Asia/Dhaka", "Bangladesh Standard Time" },
            { "Asia/Bangkok", "SE Asia Standard Time" }, { "Asia/Jakarta", "SE Asia Standard Time" }, { "Asia/Singapore", "Singapore Standard Time" },
            { "Asia/Kuala_Lumpur", "Singapore Standard Time" }, { "Asia/Shanghai", "China Standard Time" }, { "Asia/Hong_Kong", "China Standard Time" },
            { "Asia/Taipei", "Taipei Standard Time" }, { "Asia/Seoul", "Korea Standard Time" }, { "Asia/Tokyo", "Tokyo Standard Time" },
            { "Australia/Sydney", "AUS Eastern Standard Time" }, { "Australia/Melbourne", "AUS Eastern Standard Time" }, { "Australia/Brisbane", "E. Australia Standard Time" },
            { "Australia/Perth", "W. Australia Standard Time" }, { "Pacific/Auckland", "New Zealand Standard Time" },
            { "America/New_York", "Eastern Standard Time" }, { "America/Toronto", "Eastern Standard Time" }, { "America/Chicago", "Central Standard Time" },
            { "America/Denver", "Mountain Standard Time" }, { "America/Phoenix", "US Mountain Standard Time" }, { "America/Los_Angeles", "Pacific Standard Time" },
            { "America/Vancouver", "Pacific Standard Time" }, { "America/Anchorage", "Alaskan Standard Time" }, { "Pacific/Honolulu", "Hawaiian Standard Time" },
            { "America/Mexico_City", "Central Standard Time (Mexico)" }, { "America/Sao_Paulo", "E. South America Standard Time" },
            { "America/Argentina/Buenos_Aires", "Argentina Standard Time" }, { "America/Bogota", "SA Pacific Standard Time" },
        };

        static TimeZoneInfo FindZone(string id)
        {
            string windowsId;
            if (!Iana.TryGetValue(id, out windowsId)) windowsId = id; // Outlook already uses Windows names
            try { return TimeZoneInfo.FindSystemTimeZoneById(windowsId); }
            catch (Exception) { return null; }
        }

        static TimeSpan ParseDuration(string v)
        {
            // e.g. PT1H30M, P1D, PT45M
            var t = TimeSpan.Zero;
            int num = 0;
            bool time = false;
            foreach (char c in v.ToUpperInvariant())
            {
                if (char.IsDigit(c)) { num = num * 10 + (c - '0'); continue; }
                if (c == 'T') time = true;
                else if (c == 'W') t += TimeSpan.FromDays(7 * num);
                else if (c == 'D') t += TimeSpan.FromDays(num);
                else if (c == 'H') t += TimeSpan.FromHours(num);
                else if (c == 'M' && time) t += TimeSpan.FromMinutes(num);
                else if (c == 'S') t += TimeSpan.FromSeconds(num);
                num = 0;
            }
            return t;
        }

        static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' || n == 'N' ? ' ' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString().Trim();
        }
    }
}
