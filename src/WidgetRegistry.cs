using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using DeskWidgets.Widgets;

namespace DeskWidgets
{
    sealed class WidgetCategory
    {
        public string Id, Name, Description;
        public char Glyph; // Segoe Fluent Icons / MDL2 Assets code point
    }

    sealed class WidgetType
    {
        public string Id, Category, Name, Description;
        public Func<Section, WidgetWindow> Create;
        // Set for community widgets: their code may crash, so the app guards every call into it.
        public string PluginId;
        // Optional: settings for a freshly added widget (lets one widget class offer several presets).
        public Action<Section> Defaults;

        public Section NewSettings()
        {
            var s = new Section("new");
            s.Values["type"] = Id;
            if (Defaults != null) Defaults(s);
            return s;
        }

        public Bitmap Snapshot()
        {
            return Plugins.Guard(this, () =>
            {
                using (var w = Create(NewSettings())) return w.Snapshot();
            });
        }
    }

    // Everything the app offers, grouped by category. Add new categories, widgets or presets here.
    // Community widgets are added and removed at run time through Register / Unregister.
    static class WidgetRegistry
    {
        public static readonly WidgetCategory[] Categories =
        {
            new WidgetCategory { Id = "clocks", Name = "Clocks", Description = "Analog and world clocks", Glyph = '\uE917' },
            new WidgetCategory { Id = "productivity", Name = "Productivity", Description = "Calendar, focus, reminders and notes", Glyph = '\uE73A' },
            new WidgetCategory { Id = "weather", Name = "Weather", Description = "Conditions and the next hours", Glyph = '\uE753' },
            new WidgetCategory { Id = "system", Name = "System", Description = "What your PC is doing", Glyph = '\uE7F4' },
            new WidgetCategory { Id = "media", Name = "Media", Description = "Your pictures and what's playing", Glyph = '\uEB9F' },
            new WidgetCategory { Id = "developer", Name = "Developer", Description = "Your GitHub at a glance", Glyph = '\uE943' },
            new WidgetCategory { Id = "sports", Name = "Sports", Description = "Formula 1 schedule, standings and timing", Glyph = '\uE7C1' },
        };

        static readonly WidgetType[] BuiltIn =
        {
            new WidgetType
            {
                Id = "analog-clock", Category = "clocks", Name = "Analog Clock", Description = "Local time, classic dial",
                Create = s => new AnalogClock(s)
            },
            new WidgetType
            {
                Id = "analog-clock-dark", Category = "clocks", Name = "Analog Clock · Dark", Description = "Local time, dark dial",
                Create = s => new AnalogClock(s), Defaults = s => s.Set("face", "dark")
            },
            new WidgetType
            {
                Id = "world-clock", Category = "clocks", Name = "World Clock", Description = "Any city, starts on Paris",
                Create = s => new AnalogClock(s),
                Defaults = s => { s.Set("label", "PAR"); s.Set("tz", "Romance Standard Time"); }
            },
        };

        static readonly List<WidgetType> types = new List<WidgetType>(BuiltIn);

        // Old built-in type ids that moved to the community gallery. When a community widget
        // registers with the new id, any orphaned config sections still using the old id are
        // migrated so the user keeps their settings.
        static readonly Dictionary<string, string> Migrations = new Dictionary<string, string>
        {
            { "calendar", "u.whoissaaif.calendar" },
            { "weather", "u.whoissaaif.weather" },
            { "focus-timer", "u.whoissaaif.focus-timer" },
            { "reminders", "u.whoissaaif.reminders" },
            { "notes", "u.whoissaaif.notes" },
            { "system", "u.whoissaaif.system" },
            { "photo-frame", "u.whoissaaif.photo-frame" },
            { "now-playing", "u.whoissaaif.now-playing" },
            { "github", "u.whoissaaif.github" },
            { "f1-hub", "u.whoissaaif.f1-hub" },
            { "f1-timing", "u.whoissaaif.f1-timing" },
        };

        // Raised when community widgets are installed, removed, enabled or disabled.
        public static event Action Changed;

        public static void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        public static IEnumerable<WidgetType> Types { get { return types; } }

        public static WidgetType Find(string id)
        {
            return types.Find(t => t.Id == id);
        }

        public static WidgetType[] InCategory(string categoryId)
        {
            return types.Where(t => t.Category == categoryId).ToArray();
        }

        // The old built-in id that should migrate to this community id, or null.
        public static string MigrationSource(string communityId)
        {
            foreach (var kv in Migrations)
                if (kv.Value == communityId) return kv.Key;
            return null;
        }

        // True when a section's type was a built-in that moved to the gallery.
        public static bool IsOrphaned(string typeId)
        {
            return Migrations.ContainsKey(typeId) && Find(typeId) == null;
        }

        // Never replaces an existing entry, built-in or not.
        public static bool Register(WidgetType type)
        {
            if (Find(type.Id) != null) return false;
            types.Add(type);
            return true;
        }

        public static void Unregister(string id)
        {
            types.RemoveAll(t => t.Id == id && t.PluginId != null);
        }
    }
}
