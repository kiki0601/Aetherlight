using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Aetherlight;

public partial class MainWindow
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private bool _sliderPreviewPending;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ExposureSlider.ValueChanged += Slider_ValueChanged;
        ContrastSlider.ValueChanged += Slider_ValueChanged;
        HighlightsSlider.ValueChanged += Slider_ValueChanged;
        ShadowsSlider.ValueChanged += Slider_ValueChanged;
        WhitesSlider.ValueChanged += Slider_ValueChanged;
        BlacksSlider.ValueChanged += Slider_ValueChanged;
        TemperatureSlider.ValueChanged += Slider_ValueChanged;
        TintSlider.ValueChanged += Slider_ValueChanged;
        VibranceSlider.ValueChanged += Slider_ValueChanged;
        SaturationSlider.ValueChanged += Slider_ValueChanged;
        CropAngleSlider.ValueChanged += CropAngle_ValueChanged;
        MaskExposureSlider.ValueChanged += MaskExposure_ValueChanged;
        _previewTimer.Tick += PreviewTimer_Tick;
        UpdateValueLabels();
        DrawHistogram();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        _sliderPreviewPending = true;
        _previewTimer.Stop();
        ApplyFastPreview();
        _previewTimer.Start();
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        if (!_sliderPreviewPending || _originalPixels == null) return;
        _sliderPreviewPending = false;
        ApplyAdjustments();
    }

    private void ApplyFastPreview()
    {
        if (_originalPixels == null || _pixelWidth == 0 || _pixelHeight == 0) return;

        const int maxPreviewWidth = 1000;
        int width = Math.Min(maxPreviewWidth, _pixelWidth);
        int height = Math.Max(1, (int)Math.Round(_pixelHeight * (width / (double)_pixelWidth)));
        byte[] pixels = new byte[width * height * 4];
        double sx = _pixelWidth / (double)width, sy = _pixelHeight / (double)height;
        double exposure = Math.Pow(2, ExposureSlider.Value);
        double contrast = (259.0 * (ContrastSlider.Value + 255.0)) / (255.0 * (259.0 - ContrastSlider.Value));
        double saturation = 1 + SaturationSlider.Value / 100.0;
        double vibrance = VibranceSlider.Value / 100.0;
        double temperature = TemperatureSlider.Value / 100.0;
        double tint = TintSlider.Value / 100.0;
        double highlights = HighlightsSlider.Value / 100.0;
        double shadows = ShadowsSlider.Value / 100.0;
        double whites = WhitesSlider.Value / 100.0;
        double blacks = BlacksSlider.Value / 100.0;

        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(_pixelHeight - 1, (int)(y * sy));
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(_pixelWidth - 1, (int)(x * sx));
                int si = (sourceY * _pixelWidth + sourceX) * 4;
                int di = (y * width + x) * 4;
                double b = _originalPixels[si] / 255.0, g = _originalPixels[si + 1] / 255.0, r = _originalPixels[si + 2] / 255.0;
                r *= exposure; g *= exposure; b *= exposure;
                r = (r - .5) * contrast + .5; g = (g - .5) * contrast + .5; b = (b - .5) * contrast + .5;
                double luma = .2126 * r + .7152 * g + .0722 * b;
                double shadowMask = Math.Clamp(1 - luma * 2, 0, 1), highlightMask = Math.Clamp((luma - .5) * 2, 0, 1), whiteMask = Math.Clamp((luma - .7) / .3, 0, 1), blackMask = Math.Clamp((.3 - luma) / .3, 0, 1);
                double tonal = shadows * shadowMask * .35 + highlights * highlightMask * -.25 + whites * whiteMask * .25 + blacks * blackMask * -.25;
                r += tonal; g += tonal; b += tonal;
                r += temperature * .10 + tint * .03; b -= temperature * .10; g -= tint * .03;
                double gray = (r + g + b) / 3, vibFactor = 1 + vibrance * (1 - Math.Abs(gray - .5) * 2);
                r = gray + (r - gray) * saturation * vibFactor; g = gray + (g - gray) * saturation * vibFactor; b = gray + (b - gray) * saturation * vibFactor;
                pixels[di] = ToByte(b); pixels[di + 1] = ToByte(g); pixels[di + 2] = ToByte(r); pixels[di + 3] = 255;
            }
        }

        var preview = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        preview.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        preview.Freeze();
        Preview.Source = preview;
        DevelopPreview.Source = preview;
    }
}
