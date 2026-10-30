using System;
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
            using (var w = Create(NewSettings())) return w.Snapshot();
        }
    }

    // Everything the app offers, grouped by category. Add new categories, widgets or presets here.
    static class WidgetRegistry
    {
        public static readonly WidgetCategory[] Categories =
        {
            new WidgetCategory { Id = "clocks", Name = "Clocks", Description = "Analog and world clocks", Glyph = '\uE917' },
            new WidgetCategory { Id = "productivity", Name = "Productivity", Description = "Focus, reminders and notes", Glyph = '\uE73A' },
            new WidgetCategory { Id = "system", Name = "System", Description = "What your PC is doing", Glyph = '\uE7F4' },
            new WidgetCategory { Id = "media", Name = "Photos", Description = "Your pictures on the desktop", Glyph = '\uEB9F' },
            new WidgetCategory { Id = "developer", Name = "Developer", Description = "Your GitHub at a glance", Glyph = '\uE943' },
        };

        public static readonly WidgetType[] Types =
        {
            new WidgetType
            {
                Id = "analog-clock", Category = "clocks", Name = "Analog clock", Description = "Local time, classic dial",
                Create = s => new AnalogClock(s)
            },
            new WidgetType
            {
                Id = "analog-clock-dark", Category = "clocks", Name = "Analog clock · Dark", Description = "Local time, dark dial",
                Create = s => new AnalogClock(s), Defaults = s => s.Set("face", "dark")
            },
            new WidgetType
            {
                Id = "world-clock", Category = "clocks", Name = "World clock", Description = "Any city, starts on Paris",
                Create = s => new AnalogClock(s),
                Defaults = s => { s.Set("label", "PAR"); s.Set("tz", "Romance Standard Time"); }
            },
            new WidgetType
            {
                Id = "focus-timer", Category = "productivity", Name = "Focus timer", Description = "A ring that counts down",
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
                Id = "photo-frame", Category = "media", Name = "Photo frame", Description = "A picture from your folder, changes hourly",
                Create = s => new PhotoFrame(s)
            },
            new WidgetType
            {
                Id = "github", Category = "developer", Name = "GitHub", Description = "Profile, streak and contribution graph",
                Create = s => new GitHubWidget(s)
            },
        };

        public static WidgetType Find(string id)
        {
            return Array.Find(Types, t => t.Id == id);
        }

        public static WidgetType[] InCategory(string categoryId)
        {
            return Types.Where(t => t.Category == categoryId).ToArray();
        }
    }
}
