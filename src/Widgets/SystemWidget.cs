using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskWidgets.Widgets
{
    // Small: CPU load with one minute of history, memory and disk use.
    // Reads straight from Windows (GetSystemTimes / GlobalMemoryStatusEx), so a sample costs microseconds.
    sealed class SystemWidget : CardWidget
    {
        const int History = 30;          // samples at 2 s = one minute
        const int SampleMs = 2000;

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        readonly List<float> cpu = new List<float>();
        static readonly List<float> PreviewSeries = new List<float> { 8, 11, 9, 14, 22, 18, 12, 10, 9, 16, 28, 24, 15, 11, 9, 8, 12, 19, 14, 10, 9, 13, 21, 17, 12, 10, 11, 15, 13, 12 };
        long lastIdle, lastTotal;
        double memUsedGb, memTotalGb;
        float diskUsed = -1;
        DateTime diskReadAt;

        public SystemWidget(Section settings) : base(settings, false)
        {
            Sample();
        }

        string Drive { get { return Settings.Get("drive", "C"); } }

        protected override int NextFrameDelay()
        {
            return IsHandleCreated ? SampleMs : -1;
        }

        protected override void Draw(Graphics g)
        {
            if (IsHandleCreated) Sample();
            base.Draw(g);
        }

        void Sample()
        {
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                long total = kernel + user; // kernel time includes idle time
                if (lastTotal != 0 && total > lastTotal)
                {
                    float load = 100f * (1f - (idle - lastIdle) / (float)(total - lastTotal));
                    cpu.Add(Math.Max(0, Math.Min(100, load)));
                    if (cpu.Count > History) cpu.RemoveAt(0);
                }
                lastIdle = idle;
                lastTotal = total;
            }

            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            if (GlobalMemoryStatusEx(ref m))
            {
                memTotalGb = m.ullTotalPhys / 1073741824.0;
                memUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
            }

            if (diskUsed < 0 || (DateTime.UtcNow - diskReadAt).TotalSeconds > 60) // disk space changes slowly
            {
                diskReadAt = DateTime.UtcNow;
                try
                {
                    var d = new DriveInfo(Drive);
                    diskUsed = d.IsReady ? 100f * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize : -1;
                }
                catch (Exception) { diskUsed = -1; }
            }
        }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            float m = P(16), w = W - m * 2;
            // Library preview: no live history yet, so show a plausible minute.
            List<float> cpu = this.cpu.Count < 2 && !IsHandleCreated ? PreviewSeries : this.cpu;
            float now = cpu.Count > 0 ? cpu[cpu.Count - 1] : 0;

            Label(g, "CPU", F(11, "semibold"), pal.Fg2, m, m);
            Label(g, Math.Round(now) + "%", F(26, "semibold"), pal.Fg, m - P(1), m + P(11));

            // Sparkline: area + line + endpoint, faint quarter grid lines.
            float top = P(62), h = P(30);
            float max = 40;
            foreach (var v in cpu) max = Math.Max(max, v * 1.25f);
            using (var grid = new Pen(pal.Sep, 1))
                for (int i = 1; i <= 3; i++) g.DrawLine(grid, m, top + h * i / 4, m + w, top + h * i / 4);
            if (cpu.Count > 1)
            {
                var pts = new PointF[cpu.Count];
                float step = w / (History - 1), x0 = m + w - step * (cpu.Count - 1);
                for (int i = 0; i < cpu.Count; i++) pts[i] = new PointF(x0 + i * step, top + h - cpu[i] / max * h);
                using (var area = new GraphicsPath())
                {
                    area.AddLines(pts);
                    area.AddLine(pts[pts.Length - 1], new PointF(pts[pts.Length - 1].X, top + h));
                    area.AddLine(new PointF(pts[pts.Length - 1].X, top + h), new PointF(pts[0].X, top + h));
                    area.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(60, pal.Blue))) g.FillPath(b, area);
                }
                using (var pen = new Pen(pal.Blue, P(1.5f)) { LineJoin = LineJoin.Round })
                    g.DrawLines(pen, pts);
                var last = pts[pts.Length - 1];
                FillCircle(g, pal.Blue, last.X - P(1.5f), last.Y, P(2.2f));
            }

            Meter(g, pal, "Memory", memUsedGb.ToString("0.0") + " of " + Math.Round(memTotalGb) + " GB",
                memTotalGb > 0 ? (float)(memUsedGb / memTotalGb) : 0, pal.Purple, P(100));
            Meter(g, pal, "Disk " + Drive + ":", diskUsed < 0 ? "—" : Math.Round(diskUsed) + "% used",
                diskUsed < 0 ? 0 : diskUsed / 100f, pal.Orange, P(126));
        }

        void Meter(Graphics g, Palette pal, string name, string value, float fraction, Color color, float y)
        {
            float m = P(16), w = W - m * 2;
            Label(g, name, F(11, "semibold"), pal.Fg, m, y);
            Label(g, value, F(11, "regular"), pal.Fg2, W - m, y, w * 0.6f, StringAlignment.Far);
            float by = y + P(16), bh = P(5);
            FillRound(g, pal.Fill, new RectangleF(m, by, w, bh), bh / 2);
            if (fraction > 0) FillRound(g, color, new RectangleF(m, by, Math.Max(bh, w * Math.Min(1, fraction)), bh), bh / 2);
        }

        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            var drives = menu.AddSubmenu("Drive");
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed) continue;
                string letter = d.Name.Substring(0, 1);
                drives.AddChoice(letter + ":", letter == Drive, delegate { Settings.Set("drive", letter); diskUsed = -1; Changed(); });
            }
        }
    }
}
