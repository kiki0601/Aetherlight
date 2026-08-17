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
            double? temperature = null;
            double? tint = null;

            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string combined = $"{name} {description}";

                if (!temperature.HasValue && name.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase))
                {
                    var value = ExtractFirstNumber(description);
                    if (value.HasValue && value.Value >= 1000 && value.Value <= 20000)
                        temperature = value.Value;
                }

                if (!tint.HasValue &&
                    (name.Contains("WB Shift GM", StringComparison.OrdinalIgnoreCase) ||
                     name.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase)))
                {
                    var value = ExtractSignedNumber(description);
                    if (value.HasValue && value.Value >= -100 && value.Value <= 100)
                        tint = value.Value;
                }

                if (!tint.HasValue && name.Contains("WB Shift", StringComparison.OrdinalIgnoreCase) && combined.Contains("GM", StringComparison.OrdinalIgnoreCase))
                {
                    var value = ExtractSignedNumber(description);
                    if (value.HasValue && value.Value >= -100 && value.Value <= 100)
                        tint = value.Value;
                }
            }

            if (temperature.HasValue)
            {
                _asShotTemperature = (int)Math.Round(temperature.Value);
                _whiteBalanceMetadataFound = true;
            }
            if (tint.HasValue)
            {
                _asShotTint = (int)Math.Round(tint.Value);
                _whiteBalanceMetadataFound = true;
            }
        }
        catch
        {
        }

        _loading = true;
        TemperatureSlider.Value = 0;
        TintSlider.Value = 0;
        _loading = false;
        UpdateWhiteBalanceLabels();
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
