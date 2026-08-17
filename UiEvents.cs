using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aetherlight;

public partial class MainWindow
{
    private int _renderVersion;
    private double _baseTemperatureKelvin = 6500;
    private double _baseTint = 0;
    private string? _loadedWhiteBalancePath;
    private bool _metadataLoading;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ExposureSlider.ValueChanged += Adjustment_ValueChangedFast;
        ContrastSlider.ValueChanged += Adjustment_ValueChangedFast;
        HighlightsSlider.ValueChanged += Adjustment_ValueChangedFast;
        ShadowsSlider.ValueChanged += Adjustment_ValueChangedFast;
        WhitesSlider.ValueChanged += Adjustment_ValueChangedFast;
        BlacksSlider.ValueChanged += Adjustment_ValueChangedFast;
        TemperatureSlider.ValueChanged += Adjustment_ValueChangedFast;
        TintSlider.ValueChanged += Adjustment_ValueChangedFast;
        VibranceSlider.ValueChanged += Adjustment_ValueChangedFast;
        SaturationSlider.ValueChanged += Adjustment_ValueChangedFast;
        CropAngleSlider.ValueChanged += CropAngle_ValueChanged;
        MaskExposureSlider.ValueChanged += MaskExposure_ValueChanged;

        ConfigureSliderReadouts();
        ConfigureWhiteBalanceGradients();
        UpdateWhiteBalanceReadouts();
        DrawHistogram();

        var descriptor = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
        descriptor?.AddValueChanged(DevelopPreview, DevelopPreviewSourceChanged);
    }

    private void ConfigureSliderReadouts()
    {
        Slider[] sliders = { ExposureSlider, ContrastSlider, HighlightsSlider, ShadowsSlider, WhitesSlider, BlacksSlider, TemperatureSlider, TintSlider, VibranceSlider, SaturationSlider };
        foreach (Slider slider in sliders)
        {
            slider.Margin = new Thickness(0, 0, 58, 0);
            slider.IsMoveToPointEnabled = true;
        }
        TextBlock[] values = { ExposureValue, ContrastValue, HighlightsValue, ShadowsValue, WhitesValue, BlacksValue, TemperatureValue, TintValue, VibranceValue, SaturationValue };
        foreach (TextBlock value in values)
        {
            value.Width = 54;
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.TextAlignment = TextAlignment.Right;
        }
    }

    private void ConfigureWhiteBalanceGradients()
    {
        TemperatureSlider.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(50, 140, 235), 0),
                new GradientStop(Color.FromRgb(170, 210, 245), .28),
                new GradientStop(Color.FromRgb(245, 245, 245), .5),
                new GradientStop(Color.FromRgb(255, 225, 125), .72),
                new GradientStop(Color.FromRgb(255, 150, 30), 1)
            }, new Point(0, .5), new Point(1, .5));
        TintSlider.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(55, 185, 85), 0),
                new GradientStop(Color.FromRgb(180, 225, 165), .3),
                new GradientStop(Color.FromRgb(245, 245, 245), .5),
                new GradientStop(Color.FromRgb(235, 170, 215), .7),
                new GradientStop(Color.FromRgb(205, 55, 170), 1)
            }, new Point(0, .5), new Point(1, .5));
    }

    private async void DevelopPreviewSourceChanged(object? sender, EventArgs e)
    {
        string? path = _currentPhotoPath;
        if (string.IsNullOrWhiteSpace(path) || !IsRawFile(path) || path == _loadedWhiteBalancePath || _metadataLoading) return;
        _metadataLoading = true;
        try
        {
            var result = await Task.Run(() => RawWhiteBalanceReader.Read(path));
            if (path != _currentPhotoPath) return;
            _baseTemperatureKelvin = result.Kelvin;
            _baseTint = result.Tint;
            _loadedWhiteBalancePath = path;
            UpdateWhiteBalanceReadouts();
            StatusText.Text = $"Aetherlight • As-shot WB {_baseTemperatureKelvin:0} K • Tint {_baseTint:+0;-0;0}";
        }
        finally { _metadataLoading = false; }
    }

    private static bool IsRawFile(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cr3" or ".arw" or ".raf" or ".dng" or ".nef" or ".nrw" or ".orf" or ".rw2" or ".pef" or ".srw";
    }

    private void UpdateWhiteBalanceReadouts()
    {
        TemperatureValue.Text = $"{_baseTemperatureKelvin + TemperatureSlider.Value:0} K";
        TintValue.Text = $"{_baseTint + TintSlider.Value:+0;-0;0}";
    }

    private void Adjustment_ValueChangedFast(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        UpdateWhiteBalanceReadouts();
        _ = RenderPreviewAsync();
    }

    private async Task RenderPreviewAsync()
    {
        int version = Interlocked.Increment(ref _renderVersion);
        int width = _pixelWidth, height = _pixelHeight;
        if (width <= 0 || height <= 0 || _originalPixels == null) return;

        byte[] source = _originalPixels;
        double[] values = { ExposureSlider.Value, ContrastSlider.Value, HighlightsSlider.Value, ShadowsSlider.Value, WhitesSlider.Value, BlacksSlider.Value, TemperatureSlider.Value, TintSlider.Value, VibranceSlider.Value, SaturationSlider.Value };
        double baseTemp = _baseTemperatureKelvin, baseTint = _baseTint;

        byte[] previewPixels = await Task.Run(() => RenderPixels(source, width, height, values, baseTemp, baseTint, 700));
        if (version != _renderVersion) return;
        var preview = BitmapSource.Create(width > 700 ? 700 : width, Math.Max(1, (int)Math.Round(height * (Math.Min(700, width) / (double)width))), 96, 96, PixelFormats.Bgra32, null, previewPixels, Math.Min(700, width) * 4);
        preview.Freeze();
        DevelopPreview.Source = preview;
        Preview.Source = preview;

        await Task.Delay(160);
        if (version != _renderVersion || _originalPixels == null) return;
        byte[] fullPixels = await Task.Run(() => RenderPixels(_originalPixels, width, height, values, baseTemp, baseTint, 0));
        if (version != _renderVersion) return;
        var full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, fullPixels, width * 4);
        full.Freeze();
        DevelopPreview.Source = full;
        Preview.Source = full;
        DrawHistogram();
    }

    private byte[] RenderPixels(byte[] source, int width, int height, double[] v, double baseTemp, double baseTint, int maxWidth)
    {
        int outWidth = maxWidth > 0 ? Math.Min(maxWidth, width) : width;
        int outHeight = Math.Max(1, (int)Math.Round(height * (outWidth / (double)width)));
        byte[] pixels = new byte[outWidth * outHeight * 4];
        double exposure = Math.Pow(2, v[0]);
        double contrast = (259.0 * (v[1] + 255.0)) / (255.0 * (259.0 - v[1]));
        double saturation = 1 + v[9] / 100.0, vibrance = v[8] / 100.0;
        double temperature = v[6] / 100.0, tint = v[7] / 100.0;
        double highlights = v[2] / 100.0, shadows = v[3] / 100.0, whites = v[4] / 100.0, blacks = v[5] / 100.0;
        double scaleX = width / (double)outWidth, scaleY = height / (double)outHeight;

        Parallel.For(0, outHeight, y =>
        {
            int sy = Math.Min(height - 1, (int)(y * scaleY));
            for (int x = 0; x < outWidth; x++)
            {
                int sx = Math.Min(width - 1, (int)(x * scaleX));
                int si = (sy * width + sx) * 4, i = (y * outWidth + x) * 4;
                double b = source[si] / 255.0, g = source[si + 1] / 255.0, r = source[si + 2] / 255.0;
                r *= exposure; g *= exposure; b *= exposure;
                r = (r - .5) * contrast + .5; g = (g - .5) * contrast + .5; b = (b - .5) * contrast + .5;
                double luma = .2126 * r + .7152 * g + .0722 * b;
                double shadowMask = Math.Clamp(1 - luma * 2, 0, 1), highlightMask = Math.Clamp((luma - .5) * 2, 0, 1), whiteMask = Math.Clamp((luma - .7) / .3, 0, 1), blackMask = Math.Clamp((.3 - luma) / .3, 0, 1);
                double tonal = shadows * shadowMask * .35 + highlights * highlightMask * -.25 + whites * whiteMask * .25 + blacks * blackMask * -.25;
                r += tonal; g += tonal; b += tonal;
                r += temperature * .10 + tint * .03; b -= temperature * .10; g -= tint * .03;
                double gray = (r + g + b) / 3, vibFactor = 1 + vibrance * (1 - Math.Abs(gray - .5) * 2);
                r = gray + (r - gray) * saturation * vibFactor; g = gray + (g - gray) * saturation * vibFactor; b = gray + (b - gray) * saturation * vibFactor;
                pixels[i] = ToByte(b); pixels[i + 1] = ToByte(g); pixels[i + 2] = ToByte(r); pixels[i + 3] = 255;
            }
        });
        return pixels;
    }
}
