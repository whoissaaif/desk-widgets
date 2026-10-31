using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeskWidgets
{
    // Tiny INI store: one [section] per widget instance.
    public sealed class Section
    {
        public readonly string Name;
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        internal Config Owner;

        public Section(string name) { Name = name; }

        public string Type { get { return Get("type", ""); } }

        public string Get(string key, string def)
        {
            string v;
            return Values.TryGetValue(key, out v) ? v : def;
        }

        public int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public float GetFloat(string key, float def)
        {
            float v;
            return float.TryParse(Get(key, null), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            return v == null ? def : v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public void Set(string key, object value)
        {
            if (value is bool) value = (bool)value ? "1" : "0";
            Values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (Owner != null) Owner.Save();
        }

        // Multi-line text: newlines and backslashes are escaped so each value stays on one INI line.
        public string GetText(string key, string def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char n = v[++i];
                    sb.Append(n == 'n' ? '\n' : n);
                }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }

        public void SetText(string key, string value)
        {
            Set(key, value.Replace("\r\n", "\n").Replace("\\", "\\\\").Replace("\n", "\\n"));
        }

        public void Remove(string key)
        {
            if (Values.Remove(key) && Owner != null) Owner.Save();
        }
    }

    sealed class Config
    {
        readonly string path;
        public readonly List<Section> Sections = new List<Section>();

        Config(string path) { this.path = path; }

        public static Config Load(string path)
        {
            var cfg = new Config(path);
            if (!File.Exists(path)) return cfg;
            Section cur = null;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    cur = new Section(line.Substring(1, line.Length - 2)) { Owner = cfg };
                    cfg.Sections.Add(cur);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur != null && eq > 0)
                    cur.Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return cfg;
        }

        public Section Add(string type)
        {
            int n = 1;
            while (Sections.Exists(s => s.Name == "widget" + n)) n++;
            var section = new Section("widget" + n);
            section.Values["type"] = type;
            Sections.Add(section);
            section.Owner = this;
            Save();
            return section;
        }

        public void Remove(Section section)
        {
            Sections.Remove(section);
            section.Owner = null;
            Save();
        }

        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var s in Sections)
            {
                sb.Append('[').Append(s.Name).Append("]\r\n");
                foreach (var kv in s.Values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                sb.Append("\r\n");
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (IOException) { } // e.g. OneDrive briefly locking the file; next change re-saves
            catch (UnauthorizedAccessException) { }
        }
    }
}
