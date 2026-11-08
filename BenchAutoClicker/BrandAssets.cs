using System.Reflection;
using System.Drawing.Drawing2D;

namespace BenchAutoClicker;

internal static class BrandAssets
{
    private const string IconResource = "BenchAutoClicker.Assets.BenchAutoClicker.ico";
    private const string PngResource = "BenchAutoClicker.Assets.bench-auto-clicker-icon.png";

    public static Icon LoadIcon()
    {
        using var stream = OpenResource(IconResource);
        using var sourceIcon = new Icon(stream);
        return (Icon)sourceIcon.Clone();
    }

    public static Bitmap LoadBitmap()
    {
        using var stream = OpenResource(PngResource);
        using var sourceImage = Image.FromStream(stream);
        return new Bitmap(sourceImage);
    }

    public static Icon LoadTrayIcon(bool isDark)
    {
        using var source = LoadBitmap();
        var foreground = SystemInformation.HighContrast
            ? SystemColors.WindowText
            : isDark ? Color.White : Color.FromArgb(32, 32, 32);
        using var monochrome = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                monochrome.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, foreground));
            }
        }
        // Include normal and high-DPI shell sizes; ICO decoding owns its copied data.
        using var stream = new MemoryStream();
        var images = new List<byte[]>();
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64 };
        foreach (var size in sizes)
        {
            using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(monochrome, new Rectangle(0, 0, size, size));
            using var imageStream = new MemoryStream();
            bitmap.Save(imageStream, System.Drawing.Imaging.ImageFormat.Png);
            images.Add(imageStream.ToArray());
        }
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            var offset = 6 + (sizes.Length * 16);
            for (var index = 0; index < sizes.Length; index++)
            {
                writer.Write((byte)sizes[index]);
                writer.Write((byte)sizes[index]);
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(images[index].Length);
                writer.Write(offset);
                offset += images[index].Length;
            }
            foreach (var bytes in images)
            {
                writer.Write(bytes);
            }
        }
        stream.Position = 0;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private static Stream OpenResource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Не найден ресурс оформления: {name}");
    }
}
