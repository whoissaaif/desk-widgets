using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskWidgets;
using DeskWidgets.Widgets;

// Medium: one picture from a folder, changed on a schedule or with the next button (shown on
// hover). Just the picture: no file name or details. Stills are decoded on a background thread
// and kept only as a widget-sized bitmap; animated GIFs keep the compressed file in memory and
// decode one frame at a time, at the GIF's own frame rate.
[DeskWidget(Name = "Photo Frame", Description = "A picture from your folder, changes hourly")]
public sealed class PhotoFrame : CardWidget
{
    static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
    static readonly int[] Intervals = { 15, 60, 360, 1440 };
    static readonly string[] IntervalNames = { "Every 15 Minutes", "Every Hour", "Every 6 Hours", "Once a Day" };
    const int MaxGifBytes = 40 * 1024 * 1024; // bigger GIFs show their first frame only

    sealed class Animation : IDisposable
    {
        public MemoryStream Stream;  // GDI+ reads frames from it on demand, so it stays open
        public Image Image;
        public int Frames, Frame;
        public int[] DelaysMs;
        public RectangleF Source;    // cover-crop of the frame for the widget's shape

        public void Dispose() { Image.Dispose(); Stream.Dispose(); }
    }

    sealed class Loaded { public Bitmap Still; public Animation Anim; }

    Bitmap still;          // already cropped to the widget size
    Animation anim;
    int nextFrameDue;      // Environment.TickCount when the next GIF frame is due
    string photoPath;
    int index = -1;
    bool loading, hovering;
    readonly Timer shuffle = new Timer();
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
    bool Animate { get { return Settings.GetBool("animate", true); } }
    bool HasPicture { get { return still != null || anim != null; } }

    protected override void OnStart()
    {
        shuffle.Interval = Settings.GetInt("every", 60) * 60 * 1000;
        shuffle.Start();
        Next();
    }

    protected override void Changed()
    {
        // Size changed: re-crop the current picture for the new dimensions.
        if (photoPath != null && still != null && (still.Width != (int)Math.Round(W) || still.Height != (int)Math.Round(H)))
            ShowPicture(photoPath, index);
        base.Changed();
    }

    // Pick another picture from the folder (never the same one twice in a row).
    void Next()
    {
        if (loading) return;
        loading = true;
        string folder = Folder;
        bool deep = Subfolders;
        int current = index;
        Background(() =>
        {
            string[] files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*", deep ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            }
            catch (Exception) { files = new string[0]; } // missing folder or no access
            if (files.Length == 0) return null;
            int pick = random.Next(files.Length);
            if (files.Length > 1 && pick == current) pick = (pick + 1) % files.Length;
            return Tuple.Create(files[pick], pick);
        }, (choice, error) =>
        {
            if (choice == null) { loading = false; SetPicture(null, null, -1); return; }
            loading = false;
            ShowPicture(choice.Item1, choice.Item2);
        });
    }

    void ShowPicture(string path, int idx)
    {
        int w = (int)Math.Round(W), h = (int)Math.Round(H);
        loading = true;
        Background(() => LoadPicture(path, w, h), (result, error) =>
        {
            loading = false;
            SetPicture(error == null ? result : null, path, idx);
        });
    }

    void SetPicture(Loaded pic, string path, int idx)
    {
        if (still != null) { still.Dispose(); still = null; }
        if (anim != null) { anim.Dispose(); anim = null; }
        if (pic != null) { still = pic.Still; anim = pic.Anim; }
        photoPath = HasPicture ? path : null;
        index = idx;
        nextFrameDue = Environment.TickCount;
        Render();
    }

    // Worker thread: decode (and for stills, crop) the picture.
    static Loaded LoadPicture(string path, int w, int h)
    {
        byte[] bytes = File.ReadAllBytes(path); // don't keep the file locked
        var ms = new MemoryStream(bytes);
        Image img = null;
        try
        {
            img = Image.FromStream(ms, false, false);
            int frames = img.FrameDimensionsList.Contains(FrameDimension.Time.Guid) ? img.GetFrameCount(FrameDimension.Time) : 1;
            if (frames > 1 && bytes.Length <= MaxGifBytes)
            {
                var a = new Animation { Stream = ms, Image = img, Frames = frames, DelaysMs = FrameDelays(img, frames), Source = Cover(img.Size, w, h) };
                img = null; ms = null; // owned by the animation now
                return new Loaded { Anim = a };
            }
            ApplyExifOrientation(img);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            bmp.SetResolution(96, 96);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, new RectangleF(0, 0, w, h), Cover(img.Size, w, h), GraphicsUnit.Pixel);
            }
            return new Loaded { Still = bmp };
        }
        finally
        {
            if (img != null) img.Dispose();
            if (ms != null) ms.Dispose();
        }
    }

    static RectangleF Cover(Size img, int w, int h)
    {
        float scale = Math.Max(w / (float)img.Width, h / (float)img.Height);
        float sw = w / scale, sh = h / scale;
        return new RectangleF((img.Width - sw) / 2, (img.Height - sh) / 2, sw, sh);
    }

    // GIF frame delays (property 0x5100, hundredths of a second). Like browsers, very short
    // delays mean 100 ms.
    static int[] FrameDelays(Image img, int frames)
    {
        var delays = Enumerable.Repeat(100, frames).ToArray();
        const int FrameDelayId = 0x5100;
        if (Array.IndexOf(img.PropertyIdList, FrameDelayId) < 0) return delays;
        byte[] v = img.GetPropertyItem(FrameDelayId).Value;
        for (int i = 0; i < frames && i * 4 + 3 < v.Length; i++)
        {
            int cs = BitConverter.ToInt32(v, i * 4);
            delays[i] = cs <= 1 ? 100 : cs * 10;
        }
        return delays;
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

    // ---------- animation timing ----------
    const int MinFrameMs = 66;    // play GIFs at no more than ~15 frames per second
    const int CoveredCheckMs = 1000;

    protected override int NextFrameDelay()
    {
        if (anim == null || !Animate || !IsHandleCreated) return -1;
        if (IsCovered()) return CoveredCheckMs; // nobody can see it: check again in a second
        return Math.Max(10, nextFrameDue - Environment.TickCount);
    }

    void AdvanceFrameIfDue()
    {
        if (anim == null || !Animate || IsCovered()) return;
        int now = Environment.TickCount;
        if (now - nextFrameDue < 0) return; // a redraw for another reason (hover): keep the frame
        anim.Frame = (anim.Frame + 1) % anim.Frames;
        nextFrameDue = now + Math.Max(MinFrameMs, anim.DelaysMs[anim.Frame]);
    }

    // ---------- interaction ----------
    PointF NextCenter { get { return new PointF(W - P(16) - P(14), H - P(16) - P(14)); } }

    protected override bool IsButton(Point p)
    {
        PointF c = NextCenter;
        float dx = p.X - c.X, dy = p.Y - c.Y, r = P(17);
        return hovering && HasPicture && dx * dx + dy * dy <= r * r;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Render(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Render(); }

    protected override void OnWidgetClick(Point p)
    {
        if (IsButton(p)) Next();
        else if (photoPath != null) OpenPicture();
        else ChooseFolder();
    }

    void OpenPicture()
    {
        try { Process.Start(photoPath); } catch (Exception) { }
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
        menu.Add("Next Picture", Next);
        if (photoPath != null) menu.Add("Open Picture", OpenPicture);
        menu.Add("Choose Folder…", ChooseFolder);
        menu.AddCheck("Include Subfolders", Subfolders, delegate { Settings.Set("subfolders", !Subfolders); index = -1; Next(); });
        menu.AddCheck("Play Animated GIFs", Animate, delegate { Settings.Set("animate", !Animate); nextFrameDue = Environment.TickCount; Render(); });
        var every = menu.AddSubmenu("Change Picture");
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
    protected override Color CardBackground(Palette pal) { return HasPicture || !IsHandleCreated ? Color.Black : pal.Bg; }

    protected override void DrawContent(Graphics g, Palette pal)
    {
        float m = P(16);
        if (!HasPicture)
        {
            if (!IsHandleCreated) { DrawPreviewScene(g); return; }
            Label(g, loading ? "Loading pictures…" : "No pictures here yet", F(15, "semibold"), pal.Fg, W / 2, H / 2 - P(22), W - m * 2, StringAlignment.Center);
            Label(g, loading ? Folder : "Click to choose a folder", F(11, "regular"), pal.Fg2, W / 2, H / 2 + P(2), W - m * 2, StringAlignment.Center);
            return;
        }

        using (var card = CardPath())
        {
            var state = g.Save();
            g.SetClip(card);
            var dest = new RectangleF(0, 0, W, H);
            if (anim != null)
            {
                AdvanceFrameIfDue();
                anim.Image.SelectActiveFrame(FrameDimension.Time, anim.Frame);
                g.InterpolationMode = InterpolationMode.Bilinear; // per-frame: cheaper than bicubic
                DrawPicture(g, anim.Image, dest, anim.Source);
            }
            else DrawPicture(g, still, dest, new RectangleF(0, 0, still.Width, still.Height));
            g.Restore(state);
        }

        if (hovering)
        {
            PointF c = NextCenter;
            FillCircle(g, Color.FromArgb(110, 0, 0, 0), c.X, c.Y, P(14));
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
    }

    // Vibrant mode shows pictures desaturated, as macOS does for desktop widgets.
    void DrawPicture(Graphics g, Image img, RectangleF dest, RectangleF src)
    {
        if (!IsVibrant) { g.DrawImage(img, dest, src, GraphicsUnit.Pixel); return; }
        var gray = new ColorMatrix(new[]
        {
            new[] { 0.30f, 0.30f, 0.30f, 0, 0 }, new[] { 0.59f, 0.59f, 0.59f, 0, 0 }, new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new[] { 0f, 0, 0, 1, 0 }, new[] { 0f, 0, 0, 0, 1 }
        });
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(gray);
            g.DrawImage(img, new[] { dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) }, src, GraphicsUnit.Pixel, attrs);
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
            if (still != null) { still.Dispose(); still = null; }
            if (anim != null) { anim.Dispose(); anim = null; }
        }
        base.Dispose(disposing);
    }
}
