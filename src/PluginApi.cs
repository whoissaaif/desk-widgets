using System;

namespace DeskWidgets
{
    // What community widgets compile against ("API v1"): this file plus WidgetWindow, Section,
    // PopupMenu and Http. Everything else in the app stays internal. Treat it as frozen: changing a
    // public signature breaks every installed widget, so a breaking change means Version 2.
    public static class PluginApi
    {
        public const int Version = 1;
    }

    // Marks the one widget class in a community widget's source. Id, category and API version come
    // from the approved row (or the local install), never from the code.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DeskWidgetAttribute : Attribute
    {
        public string Name, Description;
    }
}
