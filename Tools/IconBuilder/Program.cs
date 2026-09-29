using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Reproducible vector-like artwork: a white T/caret over a blue keycap.
// No external image tooling, fonts, or dependencies are required.
var destination = Path.GetFullPath(args.Length > 0 ? args[0] : "../../App/Assets");
Directory.CreateDirectory(destination);
var sizes = new[] { 16, 20, 24, 28, 32, 40, 48, 64, 128, 256 };
foreach (var state in new[] { "running", "paused", "error" })
{
    var frames = new List<byte[]>();
    foreach (var size in sizes)
    {
        using var bitmap = Draw(size, state);
        using var png = new MemoryStream(); bitmap.Save(png, ImageFormat.Png); frames.Add(png.ToArray());
    }
    var name = state == "running" ? "typingstats" : "typingstats-" + state;
    using var output = new BinaryWriter(File.Create(Path.Combine(destination, name + ".ico")));
    output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
    var offset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
        output.Write(frames[i].Length); output.Write(offset); offset += frames[i].Length;
    }
    foreach (var frame in frames) output.Write(frame);
    using var preview = Draw(128, state); preview.Save(Path.Combine(destination, name + ".png"), ImageFormat.Png);
}
Console.WriteLine("Created 3 multi-resolution icons (16–256 px): " + destination);

static Bitmap Draw(int size, string state)
{
    // Render large and downsample for clean transparency at 16 and 20 px.
    const int canvas = 256;
    using var artwork = new Bitmap(canvas, canvas, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(artwork))
    {
        g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = RoundedRect(new RectangleF(12, 12, 232, 232), 48);
        using var keycap = new LinearGradientBrush(new Point(0, 0), new Point(256, 256), Color.FromArgb(38, 111, 181), Color.FromArgb(18, 66, 115));
        g.FillPath(keycap, outline);
        using var white = new SolidBrush(Color.White);
        // Wide, deliberately simple monogram remains readable at tray size.
        g.FillRectangle(white, 55, 58, 142, 30);
        g.FillRectangle(white, 105, 78, 34, 112);
        g.FillRectangle(white, 67, 168, 42, 22);
        if (state != "running")
        {
            using var border = new SolidBrush(Color.FromArgb(18, 46, 72));
            g.FillEllipse(border, 144, 144, 108, 108);
            using var badge = new SolidBrush(state == "paused" ? Color.FromArgb(248, 190, 63) : Color.FromArgb(220, 65, 77));
            g.FillEllipse(badge, 152, 152, 92, 92);
            if (state == "paused")
            {
                using var dark = new SolidBrush(Color.FromArgb(65, 45, 12));
                g.FillRectangle(dark, 176, 174, 13, 48); g.FillRectangle(dark, 205, 174, 13, 48);
            }
            else
            {
                g.FillRectangle(white, 190, 170, 16, 33); g.FillEllipse(white, 190, 212, 16, 16);
            }
        }
    }
    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(result))
    { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(artwork, new Rectangle(0, 0, size, size)); }
    return result;
}

static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    var p = new GraphicsPath(); var d = radius * 2;
    p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
}
