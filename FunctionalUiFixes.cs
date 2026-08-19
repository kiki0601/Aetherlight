using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Aetherlight;

/// <summary>
/// Stable runtime UI layer for precise controls, masks and canvas navigation.
/// Keeping this separate from the XAML avoids fragile startup-time tree surgery.
/// </summary>
internal static class FunctionalUiFixes
{
    private static readonly Dictionary<Slider, TextBox> ValueBoxes = new();
    private static readonly Dictionary<TextBox, Slider> BoxSliders = new();
    private static bool _registered;

    [ModuleInitializer]
    internal static void Register()
    {
        if (_registered) return;
        _registered = true;

        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.MouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseDown), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.MouseMoveEvent,
            new MouseEventHandler(OnPreviewMouseMove), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnPreviewMouseUp), true);
        EventManager.RegisterClassHandler(typeof(Image), UIElement.MouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewWheel), true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;
        if (window.DevelopView != null)
        {
            window.DevelopView.IsVisibleChanged -= DevelopView_IsVisibleChanged;
            window.DevelopView.IsVisibleChanged += DevelopView_IsVisibleChanged;
        }
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => Repair(window)));
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Repair(window)));
    }

    private static void DevelopView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Grid view && view.IsVisible && Window.GetWindow(view) is MainWindow window)
            window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Repair(window)));
    }

    private static void Repair(MainWindow window)
    {
        try
        {
            AddNumericControls(window);
            AddColorGradingReadout(window);
            RepairZoomViewport(window);
            SyncOverlayTransform(window);
        }
        catch
        {
            // UI repair must never prevent the main editor/render pipeline from starting.
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    private static void AddNumericControls(MainWindow window)
    {
        foreach (Slider slider in FindVisualChildren<Slider>(window).ToList())
        {
            if (ValueBoxes.ContainsKey(slider) || string.IsNullOrWhiteSpace(slider.Name)) continue;
            if (IsBasicSlider(slider)) continue;

            TextBox? existing = FindValueBoxNearSlider(slider);
            if (existing != null)
            {
                ValueBoxes[slider] = existing;
                BoxSliders[existing] = slider;
                existing.Text = Format(slider);
                continue;
            }

            var box = new TextBox
            {
                Width = 62,
                Height = 22,
                Margin = new Thickness(7, 0, 0, 0),
                Padding = new Thickness(1, 0, 1, 0),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gainsboro,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Text = Format(slider),
                Tag = slider.Name,
                ToolTip = "Click the value, type an exact number, then press Enter. Up/Down changes by the smallest useful step."
            };

            box.PreviewMouseLeftButtonDown += ValueBox_MouseDown;
            box.KeyDown += ValueBox_KeyDown;
            box.LostFocus += ValueBox_LostFocus;
            slider.ValueChanged += Slider_ValueChanged;
            ValueBoxes[slider] = box;
            BoxSliders[box] = slider;
            PlaceValueBox(slider, box);
        }
    }

    private static bool IsBasicSlider(Slider slider) => slider.Name is
        "ExposureSlider" or "ContrastSlider" or "HighlightsSlider" or "ShadowsSlider" or
        "WhitesSlider" or "BlacksSlider" or "TemperatureSlider" or "TintSlider" or
        "VibranceSlider" or "SaturationSlider";

    private static TextBox? FindValueBoxNearSlider(Slider slider)
    {
        if (slider.Parent is not Panel panel) return null;
        foreach (FrameworkElement child in panel.Children.OfType<FrameworkElement>())
        {
            if (child is TextBox tb && string.Equals(tb.Tag?.ToString(), slider.Name, StringComparison.OrdinalIgnoreCase))
                return tb;
            if (child is Grid grid)
            {
                TextBox? box = grid.Children.OfType<TextBox>().FirstOrDefault(tb =>
                    string.Equals(tb.Tag?.ToString(), slider.Name, StringComparison.OrdinalIgnoreCase));
                if (box != null) return box;
            }
        }
        return null;
    }

    private static void PlaceValueBox(Slider slider, TextBox box)
    {
        if (slider.Parent is Grid grid)
        {
            if (grid.ColumnDefinitions.Count == 0) grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (grid.ColumnDefinitions.Count == 1) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(slider, 0);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return;
        }

        if (slider.Parent is Panel panel)
        {
            int index = panel.Children.IndexOf(slider);
            panel.Children.RemoveAt(index);
            var wrapper = new Grid { Margin = slider.Margin };
            wrapper.ColumnDefinitions.Add(new ColumnDefinition());
            wrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            slider.Margin = new Thickness(0, 2, 0, 9);
            Grid.SetColumn(slider, 0);
            Grid.SetColumn(box, 1);
            wrapper.Children.Add(slider);
            wrapper.Children.Add(box);
            panel.Children.Insert(index, wrapper);
        }
    }

    private static string Format(Slider slider)
    {
        double value = slider.Value;
        if (slider.Name.Contains("Temperature", StringComparison.OrdinalIgnoreCase)) return $"{Math.Round(value):0} K";
        if (slider.Name.Contains("Exposure", StringComparison.OrdinalIgnoreCase)) return value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        if (slider.Name.Contains("Angle", StringComparison.OrdinalIgnoreCase)) return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°";
        if (Math.Abs(value - Math.Round(value)) < .0001) return value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }

    private static double Step(Slider slider)
    {
        if (slider.Name.Contains("Temperature", StringComparison.OrdinalIgnoreCase)) return 1;
        if (slider.Name.Contains("Exposure", StringComparison.OrdinalIgnoreCase)) return .01;
        if (slider.Name.Contains("Angle", StringComparison.OrdinalIgnoreCase)) return .1;
        return 1;
    }

    private static void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && ValueBoxes.TryGetValue(slider, out TextBox? box)) box.Text = Format(slider);
    }

    private static void ValueBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox box) box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(box.SelectAll));
    }

    private static void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || !BoxSliders.TryGetValue(box, out Slider? slider)) return;
        if (e.Key == Key.Enter)
        {
            Commit(box, slider);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = Format(slider);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            slider.Value = Math.Clamp(slider.Value + (e.Key == Key.Up ? Step(slider) : -Step(slider)), slider.Minimum, slider.Maximum);
            box.Text = Format(slider);
            box.SelectAll();
            e.Handled = true;
        }
    }

    private static void ValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && BoxSliders.TryGetValue(box, out Slider? slider)) Commit(box, slider);
    }

    private static void Commit(TextBox box, Slider slider)
    {
        string raw = box.Text.Replace("K", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("°", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double value))
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        box.Text = Format(slider);
    }

    private static void AddColorGradingReadout(MainWindow window)
    {
        Canvas? wheel = FindVisualChildren<Canvas>(window).FirstOrDefault(c => c.Name.Contains("ColorWheel", StringComparison.OrdinalIgnoreCase));
        if (wheel?.Parent is not Panel panel) return;
        if (panel.Children.OfType<FrameworkElement>().Any(x => x.Tag?.ToString() == "AetherlightGradeReadout")) return;

        var readout = new StackPanel { Tag = "AetherlightGradeReadout", Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 4) };
        readout.Children.Add(MakeGradeBox("H", "Hue in degrees"));
        readout.Children.Add(MakeGradeBox("S", "Saturation 0-1"));
        readout.Children.Add(MakeGradeBox("L", "Luma -1 to +1"));
        int index = panel.Children.IndexOf(wheel);
        panel.Children.Insert(Math.Min(index + 1, panel.Children.Count), readout);
    }

    private static TextBox MakeGradeBox(string label, string tooltip)
    {
        var box = new TextBox
        {
            Width = 62, Height = 22, Margin = new Thickness(3, 0, 3, 0), Text = "0",
            Foreground = Brushes.Gainsboro, Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)), HorizontalContentAlignment = HorizontalAlignment.Right,
            Tag = "Grade:" + label, ToolTip = tooltip
        };
        box.PreviewMouseLeftButtonDown += (_, _) => box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(box.SelectAll));
        box.KeyDown += GradeBox_KeyDown;
        return box;
    }

    private static void GradeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not string tag || !tag.StartsWith("Grade:", StringComparison.Ordinal) || e.Key != Key.Enter) return;
        if (!double.TryParse(box.Text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double value)) return;
        if (Window.GetWindow(box) is not MainWindow window) return;
        string kind = tag.Substring(6);
        if (kind == "H") window._gradeHue = value;
        else if (kind == "S") window._gradeSat = Math.Clamp(value, 0, 1);
        else window._gradeLuma = Math.Clamp(value, -1, 1);
        window.DrawColorWheel();
        window.StartFastRender();
        e.Handled = true;
    }

    private static void RepairZoomViewport(MainWindow window)
    {
        if (window.DevelopPreview == null) return;
        window.DevelopPreview.RenderTransformOrigin = new Point(.5, .5);
        DependencyObject? current = window.DevelopPreview;
        for (int i = 0; i < 5 && current != null; i++)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is FrameworkElement element) element.ClipToBounds = true;
        }
    }

    private static void OnPreviewWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        if (Window.GetWindow(image) is not MainWindow window) return;
        window.ChangeCanvasZoom(e.Delta > 0 ? 1.15 : 1.0 / 1.15);
        window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            SyncOverlayTransform(window);
            RepairZoomViewport(window);
        }));
        e.Handled = true;
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        if (Window.GetWindow(image) is not MainWindow window) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            window._panningCanvas = true;
            window._panStartMouse = e.GetPosition(image);
            window._panStartOffset = window._canvasPan;
            image.CaptureMouse();
            e.Handled = true;
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        if (Window.GetWindow(image) is not MainWindow window) return;
        if (window._panningCanvas && e.MiddleButton == MouseButtonState.Pressed)
        {
            Point now = e.GetPosition(image);
            window._canvasPan = window._panStartOffset + (now - window._panStartMouse);
            window.ApplyCanvasTransform();
            SyncOverlayTransform(window);
            e.Handled = true;
        }
    }

    private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || image.Name != "DevelopPreview") return;
        if (Window.GetWindow(image) is not MainWindow window) return;

        if (e.ChangedButton == MouseButton.Middle && window._panningCanvas)
        {
            window._panningCanvas = false;
            image.ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && window._toolMode == ToolMode.Mask)
        {
            window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                window.StartFastRender();
                SyncOverlayTransform(window);
            }));
        }
    }

    private static void SyncOverlayTransform(MainWindow window)
    {
        if (window.OverlayCanvas == null) return;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(window._canvasZoom, window._canvasZoom));
        group.Children.Add(new TranslateTransform(window._canvasPan.X, window._canvasPan.Y));
        window.OverlayCanvas.RenderTransformOrigin = new Point(.5, .5);
        window.OverlayCanvas.RenderTransform = group;
    }
}
