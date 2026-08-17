using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private bool _whiteBalanceSliderHandling;

    private static readonly bool _whiteBalanceHandlerRegistered = RegisterWhiteBalanceHandler();

    private static bool RegisterWhiteBalanceHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            RangeBase.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>(HandleAdjustmentSliderValueChanged));
        return true;
    }

    private static void HandleAdjustmentSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not MainWindow window || window._loading || window._whiteBalanceSliderHandling || window._originalPixels == null)
            return;

        if (e.OriginalSource is not Slider slider || !window.IsAdjustmentSlider(slider))
            return;

        e.Handled = true;
        window._whiteBalanceSliderHandling = true;
        try
        {
            double absoluteTemperature = window.TemperatureSlider.Value;
            double temperatureDelta = absoluteTemperature - window._asShotTemperature;

            window.TemperatureSlider.Value = temperatureDelta;
            window.ApplyAdjustments();
            window.TemperatureSlider.Value = absoluteTemperature;
            window.UpdateValueLabels();
        }
        finally
        {
            window._whiteBalanceSliderHandling = false;
        }
    }

    private bool IsAdjustmentSlider(Slider slider) =>
        ReferenceEquals(slider, ExposureSlider) ||
        ReferenceEquals(slider, ContrastSlider) ||
        ReferenceEquals(slider, HighlightsSlider) ||
        ReferenceEquals(slider, ShadowsSlider) ||
        ReferenceEquals(slider, WhitesSlider) ||
        ReferenceEquals(slider, BlacksSlider) ||
        ReferenceEquals(slider, TemperatureSlider) ||
        ReferenceEquals(slider, TintSlider) ||
        ReferenceEquals(slider, VibranceSlider) ||
        ReferenceEquals(slider, SaturationSlider);

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
        UpdateWhiteBalanceLabels();
    }

    private bool TryReadGenericMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            double? temperature = null;
            double? tint = null;
            int temperatureScore = -1;
            int tintScore = -1;

            foreach (var directory in directories)
            foreach (var tag in directory.Tags)
            {
                string name = tag.Name.Trim();
                string description = (tag.Description ?? string.Empty).Trim();
                string normalized = NormalizeTagName(name);

                int tempScore = GetTemperatureTagScore(normalized, name);
                if (tempScore >= 0)
                {
                    double? value = ExtractFirstNumber(description);
                    if (IsKelvin(value) && tempScore > temperatureScore)
                    {
                        temperature = value;
                        temperatureScore = tempScore;
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

            if (temperature.HasValue)
            {
                _asShotTemperature = RoundTemperatureForDisplay((int)Math.Round(temperature.Value));
                _whiteBalanceMetadataFound = true;
            }

            if (tint.HasValue)
            {
                _asShotTint = (int)Math.Round(tint.Value);
                _whiteBalanceMetadataFound = true;
            }

            return temperature.HasValue;
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

    private static int GetTemperatureTagScore(string normalized, string originalName)
    {
        if (normalized.Contains("ColorTempAsShot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("ColorTemperatureAsShot", StringComparison.OrdinalIgnoreCase)) return 100;
        if (normalized.Contains("ColorTempKelvin", StringComparison.OrdinalIgnoreCase)) return 90;
        if (normalized.Contains("ColorTemperatureKelvin", StringComparison.OrdinalIgnoreCase)) return 90;
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

    private static double SliderPositionToKelvin(double position)
    {
        double t = Math.Clamp(position, 0.0, 1.0);
        return MinKelvin * Math.Pow(MaxKelvin / MinKelvin, t);
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
