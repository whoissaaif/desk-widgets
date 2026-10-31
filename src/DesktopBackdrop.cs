using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskWidgets
{
    // The "vibrant" look: widgets show the wallpaper behind them, blurred. Layered windows can't use
    // DWM's live blur, so we keep one tiny copy of the whole desktop wallpaper (1/12 scale, a few KB)
    // and stretch the piece behind each widget back up, which blurs it for free. Rebuilt only when
    // the wallpaper or the displays change.
    static class DesktopBackdrop
    {
        const int Shrink = 12;
        static Bitmap map;
        static Rectangle virt;
        static bool built;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SystemParametersInfo(int action, int param, StringBuilder value, int winIni);
        [DllImport("user32.dll")] static extern int GetSysColor(int index);
        const int SPI_GETDESKWALLPAPER = 0x73, COLOR_DESKTOP = 1;

        public static void Invalidate()
        {
            if (map != null) { map.Dispose(); map = null; }
            built = false;
        }

        // A blurred copy of the wallpaper behind screenRect, at the given size. Caller disposes it.
        // An empty rect (library previews) gets a neutral stand-in.
        public static Bitmap Blurred(Size size, Rectangle screenRect)
        {
            var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                if (!screenRect.IsEmpty && !built) Build();
                if (screenRect.IsEmpty || map == null)
                {
                    using (var b = new LinearGradientBrush(new Point(0, 0), new Point(bmp.Width, bmp.Height), Color.FromArgb(70, 108, 120), Color.FromArgb(150, 120, 90)))
                        g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
                    return bmp;
                }
                var src = new RectangleF((screenRect.X - virt.X) / (float)Shrink, (screenRect.Y - virt.Y) / (float)Shrink,
                                         screenRect.Width / (float)Shrink, screenRect.Height / (float)Shrink);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the edges
                    g.DrawImage(map, new Rectangle(0, 0, bmp.Width, bmp.Height), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
        }

        // Build on a ThreadPool thread. done(newMap, newVirt) fires on the pool thread;
        // caller marshals to UI thread before calling Apply.
        public static void RebuildAsync(Action<Bitmap, Rectangle> done)
        {
            var virtRect = SystemInformation.VirtualScreen;
            var screens = Screen.AllScreens;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap newMap = null;
                try { newMap = BuildCore(virtRect, screens); }
                catch { }
                done(newMap, virtRect);
            });
        }

        // Swap in a freshly built backdrop. Call on UI thread only.
        public static void Apply(Bitmap newMap, Rectangle newVirt)
        {
            if (map != null) map.Dispose();
            map = newMap;
            virt = newVirt;
            built = true;
        }

        // Synchronous first-time build (called lazily from Blurred on the first render).
        static void Build()
        {
            built = true;
            virt = SystemInformation.VirtualScreen;
            try
            {
                var newMap = BuildCore(virt, Screen.AllScreens);
                if (map != null) map.Dispose();
                map = newMap;
            }
            catch { }
        }

        static Bitmap BuildCore(Rectangle virtRect, Screen[] screens)
        {
            var bmp = new Bitmap(Math.Max(1, virtRect.Width / Shrink), Math.Max(1, virtRect.Height / Shrink), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(ColorTranslator.FromWin32(GetSysColor(COLOR_DESKTOP)));

                string path = FindWallpaper();
                if (path == null) return bmp;

                int style = 10, tile = 0;
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    if (k != null)
                    {
                        int.TryParse(k.GetValue("WallpaperStyle", "10") as string, out style);
                        int.TryParse(k.GetValue("TileWallpaper", "0") as string, out tile);
                    }

                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                byte[] bytes = ReadWithRetry(path);
                if (bytes == null) return bmp;

                try
                {
                    using (var ms = new MemoryStream(bytes))
                    using (var img = Image.FromStream(ms, false, false))
                    {
                        if (style == 22) DrawStyled(g, img, virtRect, virtRect, 10);
                        else foreach (var s in screens) DrawStyled(g, img, s.Bounds, virtRect, tile == 1 ? 10 : style);
                    }
                }
                catch { }
            }
            return bmp;
        }

        // TranscodedWallpaper is written before the registry updates, so prefer it.
        static string FindWallpaper()
        {
            var sb = new StringBuilder(520);
            bool hasSpi = SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            string spiPath = hasSpi ? sb.ToString() : "";

            string transcoded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(transcoded)) return transcoded;

            if (spiPath.Length > 0 && File.Exists(spiPath)) return spiPath;
            return null;
        }

        static byte[] ReadWithRetry(string path)
        {
            for (int i = 0; i < 3; i++)
            {
                try { return File.ReadAllBytes(path); }
                catch (IOException) { if (i < 2) Thread.Sleep(30); }
            }
            return null;
        }

        // WallpaperStyle: 10 fill, 6 fit, 2 stretch, 0 centre (tile is treated as fill).
        static void DrawStyled(Graphics g, Image img, Rectangle screen, Rectangle virtRect, int style)
        {
            float sx = screen.Width / (float)img.Width, sy = screen.Height / (float)img.Height;
            float scale = style == 6 ? Math.Min(sx, sy) : style == 0 ? 1f : Math.Max(sx, sy);
            float w = style == 2 ? screen.Width : img.Width * scale, h = style == 2 ? screen.Height : img.Height * scale;
            float x = screen.X + (screen.Width - w) / 2 - virtRect.X, y = screen.Y + (screen.Height - h) / 2 - virtRect.Y;
            var state = g.Save();
            g.SetClip(new RectangleF((screen.X - virtRect.X) / (float)Shrink, (screen.Y - virtRect.Y) / (float)Shrink, screen.Width / (float)Shrink, screen.Height / (float)Shrink));
            g.DrawImage(img, x / Shrink, y / Shrink, w / Shrink, h / Shrink);
            g.Restore(state);
        }
    }
}
