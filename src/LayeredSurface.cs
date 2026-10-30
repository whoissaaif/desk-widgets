using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace DeskWidgets
{
    // A 32-bit premultiplied DIB that GDI+ draws straight into and that is pushed to a
    // layered window with UpdateLayeredWindow. Allocated once per size: no per-frame
    // bitmap allocations or copies.
    sealed class LayeredSurface : IDisposable
    {
        public readonly int Width, Height;
        public readonly Bitmap Bitmap;
        public readonly Graphics Graphics;
        readonly IntPtr hdc, hbmp, oldBmp;

        public LayeredSurface(int width, int height)
        {
            Width = width;
            Height = height;
            var bi = new Native.BITMAPINFOHEADER
            {
                biSize = 40, biWidth = width, biHeight = -height, // negative = top-down rows
                biPlanes = 1, biBitCount = 32
            };
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            hdc = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            IntPtr bits;
            hbmp = Native.CreateDIBSection(hdc, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(hdc, hbmp);
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
            Bitmap.SetResolution(96, 96);
            Graphics = Graphics.FromImage(Bitmap);
        }

        public void Present(IntPtr hwnd, byte alpha)
        {
            Graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            var size = new Native.SIZE { cx = Width, cy = Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, hdc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }

        public void Dispose()
        {
            Graphics.Dispose();
            Bitmap.Dispose();
            Native.SelectObject(hdc, oldBmp);
            Native.DeleteObject(hbmp);
            Native.DeleteDC(hdc);
        }
    }
}
