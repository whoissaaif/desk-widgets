using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace DeskWidgets.Widgets
{
    // Medium: one picture from a folder, changed on a schedule or with the next button.
    // Pictures are decoded on a background thread and scaled once to the widget's size, so only a
    // widget-sized bitmap (a few hundred KB) stays in memory, never the full photo.
    sealed class PhotoFrame : CardWidget
    {
        static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
        static readonly int[] Intervals = { 15, 60, 360, 1440 };
        static readonly string[] IntervalNames = { "Every 15 minutes", "Every hour", "Every 6 hours", "Once a day" };

        Bitmap photo;          // already cropped to the widget size
        string photoPath;
        int index = -1, count;
        bool loading;
        readonly System.Windows.Forms.Timer shuffle = new System.Windows.Forms.Timer();
        readonly Random random = new Random();

        public PhotoFrame(Section settings) : base(settings, true)
        {
            shuffle.Tick += delegate { Next(); };
        }

        string Folder
        {
            get
            {
                string f = Settings.Get("folder", "");
                return f.Length > 0 ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }
        }

        bool Subfolders { get { return Settings.GetBool("subfolders", false); } }

        protected override void OnStart()
        {
            shuffle.Interval = Settings.GetInt("every", 60) * 60 * 1000;
            shuffle.Start();
            Next();
        }

        protected override void Changed()
        {
            // Size changed: re-crop the current picture for the new dimensions.
            if (photoPath != null && photo != null && (photo.Width != (int)Math.Round(W) || photo.Height != (int)Math.Round(H)))
                Reload(photoPath, index, count);
            base.Changed();
        }

        void Next()
        {
            if (loading) return;
            loading = true;
            string folder = Folder;
            bool deep = Subfolders;
            int current = index, w = (int)Math.Round(W), h = (int)Math.Round(H);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*", deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                        .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
                }
                catch (Exception) { files = new string[0]; } // missing folder or no access
                int pick = -1;
                if (files.Length > 0)
                {
                    pick = random.Next(files.Length);
                    if (files.Length > 1 && pick == current) pick = (pick + 1) % files.Length;
                }
                Deliver(pick >= 0 ? files[pick] : null, pick, files.Length, w, h);
            });
        }

        void Reload(string path, int idx, int total)
        {
            int w = (int)Math.Round(W), h = (int)Math.Round(H);
            ThreadPool.QueueUserWorkItem(delegate { Deliver(path, idx, total, w, h); });
        }

        // Runs on a worker thread: decode + crop, then hand the result to the UI thread.
        void Deliver(string path, int idx, int total, int w, int h)
        {
            Bitmap scaled = null;
            if (path != null)
            {
                try { scaled = CoverCrop(path, w, h); }
                catch (Exception) { scaled = null; } // unreadable file: show the placeholder
            }
            if (IsDisposed || !IsHandleCreated) { if (scaled != null) scaled.Dispose(); return; }
            try
            {
                BeginInvoke((Action)delegate
                {
                    loading = false;
                    if (photo != null) photo.Dispose();
                    photo = scaled;
                    photoPath = scaled != null ? path : null;
                    index = idx;
                    count = total;
                    Render();
                });
            }
            catch (InvalidOperationException) { if (scaled != null) scaled.Dispose(); }
        }

        static Bitmap CoverCrop(string path, int w, int h)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path))) // don't keep the file locked
            using (var img = Image.FromStream(ms, false, false))
            {
                ApplyExifOrientation(img);
                float scale = Math.Max(w / (float)img.Width, h / (float)img.Height);
                float sw = w / scale, sh = h / scale;
                var src = new RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(img, new RectangleF(0, 0, w, h), src, GraphicsUnit.Pixel);
                }
                return bmp;
            }
        }

        static void ApplyExifOrientation(Image img)
        {
            const int OrientationId = 0x0112;
            if (Array.IndexOf(img.PropertyIdList, OrientationId) < 0) return;
            int o = img.GetPropertyItem(OrientationId).Value[0];
            RotateFlipType t = o == 3 ? RotateFlipType.Rotate180FlipNone : o == 6 ? RotateFlipType.Rotate90FlipNone :
                               o == 8 ? RotateFlipType.Rotate270FlipNone : o == 2 ? RotateFlipType.RotateNoneFlipX :
                               o == 4 ? RotateFlipType.RotateNoneFlipY : RotateFlipType.RotateNoneFlipNone;
            if (t != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(t);
        }

        // ---------- interaction ----------
        PointF NextCenter { get { return new PointF(W - P(16) - P(14), H - P(16) - P(14)); } }

        protected override bool IsButton(Point p)
        {
            PointF c = NextCenter;
            float dx = p.X - c.X, dy = p.Y - c.Y, r = P(17);
            return photo != null && dx * dx + dy * dy <= r * r;
        }

        protected override void OnWidgetClick(Point p)
        {
            if (IsButton(p)) { Next(); return; }
            if (photoPath != null)
            {
                try { Process.Start(photoPath); } catch (Exception) { }
            }
            else ChooseFolder();
        }

        void ChooseFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the folder of pictures to show", SelectedPath = Folder })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                Settings.Set("folder", dlg.SelectedPath);
                index = -1;
                Next();
            }
        }

        protected override void BuildWidgetMenu(PopupMenu menu)
        {
            menu.Add("Next picture", Next);
            menu.Add("Choose folder…", ChooseFolder);
            menu.AddCheck("Include subfolders", Subfolders, delegate { Settings.Set("subfolders", !Subfolders); index = -1; Next(); });
            var every = menu.AddSubmenu("Change picture");
            int cur = Settings.GetInt("every", 60);
            for (int i = 0; i < Intervals.Length; i++)
            {
                int min = Intervals[i];
                every.AddChoice(IntervalNames[i], cur == min, delegate
                {
                    Settings.Set("every", min);
                    shuffle.Interval = min * 60 * 1000;
                    shuffle.Stop();
                    shuffle.Start();
                });
            }
        }

        // ---------- drawing ----------
        protected override Color CardBackground(Palette pal) { return photo != null || !IsHandleCreated ? Color.Black : pal.Bg; }

        protected override void DrawContent(Graphics g, Palette pal)
        {
            float m = P(16);
            if (photo == null)
            {
                if (!IsHandleCreated) { DrawPreviewScene(g); DrawCaption(g, "Evening tide", "Pictures · 1 of 24"); return; }
                Label(g, loading ? "Loading pictures…" : "No pictures here yet", F(15, "semibold"), pal.Fg, W / 2, H / 2 - P(22), W - m * 2, StringAlignment.Center);
                Label(g, loading ? Folder : "Click to choose a folder", F(11, "regular"), pal.Fg2, W / 2, H / 2 + P(2), W - m * 2, StringAlignment.Center);
                return;
            }
            using (var card = CardPath())
            {
                g.SetClip(card);
                g.DrawImage(photo, new RectangleF(0, 0, W, H));
                g.ResetClip();
            }
            string folderName = Path.GetFileName(Folder.TrimEnd('\\'));
            DrawCaption(g, Path.GetFileNameWithoutExtension(photoPath), folderName + " · " + (index + 1) + " of " + count);
        }

        void DrawCaption(Graphics g, string title, string sub)
        {
            float m = P(16);
            using (var card = CardPath())
            using (var shade = new LinearGradientBrush(new PointF(0, H * 0.45f), new PointF(0, H + 1), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(150, 0, 0, 0)))
            {
                g.SetClip(card);
                g.FillRectangle(shade, 0, H * 0.45f, W, H * 0.55f + 1);
                g.ResetClip();
            }
            float textW = W - m * 2 - P(36);
            Label(g, title, F(13, "semibold"), Color.White, m, H - m - P(30), textW, StringAlignment.Near);
            Label(g, sub, F(11, "regular"), Color.FromArgb(200, 255, 255, 255), m, H - m - P(13), textW, StringAlignment.Near);

            PointF c = NextCenter;
            FillCircle(g, Color.FromArgb(70, 255, 255, 255), c.X, c.Y, P(14));
            using (var b = new SolidBrush(Color.White))
            {
                float s = P(4.5f);
                for (int k = 0; k < 2; k++)
                {
                    float x = c.X - P(4.5f) + k * P(4.5f);
                    g.FillPolygon(b, new[] { new PointF(x, c.Y - s), new PointF(x, c.Y + s), new PointF(x + s, c.Y) });
                }
            }
        }

        // Library preview: a painted dusk scene, so the card shows what the widget looks like.
        void DrawPreviewScene(Graphics g)
        {
            using (var card = CardPath())
            {
                g.SetClip(card);
                using (var sky = new LinearGradientBrush(new PointF(0, 0), new PointF(0, H), Rgb(0x2B3A67), Rgb(0xF2CC8F)))
                {
                    var blend = new ColorBlend { Colors = new[] { Rgb(0x2B3A67), Rgb(0xE07A5F), Rgb(0xF2CC8F) }, Positions = new[] { 0f, 0.6f, 1f } };
                    sky.InterpolationColors = blend;
                    g.FillRectangle(sky, 0, 0, W, H);
                }
                FillCircle(g, Rgb(0xFFE3A3), W * 0.7f, H * 0.55f, P(14));
                using (var hills = new GraphicsPath())
                {
                    hills.AddBezier(0, H * 0.62f, W * 0.3f, H * 0.52f, W * 0.6f, H * 0.7f, W, H * 0.6f);
                    hills.AddLine(W, H * 0.6f, W, H);
                    hills.AddLine(W, H, 0, H);
                    hills.CloseFigure();
                    using (var b = new SolidBrush(Rgb(0x3D405B))) g.FillPath(b, hills);
                }
                using (var sea = new SolidBrush(Rgb(0x1F2440))) g.FillRectangle(sea, 0, H * 0.72f, W, H * 0.28f);
                g.ResetClip();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuffle.Dispose();
                if (photo != null) { photo.Dispose(); photo = null; }
            }
            base.Dispose(disposing);
        }
    }
}
