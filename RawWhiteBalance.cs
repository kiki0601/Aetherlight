using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace Aetherlight;

internal readonly record struct RawWhiteBalance(double Kelvin, double Tint);

internal static class RawWhiteBalanceReader
{
    public static RawWhiteBalance Read(string path)
    {
        double? asShotKelvin = null;
        double? genericKelvin = null;
        double? tint = null;

        try
        {
            var metadata = ImageMetadataReader.ReadMetadata(path);
            foreach (var tag in metadata.SelectMany(d => d.Tags))
            {
                string name = tag.Name ?? string.Empty;
                string description = tag.Description ?? string.Empty;
                string lowerName = name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
                string lower = $"{name} {description}".ToLowerInvariant();

                // Canon CR3/CR2 and several other cameras expose the exact in-camera
                // value as ColorTempAsShot. This must win over generic ColorTemperature.
                if (lowerName.Contains("colortempasshot") || lowerName.Contains("whitebalancetempasshot"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue) asShotKelvin = parsed.Value;
                    continue;
                }

                if (lowerName.Contains("colortempkelvin") || lowerName.Contains("colortemperature") ||
                    lower.Contains("color temperature") || lower.Contains("colour temperature"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue) genericKelvin = parsed.Value;
                }

                // Green/Magenta is the Lightroom Tint axis. Do not mistake
                // Amber/Blue WB shifts for Tint.
                if (lowerName.Contains("wbshiftgm") || lowerName.Contains("wbshiftgreenmagenta") ||
                    lowerName.Contains("tint") || lower.Contains("green/magenta") || lower.Contains("g/m shift"))
                {
                    var parsed = ExtractSignedNumber(description);
                    if (parsed.HasValue && Math.Abs(parsed.Value) <= 100) tint = parsed.Value;
                }
            }
        }
        catch
        {
            // Metadata is optional. LibRaw decoding remains usable when a camera's
            // maker notes are unavailable or unsupported.
        }

        double kelvin = asShotKelvin ?? genericKelvin ?? 6500;
        return new RawWhiteBalance(Math.Clamp(kelvin, 1500, 20000), Math.Clamp(tint ?? 0, -100, 100));
    }

    private static double? ExtractTemperature(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)(\d{3,5})(?:\s*K)?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && value is >= 1500 and <= 20000 ? value : null;
    }

    private static double? ExtractSignedNumber(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)([+-]?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }
}
