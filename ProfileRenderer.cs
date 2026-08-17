using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aetherlight;

internal sealed record ProfileDefinition(string Name, string Group);

internal static class ProfileRenderer
{
    public static BitmapSource Render(BitmapSource source, ProfileDefinition profile, int maxWidth = 190)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = Math.Min(maxWidth, converted.PixelWidth);
        int height = Math.Max(1, (int)Math.Round(converted.PixelHeight * (width / (double)converted.PixelWidth)));
        byte[] src = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(src, converted.PixelWidth * 4, 0);
        byte[] dst = new byte[checked(width * height * 4)];
        double sx = converted.PixelWidth / (double)width;
        double sy = converted.PixelHeight / (double)height;

        for (int y = 0; y < height; y++)
        {
            int py = Math.Min(converted.PixelHeight - 1, (int)(y * sy));
            for (int x = 0; x < width; x++)
            {
                int px = Math.Min(converted.PixelWidth - 1, (int)(x * sx));
                int si = (py * converted.PixelWidth + px) * 4;
                int di = (y * width + x) * 4;
                double b = src[si] / 255.0, g = src[si + 1] / 255.0, r = src[si + 2] / 255.0;
                Apply(profile.Name, ref r, ref g, ref b);
                dst[di] = ToByte(b); dst[di + 1] = ToByte(g); dst[di + 2] = ToByte(r); dst[di + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), dst, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    public static void Apply(string profile, ref double r, ref double g, ref double b)
    {
        double luma = .2126 * r + .7152 * g + .0722 * b;
        switch (profile)
        {
            case "Camera Standard":
                Tone(ref r, ref g, ref b, 1.07, 1.04, 0.008, 0.0, 0.0);
                break;
            case "Camera Faithful":
                Tone(ref r, ref g, ref b, 1.01, 0.98, 0.002, 0.0, 0.0);
                break;
            case "Camera Landscape":
                Tone(ref r, ref g, ref b, 1.10, 1.15, 0.0, 0.018, 0.028);
                break;
            case "Camera Neutral":
                Tone(ref r, ref g, ref b, 0.94, 0.92, -0.002, 0.0, 0.0);
                break;
            case "Camera Portrait":
                Tone(ref r, ref g, ref b, 0.98, 0.96, 0.014, -0.006, -0.004);
                break;
            case "Camera Monochrome":
                r = g = b = luma;
                r = g = b = (r - .5) * 1.08 + .5;
                break;
            case "Adobe Color":
                Tone(ref r, ref g, ref b, 1.06, 1.06, 0.008, 0.0, 0.004);
                break;
            case "Adobe Standard":
                Tone(ref r, ref g, ref b, 1.02, 1.01, 0.0, 0.0, 0.0);
                break;
            case "Adobe Landscape":
                Tone(ref r, ref g, ref b, 1.08, 1.12, 0.0, 0.015, 0.025);
                break;
            case "Adobe Portrait":
                Tone(ref r, ref g, ref b, 0.98, 0.95, 0.012, -0.004, -0.003);
                break;
            case "Adobe Neutral":
                Tone(ref r, ref g, ref b, 0.90, 0.90, 0.0, 0.0, 0.0);
                break;
            case "Adobe Monochrome":
                r = g = b = (luma - .5) * 1.10 + .5;
                break;
        }
    }

    private static void Tone(ref double r, ref double g, ref double b, double contrast, double saturation, double warm, double green, double blue)
    {
        r += warm; g += green; b += blue;
        double gray = .2126 * r + .7152 * g + .0722 * b;
        r = gray + (r - gray) * saturation;
        g = gray + (g - gray) * saturation;
        b = gray + (b - gray) * saturation;
        r = (r - .5) * contrast + .5;
        g = (g - .5) * contrast + .5;
        b = (b - .5) * contrast + .5;
    }

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255.0);
}
