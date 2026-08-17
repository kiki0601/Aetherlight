using System.Globalization;
using MetadataExtractor;

namespace Aetherlight;

public partial class MainWindow
{
    private int _asShotTemperature = 6500;
    private int _asShotTint = 0;
    private bool _whiteBalanceMetadataFound;

    private void LoadAsShotWhiteBalance(string path)
    {
        _asShotTemperature = 6500;
        _asShotTint = 0;
        _whiteBalanceMetadataFound = false;

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            double? asShotTemperature = null;
            double? asShotTint = null;
            double? fallbackTemperature = null;
            double? fallbackTint = null;

            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string normalizedName = name.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

                // Canon maker-note fields. Prefer the explicit as-shot field over generic temperature fields.
                if (asShotTemperature == null &&
                    (normalizedName.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) ||
                     normalizedName.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ExtractFirstNumber(description);
                    if (IsKelvin(value)) asShotTemperature = value;
                }

                if (asShotTint == null && IsCanonAsShotTintTag(normalizedName))
                {
                    var value = ExtractSignedNumber(description);
                    if (IsTint(value)) asShotTint = value;
                }

                // Generic fallback for RAW formats that expose WB through standard metadata names.
                if (fallbackTemperature == null &&
                    (name.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase) ||
                     normalizedName.Contains("ColorTemperature", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ExtractFirstNumber(description);
                    if (IsKelvin(value)) fallbackTemperature = value;
                }

                if (fallbackTint == null &&
                    (name.Contains("WB Shift GM", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase) ||
                     (name.Contains("WB Shift", StringComparison.OrdinalIgnoreCase) &&
                      (name.Contains("GM", StringComparison.OrdinalIgnoreCase) || description.Contains("GM", StringComparison.OrdinalIgnoreCase)))))
                {
                    var value = ExtractSignedNumber(description);
                    if (IsTint(value)) fallbackTint = value;
                }
            }

            if (asShotTemperature.HasValue)
            {
                _asShotTemperature = (int)Math.Round(asShotTemperature.Value);
                _whiteBalanceMetadataFound = true;
            }
            else if (fallbackTemperature.HasValue)
            {
                _asShotTemperature = (int)Math.Round(fallbackTemperature.Value);
                _whiteBalanceMetadataFound = true;
            }

            if (asShotTint.HasValue)
            {
                _asShotTint = (int)Math.Round(asShotTint.Value);
                _whiteBalanceMetadataFound = true;
            }
            else if (fallbackTint.HasValue)
            {
                _asShotTint = (int)Math.Round(fallbackTint.Value);
                _whiteBalanceMetadataFound = true;
            }
        }
        catch
        {
            // Metadata is optional. RAW development must still work if a file has no readable maker-note WB metadata.
        }

        _loading = true;
        TemperatureSlider.Value = 0;
        TintSlider.Value = 0;
        _loading = false;
        UpdateWhiteBalanceLabels();
    }

    private static bool IsKelvin(double? value) => value.HasValue && value.Value >= 1500 && value.Value <= 20000;
    private static bool IsTint(double? value) => value.HasValue && value.Value >= -100 && value.Value <= 100;

    private static bool IsCanonAsShotTintTag(string normalizedName)
    {
        return normalizedName.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase) ||
               normalizedName.Contains("WBShiftGreenMagenta", StringComparison.OrdinalIgnoreCase) ||
               normalizedName.Contains("WhiteBalanceShiftGM", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateWhiteBalanceLabels()
    {
        int currentTemperature = _asShotTemperature + (int)Math.Round(TemperatureSlider.Value);
        int currentTint = _asShotTint + (int)Math.Round(TintSlider.Value);
        TemperatureValue.Text = $"{currentTemperature} K";
        TintValue.Text = currentTint.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    }

    private static double? ExtractFirstNumber(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[-+]?\d+(?:[\.,]\d+)?");
        if (!match.Success) return null;
        return double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static double? ExtractSignedNumber(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"[-+]?\d+(?:[\.,]\d+)?");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return null;
    }
}
