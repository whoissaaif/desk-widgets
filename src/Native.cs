using System;
using System.Runtime.InteropServices;

namespace DeskWidgets
{
    static class Native
    {
        public const int GWL_HWNDPARENT = -8;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_NOACTIVATE = 0x8000000;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOPMOST = 0x8;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int WM_WINDOWPOSCHANGING = 0x46;
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int WM_EXITSIZEMOVE = 0x232;
        public const int HTCAPTION = 2;
        public const uint SWP_NOZORDER = 0x4;
        public const int ULW_ALPHA = 2;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd, hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ---- native popup menus ----
        public const uint MIIM_STATE = 0x1, MIIM_ID = 0x2, MIIM_SUBMENU = 0x4, MIIM_STRING = 0x40, MIIM_FTYPE = 0x100;
        public const uint MFT_SEPARATOR = 0x800, MFT_RADIOCHECK = 0x200;
        public const uint MFS_CHECKED = 0x8, MFS_DEFAULT = 0x1000;
        public const uint TPM_RIGHTBUTTON = 0x2, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;
        public const int WM_NULL = 0, WM_SETTINGCHANGE = 0x1A;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MENUITEMINFO
        {
            public uint cbSize, fMask, fType, fState, wID;
            public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData;
            public string dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
        }

        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool InsertMenuItem(IntPtr hMenu, uint item, bool byPosition, ref MENUITEMINFO mii);
        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

        // Undocumented-but-stable uxtheme exports (Windows 10 1903+) that make Win32 menus follow
        // the system light/dark app theme, exactly like Explorer's menus. Silently skipped elsewhere.
        [DllImport("uxtheme.dll", EntryPoint = "#133")] static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
        [DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
        [DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

        public static void FollowSystemThemeForMenus(IntPtr ownerHwnd)
        {
            try
            {
                SetPreferredAppMode(1); // AllowDark
                AllowDarkModeForWindow(ownerHwnd, true);
                FlushMenuThemes();
            }
            catch (EntryPointNotFoundException) { }
        }

        public static void RefreshMenuTheme()
        {
            try { FlushMenuThemes(); }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int idx, int v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);

        public static void SetOwner(IntPtr hwnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_HWNDPARENT, owner);
            else SetWindowLong32(hwnd, GWL_HWNDPARENT, owner.ToInt32());
        }
    }
}
