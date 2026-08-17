using Microsoft.Win32;
using Sdcb.LibRaw;
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Aetherlight;

public partial class MainWindow : Window
{
    private BitmapSource? _originalSource;
    private WriteableBitmap? _editedBitmap;
    private byte[]? _originalPixels;
    private int _pixelWidth;
    private int _pixelHeight;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => DrawHistogram();
    }

    private void ShowView(Grid view)
    {
        LibraryView.Visibility = Visibility.Collapsed;
        DevelopView.Visibility = Visibility.Collapsed;
        MapView.Visibility = Visibility.Collapsed;
        PrintView.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
        view.Visibility = Visibility.Visible;
    }

    private void Library_Click(object sender, RoutedEventArgs e) => ShowView(LibraryView);
    private void Develop_Click(object sender, RoutedEventArgs e)
    {
        ShowView(DevelopView);
        DevelopPreview.Source = _editedBitmap ?? _originalSource;
        DevelopEmpty.Visibility = DevelopPreview.Source == null ? Visibility.Visible : Visibility.Collapsed;
        DrawHistogram();
    }
    private void Map_Click(object sender, RoutedEventArgs e) => ShowView(MapView);
    private void Print_Click(object sender, RoutedEventArgs e) => ShowView(PrintView);
    private void Web_Click(object sender, RoutedEventArgs e) => ShowView(WebView);

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "RAW & Photos|*.cr3;*.arw;*.raf;*.dng;*.nef;*.nrw;*.orf;*.rw2;*.pef;*.srw;*.tif;*.tiff;*.jpg;*.jpeg;*.png|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        Filmstrip.Children.Clear();
        LibraryCount.Text = $"{dlg.FileNames.Length} photo{(dlg.FileNames.Length == 1 ? "" : "s")}";
        StatusText.Text = "Aetherlight • Importing…";

        foreach (var path in dlg.FileNames)
        {
            try
            {
                var source = await Task.Run(() => LoadPhoto(path, true));
                if (source == null) continue;
                var thumb = new Image
                {
                    Source = source,
                    Width = 150,
                    Height = 105,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(4),
                    ToolTip = IOPath.GetFileName(path)
                };
                thumb.MouseLeftButtonUp += (_, _) => SelectPhoto(path);
                Filmstrip.Children.Add(thumb);

                if (_originalSource == null)
                    SetCurrentPhoto(source, path);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Import failed: {IOPath.GetFileName(path)}";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        StatusText.Text = "Aetherlight • Ready";
    }

    private void SelectPhoto(string path)
    {
        try
        {
            var source = LoadPhoto(path, false);
            if (source != null) SetCurrentPhoto(source, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open {IOPath.GetFileName(path)}\n\n{ex.Message}", "Aetherlight", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetCurrentPhoto(BitmapSource source, string path)
    {
        _originalSource = source;
        _loading = true;
        ExposureSlider.Value = 0;
        ContrastSlider.Value = 0;
        HighlightsSlider.Value = 0;
        ShadowsSlider.Value = 0;
        WhitesSlider.Value = 0;
        BlacksSlider.Value = 0;
        TemperatureSlider.Value = 0;
        TintSlider.Value = 0;
        VibranceSlider.Value = 0;
        SaturationSlider.Value = 0;
        _loading = false;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        _pixelWidth = converted.PixelWidth;
        _pixelHeight = converted.PixelHeight;
        _originalPixels = new byte[_pixelWidth * _pixelHeight * 4];
        converted.CopyPixels(_originalPixels, _pixelWidth * 4, 0);
        ApplyAdjustments();

        Preview.Source = _editedBitmap;
        Preview.Visibility = Visibility.Visible;
        EmptyHint.Visibility = Visibility.Collapsed;
        DevelopPreview.Source = _editedBitmap;
        DevelopEmpty.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Aetherlight • {IOPath.GetFileName(path)} • {_pixelWidth} × {_pixelHeight}";
        DrawHistogram();
    }

    private static BitmapSource? LoadPhoto(string path, bool thumbnail)
    {
        var ext = IOPath.GetExtension(path).ToLowerInvariant();
        if (ext is ".cr3" or ".arw" or ".raf" or ".dng" or ".nef" or ".nrw" or ".orf" or ".rw2" or ".pef" or ".srw")
        {
            using RawContext raw = RawContext.OpenFile(path);
            using ProcessedImage image = raw.ExportRawImage(c =>
            {
                c.HalfSize = thumbnail;
                c.UseCameraWb = true;
                c.OutputBps = 8;
                c.Brightness = 1.0f;
                c.Interpolation = true;
            });

            int width = Convert.ToInt32(image.Width);
            int height = Convert.ToInt32(image.Height);
            byte[] rgb = image.AsSpan<byte>().ToArray();
            int expected = checked(width * height * 3);
            if (rgb.Length < expected)
                throw new System.IO.InvalidDataException($"RAW decoder returned {rgb.Length} bytes for a {width}×{height} image.");

            byte[] bgra = new byte[checked(width * height * 4)];
            int src = 0;
            int dst = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = rgb[src++];
                    byte g = rgb[src++];
                    byte b = rgb[src++];
                    bgra[dst++] = b;
                    bgra[dst++] = g;
                    bgra[dst++] = r;
                    bgra[dst++] = 255;
                }
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
        ApplyAdjustments();
    }

    private void ApplyAdjustments()
    {
        if (_originalPixels == null || _pixelWidth == 0) return;

        byte[] pixels = new byte[_originalPixels.Length];
        double exposure = Math.Pow(2, ExposureSlider.Value);
        double contrast = (259.0 * (ContrastSlider.Value + 255.0)) / (255.0 * (259.0 - ContrastSlider.Value));
        double saturation = 1.0 + SaturationSlider.Value / 100.0;
        double vibrance = VibranceSlider.Value / 100.0;
        double temperature = TemperatureSlider.Value / 100.0;
        double tint = TintSlider.Value / 100.0;
        double highlights = HighlightsSlider.Value / 100.0;
        double shadows = ShadowsSlider.Value / 100.0;
        double whites = WhitesSlider.Value / 100.0;
        double blacks = BlacksSlider.Value / 100.0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = _originalPixels[i] / 255.0;
            double g = _originalPixels[i + 1] / 255.0;
            double r = _originalPixels[i + 2] / 255.0;

            r *= exposure; g *= exposure; b *= exposure;
            r = (r - 0.5) * contrast + 0.5;
            g = (g - 0.5) * contrast + 0.5;
            b = (b - 0.5) * contrast + 0.5;

            double luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double shadowMask = Math.Clamp(1.0 - luma * 2.0, 0, 1);
            double highlightMask = Math.Clamp((luma - 0.5) * 2.0, 0, 1);
            double whiteMask = Math.Clamp((luma - 0.7) / 0.3, 0, 1);
            double blackMask = Math.Clamp((0.3 - luma) / 0.3, 0, 1);
            r += shadows * shadowMask * 0.35 + highlights * highlightMask * -0.25 + whites * whiteMask * 0.25 + blacks * blackMask * -0.25;
            g += shadows * shadowMask * 0.35 + highlights * highlightMask * -0.25 + whites * whiteMask * 0.25 + blacks * blackMask * -0.25;
            b += shadows * shadowMask * 0.35 + highlights * highlightMask * -0.25 + whites * whiteMask * 0.25 + blacks * blackMask * -0.25;

            r += temperature * 0.10 + tint * 0.03;
            b -= temperature * 0.10;
            g -= tint * 0.03;

            double gray = (r + g + b) / 3.0;
            double vibFactor = 1.0 + vibrance * (1.0 - Math.Abs(gray - 0.5) * 2.0);
            r = gray + (r - gray) * saturation * vibFactor;
            g = gray + (g - gray) * saturation * vibFactor;
            b = gray + (b - gray) * saturation * vibFactor;

            pixels[i] = ToByte(b);
            pixels[i + 1] = ToByte(g);
            pixels[i + 2] = ToByte(r);
            pixels[i + 3] = 255;
        }

        _editedBitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        _editedBitmap.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, _pixelWidth * 4, 0);
        _editedBitmap.Freeze();
        Preview.Source = _editedBitmap;
        DevelopPreview.Source = _editedBitmap;
        DrawHistogram();
    }

    private static byte ToByte(double value) => (byte)(Math.Clamp(value, 0, 1) * 255.0);

    private void DrawHistogram()
    {
        if (!IsLoaded || _editedBitmap == null || HistogramCanvas == null) return;
        HistogramCanvas.Children.Clear();
        int bins = 256;
        int[] red = new int[bins], green = new int[bins], blue = new int[bins];
        byte[] data = new byte[_pixelWidth * _pixelHeight * 4];
        _editedBitmap.CopyPixels(data, _pixelWidth * 4, 0);
        for (int i = 0; i < data.Length; i += 4)
        {
            blue[data[i]]++; green[data[i + 1]]++; red[data[i + 2]]++;
        }
        int max = Math.Max(1, Math.Max(red.Max(), Math.Max(green.Max(), blue.Max())));
        double w = Math.Max(300, HistogramCanvas.ActualWidth > 10 ? HistogramCanvas.ActualWidth : 330);
        double h = 140;
        AddHistogramLine(red, w, h, Brushes.Red, max);
        AddHistogramLine(green, w, h, Brushes.LimeGreen, max);
        AddHistogramLine(blue, w, h, Brushes.DodgerBlue, max);
    }

    private void AddHistogramLine(int[] bins, double width, double height, Brush brush, int max)
    {
        var line = new Polyline { Stroke = brush, StrokeThickness = 1, Opacity = 0.65 };
        for (int i = 0; i < bins.Length; i++)
            line.Points.Add(new Point(i * width / 255.0, height - (bins[i] / (double)max) * (height - 4)));
        HistogramCanvas.Children.Add(line);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_editedBitmap == null)
        {
            MessageBox.Show("Import and select a photo first.", "Aetherlight");
            return;
        }
        var dlg = new SaveFileDialog { Filter = "JPEG|*.jpg|PNG|*.png|TIFF|*.tif", FileName = "Aetherlight Export.jpg" };
        if (dlg.ShowDialog() != true) return;
        BitmapEncoder encoder = IOPath.GetExtension(dlg.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_editedBitmap));
        using var stream = System.IO.File.Create(dlg.FileName);
        encoder.Save(stream);
        StatusText.Text = $"Aetherlight • Exported {IOPath.GetFileName(dlg.FileName)}";
    }
}
