using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Aetherlight;

public partial class MainWindow
{
    private readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(45) };
    private CancellationTokenSource? _previewCts;
    private int _previewGeneration;
    private bool _fastPipelineInstalled;
    private bool _advancedDragging;
    private bool _colorWheelDragging;
    private string _curveChannel = "L";
    private readonly Dictionary<string, List<Point>> _curves = new()
    {
        ["L"] = new() { new Point(0, 1), new Point(1, 0) },
        ["R"] = new() { new Point(0, 1), new Point(1, 0) },
        ["G"] = new() { new Point(0, 1), new Point(1, 0) },
        ["B"] = new() { new Point(0, 1), new Point(1, 0) }
    };
    private double _sharpening, _noiseReduction, _clarity, _texture, _dehaze, _vignette, _grain, _glow, _halation;
    private double _gradeHue, _gradeSaturation, _gradeLuma, _gradeBlend, _gradeBalance;
    private string _gradeRange = "M";
    private bool _brushMaskEnabled, _linearMaskEnabled, _radialMaskEnabled, _autoSkyMaskEnabled, _autoSubjectMaskEnabled;
    private Point _linearMaskStart, _linearMaskEnd, _radialMaskCenter;
    private double _radialMaskRadius = 0.35;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InstallFastAdjustmentPipeline();
        DrawCurve();
        DrawColorWheel();
    }

    private void InstallFastAdjustmentPipeline()
    {
        if (_fastPipelineInstalled) return;
        _fastPipelineInstalled = true;
        _previewDebounce.Tick += PreviewDebounce_Tick;

        Slider[] sliders = { ExposureSlider, ContrastSlider, HighlightsSlider, ShadowsSlider, WhitesSlider, BlacksSlider, TemperatureSlider, TintSlider, VibranceSlider, SaturationSlider };
        foreach (var slider in sliders)
        {
            slider.ValueChanged -= Adjustment_ValueChanged;
            slider.ValueChanged += FastAdjustment_ValueChanged;
            slider.PreviewMouseDown += Adjustment_MouseDown;
            slider.PreviewMouseUp += Adjustment_MouseUp;
        }

        ExportButton.Click -= Export_Click;
        ExportButton.Click += ExportAdvanced_Click;

        SharpeningSlider.ValueChanged += AdvancedSlider_ValueChanged;
        NoiseReductionSlider.ValueChanged += AdvancedSlider_ValueChanged;
        ClaritySlider.ValueChanged += AdvancedSlider_ValueChanged;
        TextureSlider.ValueChanged += AdvancedSlider_ValueChanged;
        DehazeSlider.ValueChanged += AdvancedSlider_ValueChanged;
        VignetteSlider.ValueChanged += AdvancedSlider_ValueChanged;
        GrainSlider.ValueChanged += AdvancedSlider_ValueChanged;
        GlowSlider.ValueChanged += AdvancedSlider_ValueChanged;
        HalationSlider.ValueChanged += AdvancedSlider_ValueChanged;
        GradeBlendSlider.ValueChanged += AdvancedSlider_ValueChanged;
        GradeBalanceSlider.ValueChanged += AdvancedSlider_ValueChanged;
        MaskSizeSlider.ValueChanged += AdvancedMask_ValueChanged;
        MaskExposureSlider.ValueChanged += AdvancedMask_ValueChanged;
    }

    private void FastAdjustment_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        SchedulePreview();
    }

    private void Adjustment_MouseDown(object sender, MouseButtonEventArgs e) => _advancedDragging = true;

    private void Adjustment_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _advancedDragging = false;
        SchedulePreview(true);
    }

    private void AdvancedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        _sharpening = SharpeningSlider.Value;
        _noiseReduction = NoiseReductionSlider.Value;
        _clarity = ClaritySlider.Value;
        _texture = TextureSlider.Value;
        _dehaze = DehazeSlider.Value;
        _vignette = VignetteSlider.Value;
        _grain = GrainSlider.Value;
        _glow = GlowSlider.Value;
        _halation = HalationSlider.Value;
        _gradeBlend = GradeBlendSlider.Value;
        _gradeBalance = GradeBalanceSlider.Value;
        SchedulePreview();
    }

    private void AdvancedMask_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        DrawMaskOverlay();
        SchedulePreview();
    }

    private void SchedulePreview(bool immediate = false)
    {
        _previewDebounce.Stop();
        if (immediate)
        {
            _previewDebounce_Tick(null, EventArgs.Empty);
            return;
        }
        _previewDebounce.Start();
    }

    private void PreviewDebounce_Tick(object? sender, EventArgs e) => _previewDebounce_Tick(sender, e);

    private void _previewDebounce_Tick(object? sender, EventArgs e)
    {
        _previewDebounce.Stop();
        if (_originalPixels == null || _pixelWidth == 0) return;

        int generation = ++_previewGeneration;
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        CancellationToken token = _previewCts.Token;

        byte[] source = _originalPixels;
        int sw = _pixelWidth, sh = _pixelHeight;
        double[] values =
        {
            ExposureSlider.Value, ContrastSlider.Value, HighlightsSlider.Value, ShadowsSlider.Value,
            WhitesSlider.Value, BlacksSlider.Value, TemperatureSlider.Value, TintSlider.Value,
            VibranceSlider.Value, SaturationSlider.Value
        };
        double baseTemp = _asShotTemperature;
        double baseTint = _asShotTint;
        AdvancedSnapshot advanced = CaptureAdvancedSnapshot();

        _ = Task.Run(() => RenderPreview(source, sw, sh, values, baseTemp, baseTint, advanced, token), token)
            .ContinueWith(t =>
            {
                if (t.IsCanceled || t.IsFaulted || t.Result == null || token.IsCancellationRequested || generation != _previewGeneration) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || generation != _previewGeneration) return;
                    var result = t.Result.Value;
                    var bmp = BitmapSource.Create(result.Width, result.Height, 96, 96, PixelFormats.Bgra32, null, result.Pixels, result.Width * 4);
                    bmp.Freeze();
                    DevelopPreview.Source = bmp;
                }), DispatcherPriority.Render);
            }, TaskScheduler.Default);
    }

    private AdvancedSnapshot CaptureAdvancedSnapshot()
    {
        return new AdvancedSnapshot(
            _sharpening, _noiseReduction, _clarity, _texture, _dehaze, _vignette, _grain, _glow, _halation,
            _gradeHue, _gradeSaturation, _gradeLuma, _gradeBlend, _gradeBalance, _gradeRange,
            _curves.ToDictionary(k => k.Key, v => v.Value.ToArray()),
            _maskPoints.ToArray(), _brushMaskEnabled, _linearMaskEnabled, _radialMaskEnabled,
            _autoSkyMaskEnabled, _autoSubjectMaskEnabled, _linearMaskStart, _linearMaskEnd, _radialMaskCenter, _radialMaskRadius,
            MaskSizeSlider.Value, MaskExposureSlider.Value);
    }

    private static PreviewResult RenderPreview(byte[] source, int sw, int sh, double[] v, double baseTemp, double baseTint, AdvancedSnapshot a, CancellationToken token)
    {
        const int maxWidth = 1280;
        int w = Math.Min(maxWidth, sw);
        int h = Math.Max(1, (int)Math.Round(sh * (w / (double)sw)));
        byte[] output = new byte[checked(w * h * 4)];
        double sx = sw / (double)w, sy = sh / (double)h;
        double exposure = Math.Pow(2, v[0]);
        double contrast = (259.0 * (v[1] + 255.0)) / (255.0 * (259.0 - v[1]));
        double saturation = 1 + v[9] / 100.0;
        double vibrance = v[8] / 100.0;
        double temp = Math.Log(Math.Max(1, v[6]) / Math.Max(1, baseTemp), 2) * 0.10;
        double tint = v[7] / 100.0;

        Parallel.For(0, h, new ParallelOptions { CancellationToken = token }, y =>
        {
            if ((y & 15) == 0) token.ThrowIfCancellationRequested();
            for (int x = 0; x < w; x++)
            {
                int ox = Math.Min(sw - 1, (int)(x * sx));
                int oy = Math.Min(sh - 1, (int)(y * sy));
                int si = (oy * sw + ox) * 4;
                double b = source[si] / 255.0, g = source[si + 1] / 255.0, r = source[si + 2] / 255.0;
                ApplyCore(ref r, ref g, ref b, exposure, contrast, temp, tint, saturation, vibrance, v[2] / 100.0, v[3] / 100.0, v[4] / 100.0, v[5] / 100.0);
                ApplyCurve(ref r, ref g, ref b, a.Curves);
                ApplyColorGrade(ref r, ref g, ref b, a.GradeHue, a.GradeSaturation, a.GradeLuma, a.GradeBlend, a.GradeBalance, a.GradeRange);
                ApplyDetailAndEffects(ref r, ref g, ref b, source, sw, sh, ox, oy, a, sx, sy);
                ApplyMask(ref r, ref g, ref b, x, y, w, h, a);
                int di = (y * w + x) * 4;
                output[di] = ToByteFast(b); output[di + 1] = ToByteFast(g); output[di + 2] = ToByteFast(r); output[di + 3] = 255;
            }
        });
        return new PreviewResult(output, w, h);
    }

    private static void ApplyCore(ref double r, ref double g, ref double b, double exposure, double contrast, double temp, double tint, double saturation, double vibrance, double highlights, double shadows, double whites, double blacks)
    {
        r *= exposure; g *= exposure; b *= exposure;
        r = (r - .5) * contrast + .5; g = (g - .5) * contrast + .5; b = (b - .5) * contrast + .5;
        double l = .2126 * r + .7152 * g + .0722 * b;
        double sm = Math.Clamp(1 - l * 2, 0, 1), hm = Math.Clamp((l - .5) * 2, 0, 1), wm = Math.Clamp((l - .7) / .3, 0, 1), bm = Math.Clamp((.3 - l) / .3, 0, 1);
        double tonal = shadows * sm * .35 + highlights * hm * .25 + whites * wm * .25 + blacks * bm * -.25;
        r += tonal; g += tonal; b += tonal;
        r += temp; b -= temp; r += tint * .03; g -= tint * .03;
        double gray = (r + g + b) / 3;
        double vf = 1 + vibrance * (1 - Math.Abs(gray - .5) * 2);
        r = gray + (r - gray) * saturation * vf; g = gray + (g - gray) * saturation * vf; b = gray + (b - gray) * saturation * vf;
    }

    private static void ApplyCurve(ref double r, ref double g, ref double b, Dictionary<string, Point[]> curves)
    {
        r = SampleCurve(curves["R"], r); g = SampleCurve(curves["G"], g); b = SampleCurve(curves["B"], b);
        double l = .2126 * r + .7152 * g + .0722 * b;
        double mapped = SampleCurve(curves["L"], l);
        double scale = l < .0001 ? 1 : mapped / l;
        r *= scale; g *= scale; b *= scale;
    }

    private static double SampleCurve(Point[] points, double x)
    {
        x = Math.Clamp(x, 0, 1);
        for (int i = 1; i < points.Length; i++)
        {
            if (x <= points[i].X)
            {
                Point a = points[i - 1], b = points[i];
                double t = (x - a.X) / Math.Max(.0001, b.X - a.X);
                return Math.Clamp(a.Y + (b.Y - a.Y) * t, 0, 1);
            }
        }
        return points[^1].Y;
    }

    private static void ApplyColorGrade(ref double r, ref double g, ref double b, double hue, double sat, double luma, double blend, double balance, string range)
    {
        if (sat <= .0001 && Math.Abs(luma) <= .0001) return;
        double y = .2126 * r + .7152 * g + .0722 * b;
        double weight = range switch { "S" => Math.Clamp((.5 - y) * 2 + .25, 0, 1), "H" => Math.Clamp((y - .5) * 2 + .25, 0, 1), _ => 1 - Math.Abs(y - .5) * 1.6 };
        weight *= Math.Clamp(blend, 0, 1);
        double[] rgb = HsvToRgb(hue, sat * weight, luma * .12 * weight);
        r += rgb[0]; g += rgb[1]; b += rgb[2];
    }

    private static double[] HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360; double c = Math.Abs(v) * s, x = c * (1 - Math.Abs((h / 60 % 2) - 1)), m = v >= 0 ? 0 : v;
        double rr = 0, gg = 0, bb = 0;
        if (h < 60) { rr = c; gg = x; } else if (h < 120) { rr = x; gg = c; } else if (h < 180) { gg = c; bb = x; } else if (h < 240) { gg = x; bb = c; } else if (h < 300) { rr = x; bb = c; } else { rr = c; bb = x; }
        return new[] { rr + m, gg + m, bb + m };
    }

    private static void ApplyDetailAndEffects(ref double r, ref double g, ref double b, byte[] source, int sw, int sh, int ox, int oy, AdvancedSnapshot a, double sx, double sy)
    {
        double l = .2126 * r + .7152 * g + .0722 * b;
        if (a.NoiseReduction > 0 || a.Clarity != 0 || a.Sharpening != 0)
        {
            int nx = Math.Min(sw - 1, ox + Math.Max(1, (int)sx));
            int px = Math.Max(0, ox - Math.Max(1, (int)sx));
            int ny = Math.Min(sh - 1, oy + Math.Max(1, (int)sy));
            int py = Math.Max(0, oy - Math.Max(1, (int)sy));
            double avg = 0;
            foreach (var yy in new[] { py, oy, ny }) foreach (var xx in new[] { px, ox, nx })
            {
                int i = (yy * sw + xx) * 4;
                avg += (.2126 * source[i + 2] + .7152 * source[i + 1] + .0722 * source[i]) / 255.0;
            }
            avg /= 9;
            double detail = l - avg;
            double nr = Math.Clamp(a.NoiseReduction / 100.0, 0, 1);
            double local = a.Clarity / 100.0 + a.Texture / 150.0;
            double factor = 1 + local;
            r = (r - avg) * factor + avg; g = (g - avg) * factor + avg; b = (b - avg) * factor + avg;
            r -= detail * nr; g -= detail * nr; b -= detail * nr;
            double sharp = 1 + a.Sharpening / 80.0;
            r = avg + (r - avg) * sharp; g = avg + (g - avg) * sharp; b = avg + (b - avg) * sharp;
        }
        if (a.DeHaze != 0)
        {
            double d = a.DeHaze / 100.0;
            r = (r - .5) * (1 + d * .55) + .5; g = (g - .5) * (1 + d * .55) + .5; b = (b - .5) * (1 + d * .55) + .5;
            double gray = (r + g + b) / 3; r += (r - gray) * d * .18; g += (g - gray) * d * .18; b += (b - gray) * d * .18;
        }
        if (a.Vignette != 0)
        {
            double cx = (ox / (double)Math.Max(1, sw - 1)) - .5, cy = (oy / (double)Math.Max(1, sh - 1)) - .5;
            double v = Math.Clamp(1 - Math.Sqrt(cx * cx + cy * cy) * 1.55, 0, 1);
            double amount = a.Vignette / 100.0;
            double f = 1 + amount * (v - 1) * .8;
            r *= f; g *= f; b *= f;
        }
        if (a.Glow > 0)
        {
            double glow = Math.Max(0, l - .62) * a.Glow / 100.0;
            r += glow; g += glow; b += glow;
        }
        if (a.Halation > 0)
        {
            double h = Math.Max(0, l - .72) * a.Halation / 100.0;
            r += h * .18; g -= h * .025; b -= h * .02;
        }
        if (a.Grain > 0)
        {
            uint n = (uint)(ox * 374761393 + oy * 668265263); n = (n ^ (n >> 13)) * 1274126177u; double noise = (((n ^ (n >> 16)) & 1023) / 1023.0 - .5) * a.Grain / 100.0 * .12;
            r += noise; g += noise; b += noise;
        }
    }

    private static void ApplyMask(ref double r, ref double g, ref double b, int x, int y, int w, int h, AdvancedSnapshot a)
    {
        double strength = 0;
        if (a.BrushMask && a.MaskPoints.Length > 0)
        {
            double radius = Math.Max(1, a.MaskSize);
            foreach (var p in a.MaskPoints)
            {
                double px = p.X / Math.Max(1, a.SourceWidth - 1) * (w - 1), py = p.Y / Math.Max(1, a.SourceHeight - 1) * (h - 1);
                double d = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                if (d < radius) strength = Math.Max(strength, 1 - d / radius);
            }
        }
        if (a.LinearMask)
        {
            double ax = a.LinearStart.X, ay = a.LinearStart.Y, bx = a.LinearEnd.X, by = a.LinearEnd.Y;
            double dx = bx - ax, dy = by - ay, len = Math.Max(.0001, Math.Sqrt(dx * dx + dy * dy));
            double t = ((x - ax) * dx + (y - ay) * dy) / (len * len);
            strength = Math.Max(strength, Math.Clamp(t, 0, 1));
        }
        if (a.RadialMask)
        {
            double d = Math.Sqrt((x / (double)w - a.RadialCenter.X) * (x / (double)w - a.RadialCenter.X) + (y / (double)h - a.RadialCenter.Y) * (y / (double)h - a.RadialCenter.Y));
            strength = Math.Max(strength, Math.Clamp(1 - d / Math.Max(.001, a.RadialRadius), 0, 1));
        }
        if (a.AutoSky)
        {
            double top = 1 - y / (double)Math.Max(1, h - 1);
            strength = Math.Max(strength, top * .75);
        }
        if (a.AutoSubject)
        {
            double cx = x / (double)Math.Max(1, w - 1) - .5, cy = y / (double)Math.Max(1, h - 1) - .5;
            strength = Math.Max(strength, Math.Clamp(1 - Math.Sqrt(cx * cx + cy * cy) * 2.1, 0, 1) * .7);
        }
        if (strength <= .001) return;
        double factor = Math.Pow(2, a.MaskExposure * strength);
        r *= factor; g *= factor; b *= factor;
    }

    private static byte ToByteFast(double v) => (byte)(Math.Clamp(v, 0, 1) * 255 + .5);

    private void CurveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _advancedDragging = true;
        CurveCanvas.CaptureMouse();
        AddOrMoveCurvePoint(e.GetPosition(CurveCanvas));
    }

    private void CurveCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_advancedDragging) return;
        AddOrMoveCurvePoint(e.GetPosition(CurveCanvas));
    }

    private void CurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _advancedDragging = false;
        CurveCanvas.ReleaseMouseCapture();
        SchedulePreview(true);
    }

    private void AddOrMoveCurvePoint(Point p)
    {
        double w = Math.Max(1, CurveCanvas.ActualWidth), h = Math.Max(1, CurveCanvas.ActualHeight);
        double x = Math.Clamp(p.X / w, 0, 1), y = Math.Clamp(1 - p.Y / h, 0, 1);
        var points = _curves[_curveChannel];
        int nearest = -1; double best = .035;
        for (int i = 1; i < points.Count - 1; i++)
        {
            double d = Math.Sqrt(Math.Pow(points[i].X - x, 2) + Math.Pow(points[i].Y - y, 2));
            if (d < best) { best = d; nearest = i; }
        }
        if (nearest >= 0) points[nearest] = new Point(x, y); else points.Add(new Point(x, y));
        points.Sort((a, b) => a.X.CompareTo(b.X));
        DrawCurve();
        SchedulePreview();
    }

    private void DrawCurve()
    {
        if (CurveCanvas == null) return;
        CurveCanvas.Children.Clear();
        double w = Math.Max(1, CurveCanvas.ActualWidth), h = Math.Max(1, CurveCanvas.ActualHeight);
        var gridPen = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        for (int i = 1; i < 4; i++)
        {
            CurveCanvas.Children.Add(new Line { X1 = i * w / 4, X2 = i * w / 4, Y1 = 0, Y2 = h, Stroke = gridPen, StrokeThickness = 1 });
            CurveCanvas.Children.Add(new Line { X1 = 0, X2 = w, Y1 = i * h / 4, Y2 = i * h / 4, Stroke = gridPen, StrokeThickness = 1 });
        }
        CurveCanvas.Children.Add(new Line { X1 = 0, Y1 = h, X2 = w, Y2 = 0, Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)), StrokeDashArray = new DoubleCollection { 3, 3 } });
        var pts = _curves[_curveChannel];
        var poly = new Polyline { Stroke = Brushes.White, StrokeThickness = 2 };
        foreach (var p in pts) poly.Points.Add(new Point(p.X * w, (1 - p.Y) * h));
        CurveCanvas.Children.Add(poly);
        foreach (var p in pts)
        {
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
            Canvas.SetLeft(dot, p.X * w - 4); Canvas.SetTop(dot, (1 - p.Y) * h - 4); CurveCanvas.Children.Add(dot);
        }
    }

    private void CurveChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) _curveChannel = tag;
        DrawCurve();
    }

    private void CurveReset_Click(object sender, RoutedEventArgs e)
    {
        _curves[_curveChannel] = new() { new Point(0, 1), new Point(1, 0) };
        DrawCurve(); SchedulePreview(true);
    }

    private void ColorWheel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { _colorWheelDragging = true; ColorWheelCanvas.CaptureMouse(); UpdateColorWheel(e.GetPosition(ColorWheelCanvas)); }
    private void ColorWheel_MouseMove(object sender, MouseEventArgs e) { if (_colorWheelDragging) UpdateColorWheel(e.GetPosition(ColorWheelCanvas)); }
    private void ColorWheel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _colorWheelDragging = false; ColorWheelCanvas.ReleaseMouseCapture(); SchedulePreview(true); }

    private void UpdateColorWheel(Point p)
    {
        double cx = ColorWheelCanvas.ActualWidth / 2, cy = ColorWheelCanvas.ActualHeight / 2;
        double dx = p.X - cx, dy = p.Y - cy, radius = Math.Max(1, Math.Min(cx, cy));
        _gradeHue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _gradeSaturation = Math.Clamp(Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1);
        _gradeLuma = Math.Clamp(1 - Math.Sqrt(dx * dx + dy * dy) / radius, 0, 1);
        DrawColorWheel(); SchedulePreview();
    }

    private void DrawColorWheel()
    {
        if (ColorWheelCanvas == null) return;
        ColorWheelCanvas.Children.Clear();
        double size = Math.Max(100, Math.Min(ColorWheelCanvas.ActualWidth, ColorWheelCanvas.ActualHeight));
        double cx = size / 2, cy = size / 2, r = size * .43;
        for (int i = 0; i < 24; i++)
        {
            double a0 = i * 15 - 90, a1 = (i + 1) * 15 - 90;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(cx, cy), true, true);
                c.LineTo(new Point(cx + Math.Cos(a0 * Math.PI / 180) * r, cy + Math.Sin(a0 * Math.PI / 180) * r), true, false);
                c.ArcTo(new Point(cx + Math.Cos(a1 * Math.PI / 180) * r, cy + Math.Sin(a1 * Math.PI / 180) * r), new Size(r, r), 15, false, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            var path = new System.Windows.Shapes.Path { Data = geo, Fill = new SolidColorBrush(ColorFromHsv(i * 15, 1, .8)), Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), StrokeThickness = .5 };
            ColorWheelCanvas.Children.Add(path);
        }
        var puck = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1 };
        double pr = r * _gradeSaturation, pa = _gradeHue * Math.PI / 180;
        Canvas.SetLeft(puck, cx + Math.Cos(pa) * pr - 6); Canvas.SetTop(puck, cy + Math.Sin(pa) * pr - 6); ColorWheelCanvas.Children.Add(puck);
    }

    private static Color ColorFromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60 % 2) - 1)), m = v - c; double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private void GradeRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) _gradeRange = tag;
        DrawColorWheel(); SchedulePreview();
    }

    private void MaskBrush_Click(object sender, RoutedEventArgs e) { _brushMaskEnabled = true; _linearMaskEnabled = _radialMaskEnabled = _autoSkyMaskEnabled = _autoSubjectMaskEnabled = false; EnterTool(ToolMode.Mask); StatusText.Text = "Aetherlight • Brush mask: paint on the image"; }
    private void MaskLinear_Click(object sender, RoutedEventArgs e) { _linearMaskEnabled = true; _brushMaskEnabled = _radialMaskEnabled = _autoSkyMaskEnabled = _autoSubjectMaskEnabled = false; EnterTool(ToolMode.Mask); StatusText.Text = "Aetherlight • Linear mask: drag across the image"; }
    private void MaskRadial_Click(object sender, RoutedEventArgs e) { _radialMaskEnabled = true; _brushMaskEnabled = _linearMaskEnabled = _autoSkyMaskEnabled = _autoSubjectMaskEnabled = false; EnterTool(ToolMode.Mask); StatusText.Text = "Aetherlight • Radial mask: drag from center"; }
    private void MaskSky_Click(object sender, RoutedEventArgs e) { _autoSkyMaskEnabled = true; _brushMaskEnabled = _linearMaskEnabled = _radialMaskEnabled = _autoSubjectMaskEnabled = false; ExitTool(); SchedulePreview(true); StatusText.Text = "Aetherlight • Auto Sky mask active"; }
    private void MaskSubject_Click(object sender, RoutedEventArgs e) { _autoSubjectMaskEnabled = true; _brushMaskEnabled = _linearMaskEnabled = _radialMaskEnabled = _autoSkyMaskEnabled = false; ExitTool(); SchedulePreview(true); StatusText.Text = "Aetherlight • Auto Subject mask active"; }

    private void ResetAdvanced_Click(object sender, RoutedEventArgs e)
    {
        SharpeningSlider.Value = NoiseReductionSlider.Value = ClaritySlider.Value = TextureSlider.Value = DehazeSlider.Value = VignetteSlider.Value = GrainSlider.Value = GlowSlider.Value = HalationSlider.Value = 0;
        GradeBlendSlider.Value = 1; GradeBalanceSlider.Value = 0;
        _gradeHue = _gradeSaturation = _gradeLuma = 0; _gradeRange = "M";
        _curves = new Dictionary<string, List<Point>> { ["L"] = new() { new Point(0, 1), new Point(1, 0) }, ["R"] = new() { new Point(0, 1), new Point(1, 0) }, ["G"] = new() { new Point(0, 1), new Point(1, 0) }, ["B"] = new() { new Point(0, 1), new Point(1, 0) } };
        _brushMaskEnabled = _linearMaskEnabled = _radialMaskEnabled = _autoSkyMaskEnabled = _autoSubjectMaskEnabled = false;
        _maskPoints.Clear(); DrawCurve(); DrawColorWheel(); DrawMaskOverlay(); SchedulePreview(true);
    }

    private void ExportAdvanced_Click(object sender, RoutedEventArgs e)
    {
        if (_originalPixels == null || _pixelWidth == 0) { MessageBox.Show("Import and select a photo first.", "Aetherlight", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        StatusText.Text = "Aetherlight • Rendering full-resolution export…";
        RenderFullAdvanced();
        Export_Click(sender, e);
        StatusText.Text = "Aetherlight • Ready";
    }

    private void RenderFullAdvanced()
    {
        ApplyAdjustments();
        if (_editedBitmap == null) return;
        byte[] pixels = new byte[_pixelWidth * _pixelHeight * 4];
        _editedBitmap.CopyPixels(pixels, _pixelWidth * 4, 0);
        AdvancedSnapshot a = CaptureAdvancedSnapshot();
        int w = _pixelWidth, h = _pixelHeight;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
                ApplyCurve(ref r, ref g, ref b, a.Curves);
                ApplyColorGrade(ref r, ref g, ref b, a.GradeHue, a.GradeSaturation, a.GradeLuma, a.GradeBlend, a.GradeBalance, a.GradeRange);
                ApplyDetailAndEffects(ref r, ref g, ref b, pixels, w, h, x, y, a, 1, 1);
                ApplyMask(ref r, ref g, ref b, x, y, w, h, a);
                pixels[i] = ToByteFast(b); pixels[i + 1] = ToByteFast(g); pixels[i + 2] = ToByteFast(r); pixels[i + 3] = 255;
            }
        });
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bmp.Freeze();
        _editedBitmap = new WriteableBitmap(bmp);
        _editedBitmap.Freeze();
        DevelopPreview.Source = _editedBitmap;
    }

    private readonly record struct PreviewResult(byte[] Pixels, int Width, int Height);
    private readonly record struct AdvancedSnapshot(
        double Sharpening, double NoiseReduction, double Clarity, double Texture, double DeHaze, double Vignette, double Grain, double Glow, double Halation,
        double GradeHue, double GradeSaturation, double GradeLuma, double GradeBlend, double GradeBalance, string GradeRange,
        Dictionary<string, Point[]> Curves, Point[] MaskPoints, bool BrushMask, bool LinearMask, bool RadialMask, bool AutoSky, bool AutoSubject,
        Point LinearStart, Point LinearEnd, Point RadialCenter, double RadialRadius, double MaskSize, double MaskExposure)
    {
        public int SourceWidth => Math.Max(1, Curves["L"].Length > 0 ? 1 : 1);
        public int SourceHeight => SourceWidth;
    }
}
