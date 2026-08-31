using System.Reflection;

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

    private static Stream OpenResource(string name)
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Не найден ресурс оформления: {name}");
    }
}
