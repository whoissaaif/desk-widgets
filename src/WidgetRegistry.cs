using System;
using System.Drawing;
using DeskWidgets.Widgets;

namespace DeskWidgets
{
    sealed class WidgetType
    {
        public string Id, Name, Description;
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

    // Every widget the library offers. Add new widgets (or presets of existing ones) here.
    static class WidgetRegistry
    {
        public static readonly WidgetType[] Types =
        {
            new WidgetType
            {
                Id = "analog-clock", Name = "Analog clock", Description = "Local time, classic dial",
                Create = s => new AnalogClock(s)
            },
            new WidgetType
            {
                Id = "analog-clock-dark", Name = "Analog clock · Dark", Description = "Local time, dark dial",
                Create = s => new AnalogClock(s), Defaults = s => s.Set("face", "dark")
            },
            new WidgetType
            {
                Id = "world-clock", Name = "World clock", Description = "Any city, starts on Paris",
                Create = s => new AnalogClock(s),
                Defaults = s => { s.Set("label", "PAR"); s.Set("tz", "Romance Standard Time"); }
            },
        };

        public static WidgetType Find(string id)
        {
            return Array.Find(Types, t => t.Id == id);
        }
    }
}
