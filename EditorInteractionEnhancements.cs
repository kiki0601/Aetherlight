using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Aetherlight;

public partial class MainWindow
{
    private enum CanvasMaskMode { Brush, Linear, Radial }

    private CanvasMaskMode _canvasMaskMode = CanvasMaskMode.Brush;
    private bool _enhancementsReady;
    private bool _maskDrawing;
    private Point _maskDrawStart;
    private Point _maskDrawCurrent;
    private double _canvasZoom = 1.0;
    private Vector _canvasPan;
    private bool _panningCanvas;
    private Point _panStartMouse;
    private Vector _panStartOffset;
    private readonly Dictionary<Slider, TextBox> _sliderValueBoxes = new();

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoadedForEnhancements));
        EventManager.RegisterClassHandler(typeof(Image), UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseDownClass), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMoveClass), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.PreviewMouseUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseUpClass), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.MouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheelClass), true);
    }

    private static void OnMainWindowLoadedForEnhancements(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._enhancementsReady) return;
        window._enhancementsReady = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(window.InitializeEditorEnhancements));
    }

    private void InitializeEditorEnhancements()
    {
        AddPreciseValueBoxesToAllSliders();
        AddMaskModeToolbar();
        if (DevelopPreview != null)
        {
            DevelopPreview.RenderTransformOrigin = new Point(0.5, 0.5);
            ApplyCanvasTransform();
        }
        UpdateCanvasZoomStatus();
    }

    private void AddPreciseValueBoxesToAllSliders()
    {
        foreach (Slider slider in FindVisualChildren<Slider>(this).ToList())
        {
            if (_sliderValueBoxes.ContainsKey(slider)) continue;

            if (slider.Parent is Grid existingGrid && existingGrid.Children.OfType<TextBox>().Any(t =>
                string.Equals(t.Tag?.ToString(), slider.Name.Replace("Slider", string.Empty), StringComparison.OrdinalIgnoreCase)))
                continue;

            var valueBox = new TextBox
            {
                Width = 58,
                Margin = new Thickness(7, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gainsboro,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Tag = slider.Name,
                Text = FormatEnhancementSliderValue(slider),
                ToolTip = "Click and type an exact value. Enter applies. Up/Down changes by one step."
            };

            valueBox.PreviewMouseLeftButtonDown += EnhancementValue_MouseDown;
            valueBox.KeyDown += EnhancementValue_KeyDown;
            valueBox.LostFocus += EnhancementValue_LostFocus;
            slider.ValueChanged += EnhancementSlider_ValueChanged;
            _sliderValueBoxes[slider] = valueBox;

            if (slider.Parent is Panel panel)
            {
                int index = panel.Children.IndexOf(slider);
                panel.Children.RemoveAt(index);
                var wrapper = new Grid { Margin = slider.Margin };
                slider.Margin = new Thickness(0, 2, 0, 9);
                wrapper.ColumnDefinitions.Add(new ColumnDefinition());
                wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(slider, 0);
                Grid.SetColumn(valueBox, 1);
                wrapper.Children.Add(slider);
                wrapper.Children.Add(valueBox);
                panel.Children.Insert(index, wrapper);
            }
        }
    }

    private void EnhancementSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && _sliderValueBoxes.TryGetValue(slider, out TextBox? box))
            box.Text = FormatEnhancementSliderValue(slider);
    }

    private static string FormatEnhancementSliderValue(Slider slider)
    {
        string name = slider.Name;
        double v = slider.Value;
        if (name.Contains("Exposure", StringComparison.OrdinalIgnoreCase))
            return v.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);
        if (name.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
            return $"{Math.Round(v):0} K";
        if (name.Contains("Angle", StringComparison.OrdinalIgnoreCase))
            return $"{v:+0.0;-0.0;0.0}°";
        if (Math.Abs(v - Math.Round(v)) < 0.0001)
            return v.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void EnhancementValue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box) return;
        Dispatcher.BeginInvoke(new Action(() => box.SelectAll()), DispatcherPriority.Input);
    }

    private void EnhancementValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string sliderName) return;
        Slider? slider = FindVisualChildren<Slider>(this).FirstOrDefault(s => s.Name == sliderName);
        if (slider == null) return;

        if (e.Key == Key.Enter)
        {
            CommitEnhancementValue(slider, box);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = FormatEnhancementSliderValue(slider);
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            double step = GetEnhancementStep(slider);
            slider.Value = Math.Clamp(slider.Value + (e.Key == Key.Up ? step : -step), slider.Minimum, slider.Maximum);
            box.Text = FormatEnhancementSliderValue(slider);
            box.SelectAll();
            e.Handled = true;
        }
    }

    private void EnhancementValue_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string sliderName) return;
        Slider? slider = FindVisualChildren<Slider>(this).FirstOrDefault(s => s.Name == sliderName);
        if (slider != null) CommitEnhancementValue(slider, box);
    }

    private static double GetEnhancementStep(Slider slider)
    {
        if (slider.Name.Contains("Temperature", StringComparison.OrdinalIgnoreCase)) return 1;
        if (slider.Name.Contains("Exposure", StringComparison.OrdinalIgnoreCase)) return 0.01;
        if (slider.Name.Contains("Angle", StringComparison.OrdinalIgnoreCase)) return 0.1;
        return 1;
    }

    private static void CommitEnhancementValue(Slider slider, TextBox box)
    {
        string raw = box.Text.Replace("K", string.Empty, StringComparison.OrdinalIgnoreCase)
                             .Replace("°", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(raw, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out double value))
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        box.Text = FormatEnhancementSliderValue(slider);
    }

    private void AddMaskModeToolbar()
    {
        if (MaskControls is not Panel panel) return;
        if (panel.Children.OfType<FrameworkElement>().Any(e => e.Tag?.ToString() == "AetherlightMaskModeToolbar")) return;

        var toolbar = new StackPanel
        {
            Tag = "AetherlightMaskModeToolbar",
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        toolbar.Children.Add(CreateMaskModeButton("Brush", CanvasMaskMode.Brush));
        toolbar.Children.Add(CreateMaskModeButton("Linear", CanvasMaskMode.Linear));
        toolbar.Children.Add(CreateMaskModeButton("Radial", CanvasMaskMode.Radial));
        panel.Children.Insert(0, toolbar);
    }

    private Button CreateMaskModeButton(string label, CanvasMaskMode mode)
    {
        var button = new Button
        {
            Content = label,
            Tag = mode,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromRgb(36, 36, 36)),
            Foreground = Brushes.Gainsboro,
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 65))
        };
        button.Click += (_, _) =>
        {
            _canvasMaskMode = mode;
            _maskDrawing = false;
            _maskPoints.Clear();
            OverlayCanvas.Children.Clear();
            StatusText.Text = $"Aetherlight • {mode} mask • drag on the image";
        };
        return button;
    }

    private static MainWindow? GetOwner(Image image) => Window.GetWindow(image) as MainWindow;

    private static void OnPreviewMouseWheelClass(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        MainWindow? window = GetOwner(image);
        if (window == null) return;
        window.ChangeCanvasZoom(e.Delta > 0 ? 1.15 : 1.0 / 1.15);
        e.Handled = true;
    }

    private static void OnPreviewMouseDownClass(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        MainWindow? window = GetOwner(image);
        if (window == null) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            window._panningCanvas = true;
            window._panStartMouse = e.GetPosition(image);
            window._panStartOffset = window._canvasPan;
            image.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (window._toolMode != ToolMode.Mask || e.ChangedButton != MouseButton.Left) return;
        window._maskDrawing = true;
        window._maskDrawStart = e.GetPosition(image);
        window._maskDrawCurrent = window._maskDrawStart;
        window.DrawEnhancedMaskPreview();
        image.CaptureMouse();
        e.Handled = true;
    }

    private static void OnPreviewMouseMoveClass(object sender, MouseEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        MainWindow? window = GetOwner(image);
        if (window == null) return;

        if (window._panningCanvas && e.MiddleButton == MouseButtonState.Pressed)
        {
            Point now = e.GetPosition(image);
            window._canvasPan = window._panStartOffset + (now - window._panStartMouse);
            window.ApplyCanvasTransform();
            e.Handled = true;
            return;
        }

        if (window._maskDrawing && window._toolMode == ToolMode.Mask && e.LeftButton == MouseButtonState.Pressed)
        {
            window._maskDrawCurrent = e.GetPosition(image);
            window.DrawEnhancedMaskPreview();
            e.Handled = true;
        }
    }

    private static void OnPreviewMouseUpClass(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        MainWindow? window = GetOwner(image);
        if (window == null) return;

        if (e.ChangedButton == MouseButton.Middle && window._panningCanvas)
        {
            window._panningCanvas = false;
            image.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && window._maskDrawing)
        {
            window._maskDrawCurrent = e.GetPosition(image);
            window.CommitEnhancedMaskStroke();
            window._maskDrawing = false;
            image.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ChangeCanvasZoom(double factor)
    {
        if (_editedBitmap == null && _originalSource == null) return;
        _canvasZoom = Math.Clamp(_canvasZoom * factor, 0.1, 16.0);
        ApplyCanvasTransform();
        UpdateCanvasZoomStatus();
    }

    private void ApplyCanvasTransform()
    {
        if (DevelopPreview == null) return;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(_canvasZoom, _canvasZoom));
        group.Children.Add(new TranslateTransform(_canvasPan.X, _canvasPan.Y));
        DevelopPreview.RenderTransform = group;
        RenderOptions.SetBitmapScalingMode(DevelopPreview,
            _canvasZoom >= 6 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void UpdateCanvasZoomStatus()
    {
        if (StatusText != null && _canvasZoom > 0)
            StatusText.Text = $"Aetherlight • Zoom {_canvasZoom * 100:0}% • Wheel to zoom • Middle-drag to pan";
    }

    private void DrawEnhancedMaskPreview()
    {
        OverlayCanvas.Children.Clear();
        Point a = _maskDrawStart;
        Point b = _maskDrawCurrent;
        var brush = new SolidColorBrush(Color.FromArgb(55, 255, 80, 80));
        var stroke = new SolidColorBrush(Color.FromArgb(150, 255, 100, 100));

        if (_canvasMaskMode == CanvasMaskMode.Brush)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = stroke, StrokeThickness = Math.Max(2, MaskSizeSlider.Value * 2),
                Opacity = 0.35
            };
            OverlayCanvas.Children.Add(line);
        }
        else if (_canvasMaskMode == CanvasMaskMode.Linear)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = stroke, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            };
            OverlayCanvas.Children.Add(line);
            AddMaskHandle(a); AddMaskHandle(b);
        }
        else
        {
            double radius = Math.Max(5, (b - a).Length);
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2, Height = radius * 2,
                Fill = brush, Stroke = stroke, StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, a.X - radius);
            Canvas.SetTop(ellipse, a.Y - radius);
            OverlayCanvas.Children.Add(ellipse);
            AddMaskHandle(a); AddMaskHandle(new Point(a.X + radius, a.Y));
        }
    }

    private void AddMaskHandle(Point p)
    {
        var handle = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1
        };
        Canvas.SetLeft(handle, p.X - 5);
        Canvas.SetTop(handle, p.Y - 5);
        OverlayCanvas.Children.Add(handle);
    }

    private void CommitEnhancedMaskStroke()
    {
        if (_originalPixels == null) return;
        _maskPoints.Clear();
        switch (_canvasMaskMode)
        {
            case CanvasMaskMode.Brush:
                RasterizeBrushStroke(_maskDrawStart, _maskDrawCurrent);
                break;
            case CanvasMaskMode.Linear:
                RasterizeLinearMask(_maskDrawStart, _maskDrawCurrent);
                break;
            case CanvasMaskMode.Radial:
                RasterizeRadialMask(_maskDrawStart, _maskDrawCurrent);
                break;
        }
        DrawMaskOverlay();
        ApplyAdjustments();
        StatusText.Text = $"Aetherlight • {_canvasMaskMode} mask • {_maskPoints.Count} samples";
    }

    private void RasterizeBrushStroke(Point startDisplay, Point endDisplay)
    {
        Point a = DisplayToSource(startDisplay), b = DisplayToSource(endDisplay);
        double distance = (b - a).Length;
        int count = Math.Max(1, (int)(distance / Math.Max(4, MaskSizeSlider.Value * 0.35)));
        for (int i = 0; i <= count; i++)
        {
            double t = i / (double)count;
            _maskPoints.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }
    }

    private void RasterizeLinearMask(Point startDisplay, Point endDisplay)
    {
        Point a = DisplayToSource(startDisplay), b = DisplayToSource(endDisplay);
        double distance = (b - a).Length;
        int count = Math.Max(8, (int)(distance / Math.Max(8, MaskSizeSlider.Value * 0.5)));
        for (int i = 0; i <= count; i++)
        {
            double t = i / (double)count;
            _maskPoints.Add(new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }
    }

    private void RasterizeRadialMask(Point centerDisplay, Point edgeDisplay)
    {
        Point center = DisplayToSource(centerDisplay), edge = DisplayToSource(edgeDisplay);
        double radius = Math.Max(8, (edge - center).Length);
        double spacing = Math.Max(8, MaskSizeSlider.Value * 0.65);
        int rings = Math.Max(2, (int)(radius / spacing));
        int samples = 48;
        for (int ring = 0; ring <= rings; ring++)
        {
            double r = radius * ring / rings;
            for (int i = 0; i < samples; i++)
            {
                double angle = i * Math.PI * 2 / samples;
                _maskPoints.Add(new Point(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r));
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dependencyObject) where T : DependencyObject
    {
        if (dependencyObject == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(dependencyObject, i);
            if (child is T typed) yield return typed;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
