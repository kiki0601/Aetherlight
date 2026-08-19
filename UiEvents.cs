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
            _loading = true;
            TemperatureSlider.Value = _baseTemperatureKelvin;
            TintSlider.Value = 0;
            _loading = false;
            UpdateValueLabels();
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
        TemperatureValue.Text = $"{TemperatureSlider.Value:0} K";
        TintValue.Text = $"{_baseTint + TintSlider.Value:+0;-0;0}";
    }

    private void Adjustment_ValueChangedFast(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        UpdateWhiteBalanceReadouts();
        SchedulePreviewRender(false);
    }

    private async Task RenderScheduledAsync(int version, CancellationToken token, bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(35, token);
            token.ThrowIfCancellationRequested();

            int width = _pixelWidth, height = _pixelHeight;
            byte[]? source = _originalPixels;
            if (source == null || width <= 0 || height <= 0) return;

            double[] values = CaptureAdjustmentValues();
            double baseTemp = _baseTemperatureKelvin;
            double baseTint = _baseTint;
            var key = RenderCacheKey.Create(values, baseTemp, baseTint, width, height);

            if (_previewCache.TryGetValue(key, out BitmapSource? cached))
            {
                if (version != _renderVersion || token.IsCancellationRequested) return;
                DevelopPreview.Source = cached;
                Preview.Source = cached;
                return;
            }

            int previewWidth = Math.Min(900, width);
            byte[] previewPixels = Array.Empty<byte>();
            bool gpuRendered = await Task.Run(() => GpuPreviewRenderer.TryRender(source, width, height, values, baseTemp, baseTint, previewWidth, token, out previewPixels, out _, out _), token);

            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            if (!gpuRendered)
                previewPixels = await Task.Run(() => RenderPixels(source, width, height, values, baseTemp, baseTint, previewWidth, token), token);

            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            int previewHeight = Math.Max(1, (int)Math.Round(height * (previewWidth / (double)width)));
            BitmapSource preview = BitmapSource.Create(previewWidth, previewHeight, 96, 96, PixelFormats.Bgra32, null, previewPixels, previewWidth * 4);
            preview.Freeze();
            AddPreviewCache(key, preview);
            DevelopPreview.Source = preview;
            Preview.Source = preview;

            await Task.Delay(180, token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            byte[] fullPixels = await Task.Run(() => RenderPixels(source, width, height, values, baseTemp, baseTint, 0, token), token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            BitmapSource full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, fullPixels, width * 4);
            full.Freeze();
            DevelopPreview.Source = full;
            Preview.Source = full;
            DrawHistogram();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
