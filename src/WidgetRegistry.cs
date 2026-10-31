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
            new WidgetType
            {
                Id = "calendar", Category = "productivity", Name = "Calendar", Description = "Today, your next events and the month",
                Create = s => new CalendarWidget(s)
            },
            new WidgetType
            {
                Id = "weather", Category = "weather", Name = "Weather", Description = "Now, high and low, and the next hours",
                Create = s => new WeatherWidget(s)
            },
            new WidgetType
            {
                Id = "focus-timer", Category = "productivity", Name = "Focus Timer", Description = "A ring that counts down",
                Create = s => new FocusTimer(s)
            },
            new WidgetType
            {
                Id = "reminders", Category = "productivity", Name = "Reminders", Description = "Tick things off from the desktop",
                Create = s => new RemindersWidget(s)
            },
            new WidgetType
            {
                Id = "notes", Category = "productivity", Name = "Notes", Description = "One note, click to edit",
                Create = s => new NotesWidget(s)
            },
            new WidgetType
            {
                Id = "system", Category = "system", Name = "System", Description = "CPU, memory and disk",
                Create = s => new SystemWidget(s)
            },
            new WidgetType
            {
                Id = "photo-frame", Category = "media", Name = "Photo Frame", Description = "A picture from your folder, changes hourly",
                Create = s => new PhotoFrame(s)
            },
            new WidgetType
            {
                Id = "now-playing", Category = "media", Name = "Now Playing", Description = "What's playing, with play, pause and skip",
                Create = s => new NowPlayingWidget(s)
            },
            new WidgetType
            {
                Id = "github", Category = "developer", Name = "GitHub", Description = "Profile, streak and contribution graph",
                Create = s => new GitHubWidget(s)
            },
            new WidgetType
            {
                Id = "f1-hub", Category = "sports", Name = "F1 Race Hub", Description = "Next race, its sessions, a countdown and the standings",
                Create = s => new F1HubWidget(s)
            },
            new WidgetType
            {
                Id = "f1-timing", Category = "sports", Name = "F1 Timing", Description = "Top five of the latest session, live with OpenF1",
                Create = s => new F1TimingWidget(s)
            },
        };

        static readonly List<WidgetType> types = new List<WidgetType>(BuiltIn);

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
