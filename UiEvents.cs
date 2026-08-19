using System.Collections.Concurrent;
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
    private CancellationTokenSource? _renderCancellation;
    private readonly ConcurrentDictionary<RenderCacheKey, BitmapSource> _previewCache = new();
    private readonly ConcurrentQueue<RenderCacheKey> _previewCacheOrder = new();
    private const int PreviewCacheLimit = 12;
    private double _baseTemperatureKelvin = 6500;
    private double _baseTint;
    private string? _loadedWhiteBalancePath;
    private bool _metadataLoading;
    private bool _uiEventsAttached;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_uiEventsAttached) return;
        _uiEventsAttached = true;

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
            slider.IsMoveToPointEnabled = true;
            slider.Margin = new Thickness(0, 0, 58, 0);
        }

        TextBlock[] values = { ExposureValue, ContrastValue, HighlightsValue, ShadowsValue, WhitesValue, BlacksValue, TemperatureValue, TintValue, VibranceValue, SaturationValue };
        foreach (TextBlock value in values)
        {
            value.HorizontalAlignment = HorizontalAlignment.Right;
            value.TextAlignment = TextAlignment.Right;
        }
    }

    private void ConfigureWhiteBalanceGradients()
    {
        TemperatureSlider.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(45, 135, 235), 0),
                new GradientStop(Color.FromRgb(155, 205, 245), .25),
                new GradientStop(Color.FromRgb(245, 245, 245), .5),
                new GradientStop(Color.FromRgb(255, 220, 110), .75),
                new GradientStop(Color.FromRgb(255, 145, 25), 1)
            }, new Point(0, .5), new Point(1, .5));

        TintSlider.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(50, 185, 80), 0),
                new GradientStop(Color.FromRgb(175, 225, 160), .28),
                new GradientStop(Color.FromRgb(245, 245, 245), .5),
                new GradientStop(Color.FromRgb(235, 170, 215), .72),
                new GradientStop(Color.FromRgb(205, 50, 170), 1)
            }, new Point(0, .5), new Point(1, .5));
    }

    private async void DevelopPreviewSourceChanged(object? sender, EventArgs e)
    {
        string? path = _currentPhotoPath;
        if (string.IsNullOrWhiteSpace(path) || !IsRawFile(path) || path == _loadedWhiteBalancePath || _metadataLoading) return;

        _metadataLoading = true;
        try
        {
            RawWhiteBalance result = await Task.Run(() => RawWhiteBalanceReader.Read(path));
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
            ClearPreviewCache();
            ScheduleRender(true);
        }
        finally
        {
            _metadataLoading = false;
        }
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
        ScheduleRender(false);
    }

    private void ScheduleRender(bool immediate)
    {
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        var cts = new CancellationTokenSource();
        _renderCancellation = cts;
        int version = Interlocked.Increment(ref _renderVersion);

        _ = RenderScheduledAsync(version, cts.Token, immediate);
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

            // Interactive render deliberately targets a small image. This keeps
            // slider movement responsive even on large CR3/ARW/RAF files.
            byte[] previewPixels = await Task.Run(() => RenderPixels(source, width, height, values, baseTemp, baseTint, 900, token), token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            BitmapSource preview = CreateBitmap(previewPixels, Math.Min(900, width), height, width);
            preview.Freeze();
            AddPreviewCache(key, preview);
            DevelopPreview.Source = preview;
            Preview.Source = preview;

            // Full resolution is intentionally delayed. If the user moves the
            // slider again, this work is cancelled before it can hit the UI.
            await Task.Delay(180, token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            byte[] fullPixels = await Task.Run(() => RenderPixels(source, width, height, values, baseTemp, baseTint, 0, token), token);
            token.ThrowIfCancellationRequested();
            if (version != _renderVersion) return;

            BitmapSource full = CreateBitmap(fullPixels, width, height, width);
            full.Freeze();
            DevelopPreview.Source = full;
            Preview.Source = full;
            DrawHistogram();
        }
        catch (OperationCanceledException)
        {
            // Expected when the user moves a slider again.
        }
        catch
        {
            // A preview failure must never take down the editor.
        }
    }

    private double[] CaptureAdjustmentValues() => new[]
    {
        ExposureSlider.Value, ContrastSlider.Value, HighlightsSlider.Value,
        ShadowsSlider.Value, WhitesSlider.Value, BlacksSlider.Value,
        TemperatureSlider.Value, TintSlider.Value, VibranceSlider.Value,
        SaturationSlider.Value
    };

    private static BitmapSource CreateBitmap(byte[] pixels, int width, int sourceHeight, int sourceWidth)
    {
        int height = Math.Max(1, (int)Math.Round(sourceHeight * (width / (double)sourceWidth)));
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private void AddPreviewCache(RenderCacheKey key, BitmapSource image)
    {
        _previewCache[key] = image;
        _previewCacheOrder.Enqueue(key);
        while (_previewCache.Count > PreviewCacheLimit && _previewCacheOrder.TryDequeue(out RenderCacheKey old))
            _previewCache.TryRemove(old, out _);
    }

    private void ClearPreviewCache()
    {
        _previewCache.Clear();
        while (_previewCacheOrder.TryDequeue(out _)) { }
    }

    private byte[] RenderPixels(byte[] source, int width, int height, double[] v, double baseTemp, double baseTint, int maxWidth, CancellationToken token)
    {
        int outWidth = maxWidth > 0 ? Math.Min(maxWidth, width) : width;
        int outHeight = Math.Max(1, (int)Math.Round(height * (outWidth / (double)width)));
        byte[] pixels = new byte[checked(outWidth * outHeight * 4)];
        double exposure = Math.Pow(2, v[0]);
        double contrast = (259.0 * (v[1] + 255.0)) / (255.0 * (259.0 - v[1]));
        double saturation = 1 + v[9] / 100.0;
        double vibrance = v[8] / 100.0;
        double temperature = (v[6] - baseTemp) / 100.0;
        double tint = (v[7] + baseTint) / 100.0;
        double highlights = v[2] / 100.0;
        double shadows = v[3] / 100.0;
        double whites = v[4] / 100.0;
        double blacks = v[5] / 100.0;
        double scaleX = width / (double)outWidth;
        double scaleY = height / (double)outHeight;

        Parallel.For(0, outHeight, new ParallelOptions { CancellationToken = token }, y =>
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            int sy = Math.Min(height - 1, (int)(y * scaleY));
            for (int x = 0; x < outWidth; x++)
            {
                int sx = Math.Min(width - 1, (int)(x * scaleX));
                int si = (sy * width + sx) * 4;
                int i = (y * outWidth + x) * 4;

                double b = source[si] / 255.0;
                double g = source[si + 1] / 255.0;
                double r = source[si + 2] / 255.0;
                r *= exposure; g *= exposure; b *= exposure;
                r = (r - .5) * contrast + .5;
                g = (g - .5) * contrast + .5;
                b = (b - .5) * contrast + .5;

                double luma = .2126 * r + .7152 * g + .0722 * b;
                double shadowMask = Math.Clamp(1 - luma * 2, 0, 1);
                double highlightMask = Math.Clamp((luma - .5) * 2, 0, 1);
                double whiteMask = Math.Clamp((luma - .7) / .3, 0, 1);
                double blackMask = Math.Clamp((.3 - luma) / .3, 0, 1);
                double tonal = shadows * shadowMask * .35 + highlights * highlightMask * -.25 + whites * whiteMask * .25 + blacks * blackMask * -.25;
                r += tonal; g += tonal; b += tonal;

                // Kelvin is absolute in the UI. The preview applies only the
                // delta from the camera's as-shot temperature.
                r += temperature * .10;
                b -= temperature * .10;
                r += tint * .03;
                g -= tint * .03;

                double gray = (r + g + b) / 3.0;
                double vibFactor = 1 + vibrance * (1 - Math.Abs(gray - .5) * 2);
                r = gray + (r - gray) * saturation * vibFactor;
                g = gray + (g - gray) * saturation * vibFactor;
                b = gray + (b - gray) * saturation * vibFactor;

                pixels[i] = ToByte(b);
                pixels[i + 1] = ToByte(g);
                pixels[i + 2] = ToByte(r);
                pixels[i + 3] = 255;
            }
        });
        return pixels;
    }

    private readonly record struct RenderCacheKey(
        int Exposure, int Contrast, int Highlights, int Shadows, int Whites, int Blacks,
        int Temperature, int Tint, int Vibrance, int Saturation, int BaseTemperature, int BaseTint,
        int Width, int Height)
    {
        public static RenderCacheKey Create(double[] v, double baseTemp, double baseTint, int width, int height) => new(
            (int)Math.Round(v[0] * 100), (int)Math.Round(v[1]), (int)Math.Round(v[2]), (int)Math.Round(v[3]),
            (int)Math.Round(v[4]), (int)Math.Round(v[5]), (int)Math.Round(v[6]), (int)Math.Round(v[7]),
            (int)Math.Round(v[8]), (int)Math.Round(v[9]), (int)Math.Round(baseTemp), (int)Math.Round(baseTint), width, height);
    }
}
