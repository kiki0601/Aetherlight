using Microsoft.Win32;
using Sdcb.LibRaw;
using System.Globalization;
using System.IO;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Aetherlight;

public partial class MainWindow : Window
{
    private enum ToolMode { None, Crop, Heal, Mask, Picker }
    private ToolMode _toolMode = ToolMode.None;
    private BitmapSource? _originalSource;
    private WriteableBitmap? _editedBitmap;
    private byte[]? _originalPixels;
    private int _pixelWidth, _pixelHeight;
    private bool _loading, _dragging, _editingNumeric;
    private string? _currentPhotoPath;
    private Point _cropStart, _cropEnd;
    private readonly List<HealSpot> _healSpots = new();
    private readonly List<Point> _maskPoints = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DrawHistogram();
        AttachAdjustmentHandlers();
    }

    private void AttachAdjustmentHandlers()
    {
        ExposureSlider.ValueChanged += Adjustment_ValueChanged;
        ContrastSlider.ValueChanged += Adjustment_ValueChanged;
        HighlightsSlider.ValueChanged += Adjustment_ValueChanged;
        ShadowsSlider.ValueChanged += Adjustment_ValueChanged;
        WhitesSlider.ValueChanged += Adjustment_ValueChanged;
        BlacksSlider.ValueChanged += Adjustment_ValueChanged;
        TemperatureSlider.ValueChanged += Adjustment_ValueChanged;
        TintSlider.ValueChanged += Adjustment_ValueChanged;
        VibranceSlider.ValueChanged += Adjustment_ValueChanged;
        SaturationSlider.ValueChanged += Adjustment_ValueChanged;
    }

    private void ShowView(Grid view)
    {
        LibraryView.Visibility = Visibility.Collapsed;
        DevelopView.Visibility = Visibility.Collapsed;
        view.Visibility = Visibility.Visible;
        if (view == DevelopView)
        {
            DevelopPreview.Source = _editedBitmap ?? _originalSource;
            DevelopEmpty.Visibility = DevelopPreview.Source == null ? Visibility.Visible : Visibility.Collapsed;
            DrawHistogram();
        }
    }

    private void Library_Click(object sender, RoutedEventArgs e) => ShowView(LibraryView);
    private void Develop_Click(object sender, RoutedEventArgs e) => ShowView(DevelopView);

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "RAW & Photos|*.cr3;*.arw;*.raf;*.dng;*.nef;*.nrw;*.orf;*.rw2;*.pef;*.srw;*.tif;*.tiff;*.jpg;*.jpeg;*.png|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        Filmstrip.Children.Clear();
        LibraryCount.Text = $"{dlg.FileNames.Length} photo{(dlg.FileNames.Length == 1 ? "" : "s")}";
        StatusText.Text = "Aetherlight • Importing…";
        bool selectedFirst = false;
        foreach (var path in dlg.FileNames)
        {
            try
            {
                BitmapSource thumbSource = LoadPhoto(path, true);
                var thumbnail = new Image { Source = thumbSource, Width = 150, Height = 105, Stretch = Stretch.UniformToFill, Margin = new Thickness(4), ToolTip = IOPath.GetFileName(path) };
                thumbnail.MouseLeftButtonUp += (_, _) => SelectPhoto(path);
                Filmstrip.Children.Add(thumbnail);
                if (!selectedFirst) { selectedFirst = true; SelectPhoto(path); }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Import failed • {IOPath.GetFileName(path)}";
                MessageBox.Show($"Aetherlight could not import:\n\n{IOPath.GetFileName(path)}\n\n{ex.GetType().Name}:\n{ex.Message}", "Aetherlight • Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        StatusText.Text = Filmstrip.Children.Count > 0 ? $"Aetherlight • {Filmstrip.Children.Count} photo(s) imported" : "Aetherlight • No photos imported";
    }

    private void SelectPhoto(string path)
    {
        try
        {
            StatusText.Text = $"Aetherlight • Developing {IOPath.GetFileName(path)}…";
            SetCurrentPhoto(LoadPhoto(path, false), path);
            ShowView(DevelopView);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Aetherlight • Could not open {IOPath.GetFileName(path)}";
            MessageBox.Show($"Could not develop:\n\n{IOPath.GetFileName(path)}\n\n{ex.GetType().Name}:\n{ex.Message}", "Aetherlight • RAW Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetCurrentPhoto(BitmapSource source, string path)
    {
        _currentPhotoPath = path;
        _originalSource = source;
        _healSpots.Clear();
        _maskPoints.Clear();
        ExitTool();
        LoadAsShotWhiteBalance(path);
        ResetAdjustments();
        RefreshBasePixels();
        ApplyAdjustments();
        Preview.Source = _editedBitmap;
        Preview.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;
        DevelopPreview.Source = _editedBitmap;
        DevelopEmpty.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Aetherlight • {IOPath.GetFileName(path)} • {_pixelWidth} × {_pixelHeight}";
        DrawHistogram();
    }

    private void RefreshBasePixels()
    {
        if (_originalSource == null) return;
        var converted = new FormatConvertedBitmap(_originalSource, PixelFormats.Bgra32, null, 0);
        _pixelWidth = converted.PixelWidth;
        _pixelHeight = converted.PixelHeight;
        _originalPixels = new byte[checked(_pixelWidth * _pixelHeight * 4)];
        converted.CopyPixels(_originalPixels, checked(_pixelWidth * 4), 0);
    }

    private static BitmapSource LoadPhoto(string path, bool thumbnail)
    {
        var ext = IOPath.GetExtension(path).ToLowerInvariant();
        if (ext is ".cr3" or ".arw" or ".raf" or ".dng" or ".nef" or ".nrw" or ".orf" or ".rw2" or ".pef" or ".srw")
        {
            using RawContext raw = RawContext.OpenFile(path);
            if (thumbnail)
            {
                using ProcessedImage preview = raw.ExportThumbnail(0);
                byte[] jpeg = preview.AsSpan<byte>().ToArray();
                if (jpeg.Length == 0) throw new InvalidDataException("The RAW file contains no usable embedded preview.");
                using var ms = new MemoryStream(jpeg);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            using ProcessedImage image = raw.ExportRawImage(c => { c.HalfSize = false; c.UseCameraWb = true; c.OutputBps = 8; c.Brightness = 1.0f; c.Interpolation = true; c.OutputTiff = false; });
            int width = Convert.ToInt32(image.Width), height = Convert.ToInt32(image.Height);
            byte[] rgb = image.AsSpan<byte>().ToArray();
            int expected = checked(width * height * 3);
            if (rgb.Length < expected) throw new InvalidDataException($"RAW decoder returned {rgb.Length} bytes for a {width}×{height} image.");
            byte[] bgra = new byte[checked(width * height * 4)];
            int src = 0, dst = 0;
            for (int i = 0; i < width * height; i++)
            {
                byte r = rgb[src++], g = rgb[src++], b = rgb[src++];
                bgra[dst++] = b; bgra[dst++] = g; bgra[dst++] = r; bgra[dst++] = 255;
            }
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(IOPath.GetFullPath(path));
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = thumbnail ? 900 : 0;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void Adjustment_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null) return;
        UpdateValueLabels();
        ApplyAdjustments();
    }

    private void UpdateValueLabels()
    {
        if (!_editingNumeric)
        {
            ExposureValue.Text = ExposureSlider.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            ContrastValue.Text = ContrastSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            HighlightsValue.Text = HighlightsSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            ShadowsValue.Text = ShadowsSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            WhitesValue.Text = WhitesSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            BlacksValue.Text = BlacksSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            UpdateWhiteBalanceLabels();
            VibranceValue.Text = VibranceSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            SaturationValue.Text = SaturationSlider.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }
    }

    private void SliderNumber_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box) return;
        _editingNumeric = true;
        Dispatcher.BeginInvoke(new Action(() => box.SelectAll()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SliderNumber_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter)
        {
            CommitNumericValue(box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _editingNumeric = false;
            UpdateValueLabels();
            e.Handled = true;
        }
        else if (e.Key == Key.Up || e.Key == Key.Down)
        {
            if (TryGetSlider(box.Tag?.ToString(), out Slider? slider))
            {
                double step = GetNumericStep(slider);
                slider.Value = Math.Clamp(slider.Value + (e.Key == Key.Up ? step : -step), slider.Minimum, slider.Maximum);
                box.Text = FormatSliderValue(box.Tag?.ToString() ?? string.Empty, slider.Value);
                box.SelectAll();
                e.Handled = true;
            }
        }
    }

    private void SliderNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitNumericValue(box);
    }

    private void CommitNumericValue(TextBox box)
    {
        string tag = box.Tag?.ToString() ?? string.Empty;
        if (!TryGetSlider(tag, out Slider? slider)) { _editingNumeric = false; return; }
        string raw = box.Text.Replace("K", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double value))
        {
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            box.Text = FormatSliderValue(tag, slider.Value);
        }
        else
        {
            box.Text = FormatSliderValue(tag, slider.Value);
        }
        _editingNumeric = false;
        UpdateValueLabels();
    }

    private static double GetNumericStep(Slider slider)
    {
        if (slider.Name == nameof(ExposureSlider)) return 0.01;
        if (slider.Name == nameof(TemperatureSlider)) return 1;
        return 1;
    }

    private static string FormatSliderValue(string tag, double value) => tag switch
    {
        "Exposure" => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
        "Temperature" => $"{Math.Round(value):0} K",
        _ => value.ToString("+0;-0;0", CultureInfo.InvariantCulture)
    };

    private bool TryGetSlider(string? tag, out Slider? slider)
    {
        slider = tag switch
        {
            "Exposure" => ExposureSlider,
            "Contrast" => ContrastSlider,
            "Highlights" => HighlightsSlider,
            "Shadows" => ShadowsSlider,
            "Whites" => WhitesSlider,
            "Blacks" => BlacksSlider,
            "Temperature" => TemperatureSlider,
            "Tint" => TintSlider,
            "Vibrance" => VibranceSlider,
            "Saturation" => SaturationSlider,
            _ => null
        };
        return slider != null;
    }

    private void ApplyAdjustments()
    {
        if (_originalPixels == null || _pixelWidth == 0) return;
        byte[] pixels = new byte[_originalPixels.Length];
        double exposure = Math.Pow(2, ExposureSlider.Value);
        double contrast = (259.0 * (ContrastSlider.Value + 255.0)) / (255.0 * (259.0 - ContrastSlider.Value));
        double saturation = 1 + SaturationSlider.Value / 100.0;
        double vibrance = VibranceSlider.Value / 100.0;
        double temperatureDelta = Math.Log(Math.Max(1.0, TemperatureSlider.Value) / Math.Max(1.0, _asShotTemperature), 2);
        double tint = TintSlider.Value / 100.0;
        double highlights = HighlightsSlider.Value / 100.0;
        double shadows = ShadowsSlider.Value / 100.0;
        double whites = WhitesSlider.Value / 100.0;
        double blacks = BlacksSlider.Value / 100.0;
        double maskRadius = MaskSizeSlider.Value;

        for (int y = 0; y < _pixelHeight; y++) for (int x = 0; x < _pixelWidth; x++)
        {
            int i = (y * _pixelWidth + x) * 4, sx = x, sy = y;
            foreach (var spot in _healSpots)
            {
                double dx = x - spot.X, dy = y - spot.Y;
                if (dx * dx + dy * dy <= spot.Radius * spot.Radius)
                {
                    sx = Math.Clamp((int)(x - 32), 0, _pixelWidth - 1);
                    sy = Math.Clamp((int)(y - 32), 0, _pixelHeight - 1);
                    break;
                }
            }
            int si = (sy * _pixelWidth + sx) * 4;
            double b = _originalPixels[si] / 255.0, g = _originalPixels[si + 1] / 255.0, r = _originalPixels[si + 2] / 255.0;
            r *= exposure; g *= exposure; b *= exposure;
            r = (r - .5) * contrast + .5; g = (g - .5) * contrast + .5; b = (b - .5) * contrast + .5;
            double luma = .2126 * r + .7152 * g + .0722 * b;
            double shadowMask = Math.Clamp(1 - luma * 2, 0, 1);
            double highlightMask = Math.Clamp((luma - .5) * 2, 0, 1);
            double whiteMask = Math.Clamp((luma - .7) / .3, 0, 1);
            double blackMask = Math.Clamp((.3 - luma) / .3, 0, 1);
            // Positive Highlights now increases highlights to the right; negative recovers them to the left.
            double tonal = shadows * shadowMask * .35 + highlights * highlightMask * .25 + whites * whiteMask * .25 + blacks * blackMask * -.25;
            r += tonal; g += tonal; b += tonal;

            double warm = temperatureDelta * .10;
            r += warm + tint * .03;
            b -= warm;
            g -= tint * .03;

            double gray = (r + g + b) / 3;
            double vibFactor = 1 + vibrance * (1 - Math.Abs(gray - .5) * 2);
            r = gray + (r - gray) * saturation * vibFactor;
            g = gray + (g - gray) * saturation * vibFactor;
            b = gray + (b - gray) * saturation * vibFactor;

            foreach (var mp in _maskPoints)
            {
                double dx = x - mp.X, dy = y - mp.Y, dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= maskRadius)
                {
                    double falloff = 1 - dist / maskRadius;
                    double mf = Math.Pow(2, MaskExposureSlider.Value * falloff);
                    r *= mf; g *= mf; b *= mf;
                    break;
                }
            }
            pixels[i] = ToByte(b); pixels[i + 1] = ToByte(g); pixels[i + 2] = ToByte(r); pixels[i + 3] = 255;
        }

        _editedBitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        _editedBitmap.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, _pixelWidth * 4, 0);
        _editedBitmap.Freeze();
        Preview.Source = _editedBitmap;
        DevelopPreview.Source = _editedBitmap;
        DrawHistogram();
    }

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255.0);

    private void Crop_Click(object sender, RoutedEventArgs e) { EnterTool(ToolMode.Crop); StatusText.Text = "Aetherlight • Crop: drag a rectangle on the photo"; }
    private void Heal_Click(object sender, RoutedEventArgs e) { EnterTool(ToolMode.Heal); StatusText.Text = "Aetherlight • Heal: click a blemish to clone nearby pixels"; }
    private void Mask_Click(object sender, RoutedEventArgs e) { EnterTool(ToolMode.Mask); StatusText.Text = "Aetherlight • Mask: paint over the area you want to adjust"; }
    private void Picker_Click(object sender, RoutedEventArgs e) { EnterTool(ToolMode.Picker); StatusText.Text = "Aetherlight • Color Picker: click the image"; }
    private void EnterTool(ToolMode mode) { _toolMode = mode; _dragging = false; OverlayCanvas.Children.Clear(); CropControls.Visibility = mode == ToolMode.Crop ? Visibility.Visible : Visibility.Collapsed; MaskControls.Visibility = mode == ToolMode.Mask ? Visibility.Visible : Visibility.Collapsed; }
    private void ExitTool() { _toolMode = ToolMode.None; _dragging = false; OverlayCanvas.Children.Clear(); CropControls.Visibility = Visibility.Collapsed; MaskControls.Visibility = Visibility.Collapsed; }
    private void CancelTool_Click(object sender, RoutedEventArgs e) { ExitTool(); StatusText.Text = "Aetherlight • Ready"; DevelopPreview.Source = _editedBitmap; }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(DevelopPreview);
        if (_toolMode == ToolMode.Crop) { _cropStart = p; _cropEnd = p; _dragging = true; DrawCropOverlay(); DevelopPreview.CaptureMouse(); }
        else if (_toolMode == ToolMode.Heal) AddHealSpot(p);
        else if (_toolMode == ToolMode.Mask) { _dragging = true; AddMaskPoint(p); DevelopPreview.CaptureMouse(); }
        else if (_toolMode == ToolMode.Picker) PickColor(p);
    }
    private void Preview_MouseMove(object sender, MouseEventArgs e) { if (!_dragging) return; Point p = e.GetPosition(DevelopPreview); if (_toolMode == ToolMode.Crop) { _cropEnd = p; DrawCropOverlay(); } else if (_toolMode == ToolMode.Mask) AddMaskPoint(p); }
    private void Preview_MouseUp(object sender, MouseButtonEventArgs e) { if (_dragging) { _dragging = false; DevelopPreview.ReleaseMouseCapture(); } }

    private Rect GetDisplayedImageRect(BitmapSource source)
    {
        double cw = DevelopPreview.ActualWidth, ch = DevelopPreview.ActualHeight, scale = Math.Min(cw / source.PixelWidth, ch / source.PixelHeight); double w = source.PixelWidth * scale, h = source.PixelHeight * scale; return new Rect((cw - w) / 2, (ch - h) / 2, w, h);
    }
    private Point DisplayToSource(Point p, BitmapSource? source = null)
    {
        source ??= _editedBitmap ?? _originalSource; if (source == null) return new Point(); Rect r = GetDisplayedImageRect(source); return new Point(Math.Clamp((p.X - r.X) / r.Width * source.PixelWidth, 0, source.PixelWidth - 1), Math.Clamp((p.Y - r.Y) / r.Height * source.PixelHeight, 0, source.PixelHeight - 1));
    }
    private Point SourceToDisplay(Point p, BitmapSource? source = null)
    {
        source ??= _editedBitmap ?? _originalSource; if (source == null) return new Point(); Rect r = GetDisplayedImageRect(source); return new Point(r.X + p.X / source.PixelWidth * r.Width, r.Y + p.Y / source.PixelHeight * r.Height);
    }
    private void DrawCropOverlay() { OverlayCanvas.Children.Clear(); double x = Math.Min(_cropStart.X, _cropEnd.X), y = Math.Min(_cropStart.Y, _cropEnd.Y), w = Math.Abs(_cropEnd.X - _cropStart.X), h = Math.Abs(_cropEnd.Y - _cropStart.Y); var rect = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)) }; Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); OverlayCanvas.Children.Add(rect); }
    private void AddHealSpot(Point displayPoint) { Point p = DisplayToSource(displayPoint); _healSpots.Add(new HealSpot(p.X, p.Y)); ApplyAdjustments(); StatusText.Text = $"Aetherlight • Heal spot {p.X:0}, {p.Y:0}"; }
    private void AddMaskPoint(Point displayPoint) { Point p = DisplayToSource(displayPoint); _maskPoints.Add(p); DrawMaskOverlay(); ApplyAdjustments(); }
    private void DrawMaskOverlay() { OverlayCanvas.Children.Clear(); double radius = MaskSizeSlider.Value; foreach (var p in _maskPoints.TakeLast(120)) { Point d = SourceToDisplay(p); var ellipse = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = new SolidColorBrush(Color.FromArgb(55, 255, 80, 80)), Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 100, 100)), StrokeThickness = 1 }; Canvas.SetLeft(ellipse, d.X - radius); Canvas.SetTop(ellipse, d.Y - radius); OverlayCanvas.Children.Add(ellipse); } }
    private void ApplyMask_Click(object sender, RoutedEventArgs e) { ApplyAdjustments(); StatusText.Text = $"Aetherlight • Mask applied • {_maskPoints.Count} brush points"; }
    private void ClearMask_Click(object sender, RoutedEventArgs e) { _maskPoints.Clear(); OverlayCanvas.Children.Clear(); ApplyAdjustments(); StatusText.Text = "Aetherlight • Mask cleared"; }
    private void MaskExposure_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { MaskExposureValue.Text = MaskExposureSlider.Value.ToString("+0.00;-0.00;0.00"); if (_toolMode == ToolMode.Mask && _originalPixels != null) ApplyAdjustments(); }

    private void PickColor(Point displayPoint)
    {
        if (_editedBitmap == null) return; Point p = DisplayToSource(displayPoint, _editedBitmap); byte[] pixel = new byte[4]; _editedBitmap.CopyPixels(new Int32Rect((int)p.X, (int)p.Y, 1, 1), pixel, 4, 0); byte b = pixel[0], g = pixel[1], r = pixel[2]; double max = Math.Max(r, Math.Max(g, b)) / 255.0, min = Math.Min(r, Math.Min(g, b)) / 255.0, delta = max - min, h = 0; if (delta > 0) { double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0; if (max == rr) h = 60 * (((gg - bb) / delta) % 6); else if (max == gg) h = 60 * ((bb - rr) / delta + 2); else h = 60 * ((rr - gg) / delta + 4); if (h < 0) h += 360; } double l = (max + min) / 2, s = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * l - 1)); StatusText.Text = $"Color Picker • RGB {r}, {g}, {b} • HSL {h:0}°, {s * 100:0}%, {l * 100:0}% • X {p.X:0} Y {p.Y:0}";
    }

    private void CropAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CropAngleValue == null || _toolMode != ToolMode.Crop || _editedBitmap == null) return; CropAngleValue.Text = CropAngleSlider.Value.ToString("+0.0;-0.0;0.0") + "°"; if (Math.Abs(CropAngleSlider.Value) < .01) { DevelopPreview.Source = _editedBitmap; return; } try { var transformed = new TransformedBitmap(_editedBitmap, new RotateTransform(CropAngleSlider.Value)); transformed.Freeze(); DevelopPreview.Source = transformed; } catch { }
    }
    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_editedBitmap == null) return; BitmapSource source = DevelopPreview.Source as BitmapSource ?? _editedBitmap; Rect displayed = GetDisplayedImageRect(source); double x1 = Math.Max(Math.Min(_cropStart.X, _cropEnd.X), displayed.Left), y1 = Math.Max(Math.Min(_cropStart.Y, _cropEnd.Y), displayed.Top), x2 = Math.Min(Math.Max(_cropStart.X, _cropEnd.X), displayed.Right), y2 = Math.Min(Math.Max(_cropStart.Y, _cropEnd.Y), displayed.Bottom);
        if (x2 - x1 < 10 || y2 - y1 < 10) { if (Math.Abs(CropAngleSlider.Value) > .01) { _originalSource = source; RefreshBasePixels(); ResetAdjustments(); ApplyAdjustments(); ExitTool(); } else StatusText.Text = "Aetherlight • Draw a crop rectangle first"; return; }
        Point a = DisplayToSource(new Point(x1, y1), source), b = DisplayToSource(new Point(x2, y2), source); int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X))), top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y))); int width = Math.Min(source.PixelWidth - left, Math.Max(1, (int)Math.Floor(Math.Abs(b.X - a.X)))), height = Math.Min(source.PixelHeight - top, Math.Max(1, (int)Math.Floor(Math.Abs(b.Y - a.Y)))); var cropped = new CroppedBitmap(source, new Int32Rect(left, top, width, height)); cropped.Freeze(); _originalSource = cropped; RefreshBasePixels(); ResetAdjustments(); ApplyAdjustments(); ExitTool(); StatusText.Text = $"Aetherlight • Cropped • {width} × {height}";
    }

    private void ResetAdjustments()
    {
        _loading = true;
        ExposureSlider.Value = 0;
        ContrastSlider.Value = 0;
        HighlightsSlider.Value = 0;
        ShadowsSlider.Value = 0;
        WhitesSlider.Value = 0;
        BlacksSlider.Value = 0;
        TemperatureSlider.Value = Math.Clamp(_asShotTemperature, 2000, 50000);
        TintSlider.Value = 0;
        VibranceSlider.Value = 0;
        SaturationSlider.Value = 0;
        _loading = false;
        UpdateValueLabels();
    }

    private void DrawHistogram()
    {
        if (!IsLoaded || _editedBitmap == null || HistogramCanvas == null) return;
        HistogramCanvas.Children.Clear();
        int[] red = new int[256], green = new int[256], blue = new int[256];
        byte[] data = new byte[_pixelWidth * _pixelHeight * 4];
        _editedBitmap.CopyPixels(data, _pixelWidth * 4, 0);
        for (int i = 0; i < data.Length; i += 4) { blue[data[i]]++; green[data[i + 1]]++; red[data[i + 2]]++; }
        int max = Math.Max(1, Math.Max(red.Max(), Math.Max(green.Max(), blue.Max())));
        double w = Math.Max(300, HistogramCanvas.ActualWidth > 10 ? HistogramCanvas.ActualWidth : 330), h = 140;
        AddHistogramLine(red, w, h, Brushes.Red, max); AddHistogramLine(green, w, h, Brushes.LimeGreen, max); AddHistogramLine(blue, w, h, Brushes.DodgerBlue, max);
    }
    private void AddHistogramLine(int[] bins, double width, double height, Brush brush, int max) { var line = new Polyline { Stroke = brush, StrokeThickness = 1, Opacity = .65 }; for (int i = 0; i < bins.Length; i++) line.Points.Add(new Point(i * width / 255.0, height - (bins[i] / (double)max) * (height - 4))); HistogramCanvas.Children.Add(line); }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_editedBitmap == null) { MessageBox.Show("Import and select a photo first.", "Aetherlight", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var dlg = new SaveFileDialog { Title = "Export Photo", Filter = "JPEG Image (*.jpg)|*.jpg|PNG Image (*.png)|*.png|TIFF Image (*.tif)|*.tif", DefaultExt = ".jpg", AddExtension = true, FileName = _currentPhotoPath == null ? "Aetherlight Export.jpg" : IOPath.GetFileNameWithoutExtension(_currentPhotoPath) + " - Aetherlight.jpg", OverwritePrompt = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            BitmapEncoder encoder; string extension = IOPath.GetExtension(dlg.FileName).ToLowerInvariant();
            if (extension == ".png") encoder = new PngBitmapEncoder(); else if (extension == ".tif" || extension == ".tiff") encoder = new TiffBitmapEncoder(); else encoder = new JpegBitmapEncoder { QualityLevel = 100 };
            encoder.Frames.Add(BitmapFrame.Create(_editedBitmap));
            using var stream = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream); stream.Flush(true);
            StatusText.Text = $"Aetherlight • Exported • {IOPath.GetFileName(dlg.FileName)}";
            MessageBox.Show($"Export complete.\n\n{dlg.FileName}", "Aetherlight", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { StatusText.Text = "Aetherlight • Export failed"; MessageBox.Show($"Aetherlight could not export this photo.\n\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private sealed record HealSpot(double X, double Y, double Radius = 28);
}