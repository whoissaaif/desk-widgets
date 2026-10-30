using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DeskWidgets
{
    // A real Win32 popup menu, so right-click menus look exactly like the rest of Windows
    // (rounded corners on Windows 11, system light/dark theme). Built on demand, destroyed after use.
    sealed class PopupMenu
    {
        // Hidden window that owns every menu (set once by App).
        public static IntPtr Owner;

        sealed class Item
        {
            public string Text;
            public Action OnClick;
            public bool Checked, Radio, Bold, Separator;
            public PopupMenu Submenu;
        }

        readonly List<Item> items = new List<Item>();

        public int Count { get { return items.Count; } }

        public void Add(string text, Action onClick)
        {
            items.Add(new Item { Text = text, OnClick = onClick });
        }

        public void AddBold(string text, Action onClick)
        {
            items.Add(new Item { Text = text, OnClick = onClick, Bold = true });
        }

        public void AddCheck(string text, bool isChecked, Action onClick)
        {
            items.Add(new Item { Text = text, OnClick = onClick, Checked = isChecked });
        }

        // One of several mutually exclusive choices (shown with a bullet).
        public void AddChoice(string text, bool isSelected, Action onClick)
        {
            items.Add(new Item { Text = text, OnClick = onClick, Checked = isSelected, Radio = true });
        }

        public void AddSeparator()
        {
            if (items.Count > 0 && !items[items.Count - 1].Separator) items.Add(new Item { Separator = true });
        }

        public PopupMenu AddSubmenu(string text)
        {
            var sub = new PopupMenu();
            items.Add(new Item { Text = text, Submenu = sub });
            return sub;
        }

        public void Show(Point screen)
        {
            var actions = new List<Action>();
            IntPtr menu = Build(actions);
            try
            {
                // Required so the menu closes when clicking elsewhere (documented TrackPopupMenu quirk).
                Native.SetForegroundWindow(Owner);
                int cmd = Native.TrackPopupMenuEx(menu, Native.TPM_RIGHTBUTTON | Native.TPM_RETURNCMD | Native.TPM_NONOTIFY,
                    screen.X, screen.Y, Owner, IntPtr.Zero);
                Native.PostMessage(Owner, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
                if (cmd > 0) actions[cmd - 1]();
            }
            finally
            {
                Native.DestroyMenu(menu); // also destroys submenus
            }
        }

        IntPtr Build(List<Action> actions)
        {
            IntPtr menu = Native.CreatePopupMenu();
            uint pos = 0;
            foreach (var item in items)
            {
                var mii = new Native.MENUITEMINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.MENUITEMINFO)) };
                if (item.Separator)
                {
                    mii.fMask = Native.MIIM_FTYPE;
                    mii.fType = Native.MFT_SEPARATOR;
                }
                else
                {
                    mii.fMask = Native.MIIM_FTYPE | Native.MIIM_STATE | Native.MIIM_STRING | Native.MIIM_ID;
                    mii.fType = item.Radio ? Native.MFT_RADIOCHECK : 0;
                    mii.fState = (item.Checked ? Native.MFS_CHECKED : 0) | (item.Bold ? Native.MFS_DEFAULT : 0);
                    mii.dwTypeData = item.Text;
                    if (item.OnClick != null)
                    {
                        actions.Add(item.OnClick);
                        mii.wID = (uint)actions.Count;
                    }
                    if (item.Submenu != null)
                    {
                        mii.fMask |= Native.MIIM_SUBMENU;
                        mii.hSubMenu = item.Submenu.Build(actions);
                    }
                }
                Native.InsertMenuItem(menu, pos++, true, ref mii);
            }
            return menu;
        }
    }
}
