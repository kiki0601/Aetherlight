using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace Aetherlight;

internal readonly record struct RawWhiteBalance(double Kelvin, double Tint);

internal static class RawWhiteBalanceReader
{
    public static RawWhiteBalance Read(string path)
    {
        double kelvin = 6500;
        double tint = 0;
        try
        {
            var metadata = ImageMetadataReader.ReadMetadata(path);
            foreach (var tag in metadata.SelectMany(d => d.Tags))
            {
                string name = tag.Name ?? string.Empty;
                string description = tag.Description ?? string.Empty;
                string text = $"{name} {description}";
                string lower = text.ToLowerInvariant();

                if (lower.Contains("color temperature") || lower.Contains("colour temperature") || lower.Contains("white balance temperature"))
                {
                    double? parsed = ExtractNumber(description);
                    if (parsed is >= 1500 and <= 20000) kelvin = parsed.Value;
                }

                if (lower.Contains("wb shift") || lower.Contains("white balance shift") || lower.Contains("tint") || lower.Contains("green/magenta") || lower.Contains("g/m shift"))
                {
                    double? parsed = ExtractSignedNumber(description);
                    if (parsed.HasValue && Math.Abs(parsed.Value) <= 100) tint = parsed.Value;
                }
            }
        }
        catch
        {
            // Metadata is optional. LibRaw decoding remains usable when a camera's maker notes are unavailable.
        }
        return new RawWhiteBalance(Math.Clamp(kelvin, 1500, 20000), Math.Clamp(tint, -100, 100));
    }

    private static double? ExtractNumber(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)(\d{3,5})(?:\s*K)?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }

    private static double? ExtractSignedNumber(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)([+-]?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }
}
