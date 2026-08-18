using System.Globalization;
using System.Windows;
using MetadataExtractor;

namespace Aetherlight;

public partial class MainWindow
{
    private const double MinKelvin = 2000.0;
    private const double MaxKelvin = 50000.0;
    private int _asShotTemperature = 6500;
    private int _asShotTint = 0;
    private bool _whiteBalanceMetadataFound;
    private string _whiteBalanceLoadedPath = string.Empty;

    private void LoadAsShotWhiteBalance(string path)
    {
        _asShotTemperature = 6500;
        _asShotTint = 0;
        _whiteBalanceMetadataFound = false;
        _whiteBalanceLoadedPath = path ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(path))
            TryReadGenericMetadata(path);

        _loading = true;
        TemperatureSlider.Value = Math.Clamp(_asShotTemperature, (int)MinKelvin, (int)MaxKelvin);
        TintSlider.Value = 0;
        _loading = false;
        UpdateValueLabels();
    }

    private bool TryReadGenericMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            double? asShotKelvin = null;
            double? manualKelvin = null;
            double? tint = null;
            int asShotScore = -1;
            int manualScore = -1;
            int tintScore = -1;
            bool canonRaw = false;

            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string normalized = NormalizeTagName(name);
                string directoryName = directory.Name ?? string.Empty;

                if (directoryName.Contains("Canon", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("WB_RGGB", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("ColorTemp", StringComparison.OrdinalIgnoreCase))
                    canonRaw = true;

                // Canon ColorData11 (EOS R6 Mark II/R7/R10/R50) contains both
                // ColorTempAsShot and ColorTempKelvin. For manual Kelvin WB,
                // ColorTempKelvin is the actual camera setting and is the value
                // Lightroom-style RAW controls should start from. ColorTempAsShot
                // can represent a different Canon-derived informational value.
                int manualScoreCandidate = GetManualTemperatureTagScore(normalized, name);
                if (manualScoreCandidate >= 0)
                {
                    double? value = ExtractFirstNumber(description);
                    if (IsKelvin(value) && manualScoreCandidate > manualScore)
                    {
                        manualKelvin = value;
                        manualScore = manualScoreCandidate;
                    }
                }

                int asShotScoreCandidate = GetAsShotTemperatureTagScore(normalized, name);
                if (asShotScoreCandidate >= 0)
                {
                    double? value = ExtractFirstNumber(description);
                    if (IsKelvin(value) && asShotScoreCandidate > asShotScore)
                    {
                        asShotKelvin = value;
                        asShotScore = asShotScoreCandidate;
                    }
                }

                int tintScoreCandidate = GetTintTagScore(normalized, name);
                if (tintScoreCandidate >= 0)
                {
                    double? value = ExtractSignedNumber(description);
                    if (IsTint(value) && tintScoreCandidate > tintScore)
                    {
                        tint = value;
                        tintScore = tintScoreCandidate;
                    }
                }
            }

            // Prefer Canon's explicit Kelvin WB entry whenever present. This is
            // the important distinction for CR3 files shot using the camera's
            // Manual Temperature (Kelvin) WB mode.
            double? chosenTemperature = manualKelvin ?? asShotKelvin;
            if (chosenTemperature.HasValue)
            {
                _asShotTemperature = RoundTemperatureForDisplay((int)Math.Round(chosenTemperature.Value));
                _whiteBalanceMetadataFound = true;
            }

            if (tint.HasValue)
            {
                _asShotTint = (int)Math.Round(tint.Value);
                _whiteBalanceMetadataFound = true;
            }

            return chosenTemperature.HasValue;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeTagName(string name) =>
        name.Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("/", string.Empty);

    private static int GetManualTemperatureTagScore(string normalized, string originalName)
    {
        // Highest priority: Canon ColorData11's Kelvin entry.
        if (normalized.Contains("ColorTempKelvin", StringComparison.OrdinalIgnoreCase)) return 120;
        if (normalized.Contains("ColorTemperatureKelvin", StringComparison.OrdinalIgnoreCase)) return 120;
        if (normalized.Contains("ColorTempCustom", StringComparison.OrdinalIgnoreCase)) return 110;
        if (normalized.Contains("ColorTemperatureCustom", StringComparison.OrdinalIgnoreCase)) return 110;
        return -1;
    }

    private static int GetAsShotTemperatureTagScore(string normalized, string originalName)
    {
        if (normalized.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (normalized.Contains("ColorTempMeasured", StringComparison.OrdinalIgnoreCase)) return 70;
        if (normalized.Equals("ColorTemperature", StringComparison.OrdinalIgnoreCase)) return 60;
        if (originalName.Contains("Color Temperature", StringComparison.OrdinalIgnoreCase)) return 50;
        return -1;
    }

    private static int GetTintTagScore(string normalized, string originalName)
    {
        if (normalized.Contains("WBShiftGM", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("WBShiftGreenMagenta", StringComparison.OrdinalIgnoreCase)) return 100;
        if (normalized.Contains("GreenMagenta", StringComparison.OrdinalIgnoreCase)) return 90;
        if (originalName.Contains("Green/Magenta", StringComparison.OrdinalIgnoreCase)) return 80;
        return -1;
    }

    private static int RoundTemperatureForDisplay(int kelvin) =>
        (int)(Math.Round(kelvin / 100.0, MidpointRounding.AwayFromZero) * 100);

    private static bool IsKelvin(double? value) => value.HasValue && value.Value >= MinKelvin && value.Value <= MaxKelvin;
    private static bool IsTint(double? value) => value.HasValue && value.Value >= -150 && value.Value <= 150;

    private void UpdateWhiteBalanceLabels()
    {
        int currentTemperature = (int)Math.Round(TemperatureSlider.Value);
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
