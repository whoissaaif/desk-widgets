using System;
using System.Collections.Generic;

namespace DeskWidgets
{
    // What community widgets compile against ("API v2"): this file plus WidgetWindow, CardWidget,
    // Section, PopupMenu, InputDialog, Http, CalEvent, Ics, MediaSession, F1, F1LiveFeed.
    // Treat public signatures as frozen: changing one breaks every installed widget.
    public static class PluginApi
    {
        public const int Version = 2;

        public static string DataDir { get { return App.DataDir; } }

        public static string Protect(string plain) { return Secrets.Protect(plain); }
        public static string Unprotect(string stored) { return Secrets.Unprotect(stored); }

        public static event Action CalendarChanged
        {
            add { if (App.Current != null) App.Current.CalendarEventsChanged += value; }
            remove { if (App.Current != null) App.Current.CalendarEventsChanged -= value; }
        }

        public static List<CalEvent> CalendarEvents
        {
            get { return App.Current != null ? App.Current.CalendarEvents : new List<CalEvent>(); }
        }

        public static bool HasCalendar
        {
            get { return App.Current != null && App.Current.HasCalendar; }
        }

        public static void PublishCalendar(WidgetWindow source, List<CalEvent> events)
        {
            if (App.Current != null) App.Current.PublishCalendar(source, events);
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DeskWidgetAttribute : Attribute
    {
        public string Name, Description;
    }
}
