using System;
using System.Collections.Generic;

namespace DeskWidgets
{
    // What community widgets compile against ("API v3"): this file plus WidgetWindow, CardWidget,
    // Section, PopupMenu, InputDialog, Http, CalEvent, Ics, MediaSession, F1, F1LiveFeed.
    // Treat public signatures as frozen: changing one breaks every installed widget.
    public static class PluginApi
    {
        // Bump this whenever anything is ADDED to that surface, not only when something changes.
        // A widget declares the version it needs, and Community.Confirm refuses to install one that
        // needs more than this app has, with a message telling the person to update. Adding
        // MediaSession.Seek without bumping is why installing Now Playing v2 on an older app showed
        // a raw compiler error instead: the widget asked for 2, the older app also said 2, and the
        // check saw nothing wrong.
        public const int Version = 3;

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
