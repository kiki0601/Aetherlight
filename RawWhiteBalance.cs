using System.Globalization;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace Aetherlight;

internal readonly record struct RawWhiteBalance(double Kelvin, double Tint);

internal static class RawWhiteBalanceReader
{
    public static RawWhiteBalance Read(string path)
    {
        double? canonKelvin = null;
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
                string lowerName = Normalize(name);
                string lower = $"{name} {description}".ToLowerInvariant();

                // Canon ColorData11 (EOS R6 Mark II/R7/R10/R50) records both
                // ColorTempAsShot and ColorTempKelvin. When the camera was set
                // to Manual Temperature (Kelvin), ColorTempKelvin is the actual
                // user-selected WB and must be preferred over the other Canon
                // informational temperature field.
                if (lowerName.Contains("colortempkelvin") || lowerName.Contains("colortemperaturekelvin"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue) canonKelvin = parsed.Value;
                    continue;
                }

                if (lowerName.Contains("colortempasshot") || lowerName.Contains("whitebalancetempasshot"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue) asShotKelvin = parsed.Value;
                    continue;
                }

                if (lowerName.Contains("colortempcustom") || lowerName.Contains("colortemperaturecustom"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue && !canonKelvin.HasValue) canonKelvin = parsed.Value;
                    continue;
                }

                if (lowerName.Contains("colortemperature") || lower.Contains("color temperature") || lower.Contains("colour temperature"))
                {
                    var parsed = ExtractTemperature(description);
                    if (parsed.HasValue) genericKelvin = parsed.Value;
                }

                // Positive Canon WBShiftGM means green; Lightroom's Tint axis is
                // green-negative / magenta-positive, so invert the sign here.
                if (lowerName.Contains("wbshiftgm") || lowerName.Contains("wbshiftgreenmagenta") ||
                    lowerName.Contains("tint") || lower.Contains("green/magenta") || lower.Contains("g/m shift"))
                {
                    var parsed = ExtractSignedNumber(description);
                    if (parsed.HasValue && Math.Abs(parsed.Value) <= 100)
                        tint = lowerName.Contains("wbshiftgm") ? -parsed.Value : parsed.Value;
                }
            }
        }
        catch
        {
            // Metadata is optional. RAW decoding remains usable if maker notes
            // aren't available or aren't understood by the metadata library.
        }

        double kelvin = canonKelvin ?? asShotKelvin ?? genericKelvin ?? 6500;
        return new RawWhiteBalance(Math.Clamp(kelvin, 1500, 50000), Math.Clamp(tint ?? 0, -150, 150));
    }

    private static string Normalize(string value) =>
        value.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Replace("/", string.Empty);

    private static double? ExtractTemperature(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)(\d{3,5})(?:\s*K)?", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && value is >= 1500 and <= 50000 ? value : null;
    }

    private static double? ExtractSignedNumber(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"(?<!\d)([+-]?\d{1,3}(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    }
}
