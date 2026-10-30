// Generates assets\app.ico (16-256 px, PNG entries). Re-run only when the icon design changes:
//   csc /nologo /r:System.Drawing.dll /out:%TEMP%\MakeIcon.exe tools\MakeIcon.cs && %TEMP%\MakeIcon.exe assets\app.ico
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = Render(s))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                images.Add(ms.ToArray());
            }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        using (var w = new BinaryWriter(File.Create(args[0])))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }

    // Dark rounded tile, white dial, hands at 10:10, orange seconds hand: the analog clock widget in miniature.
    static Bitmap Render(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float r = s * 0.22f, d = r * 2;
            using (var p = new GraphicsPath())
            {
                p.AddArc(0, 0, d, d, 180, 90); p.AddArc(s - d, 0, d, d, 270, 90);
                p.AddArc(s - d, s - d, d, d, 0, 90); p.AddArc(0, s - d, d, d, 90, 90);
                p.CloseFigure();
                using (var b = new LinearGradientBrush(new PointF(0, 0), new PointF(0, s), Color.FromArgb(58, 58, 62), Color.FromArgb(28, 28, 30)))
                    g.FillPath(b, p);
            }
            float c = s / 2f, face = s * 0.40f;
            g.FillEllipse(Brushes.White, c - face, c - face, face * 2, face * 2);

            if (s >= 32)
                using (var tick = new Pen(Color.FromArgb(120, 120, 126), Math.Max(1f, s * 0.025f)))
                    for (int i = 0; i < 12; i++)
                    {
                        double a = i * Math.PI / 6;
                        float dx = (float)Math.Sin(a), dy = (float)-Math.Cos(a);
                        g.DrawLine(tick, c + dx * face * 0.80f, c + dy * face * 0.80f, c + dx * face * 0.92f, c + dy * face * 0.92f);
                    }

            Hand(g, c, 300, face * 0.52f, Math.Max(1.6f, s * 0.075f), Color.FromArgb(17, 17, 17));
            Hand(g, c, 60, face * 0.80f, Math.Max(1.4f, s * 0.06f), Color.FromArgb(17, 17, 17));
            if (s >= 24) Hand(g, c, 150, face * 0.85f, Math.Max(1f, s * 0.025f), Color.FromArgb(255, 149, 0));
            float hub = Math.Max(1.2f, s * 0.045f);
            using (var b = new SolidBrush(Color.FromArgb(255, 149, 0)))
                g.FillEllipse(b, c - hub, c - hub, hub * 2, hub * 2);
        }
        return bmp;
    }

    static void Hand(Graphics g, float c, double degrees, float length, float width, Color color)
    {
        double a = degrees * Math.PI / 180;
        using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(pen, c, c, c + (float)Math.Sin(a) * length, c - (float)Math.Cos(a) * length);
    }
}
