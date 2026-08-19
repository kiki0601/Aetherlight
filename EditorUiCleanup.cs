using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Aetherlight;

internal static class EditorUiCleanup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => RemoveDuplicateValueBoxes(window)));
    }

    private static void RemoveDuplicateValueBoxes(MainWindow window)
    {
        foreach (Slider slider in FindVisualChildren<Slider>(window).ToList())
        {
            if (slider.Parent is not Grid wrapper || wrapper.Children.Count != 2) continue;
            TextBox? generatedValue = wrapper.Children.OfType<TextBox>().FirstOrDefault();
            if (generatedValue == null || generatedValue.Tag?.ToString() != slider.Name) continue;
            if (wrapper.Parent is not Panel panel) continue;

            int index = panel.Children.IndexOf(wrapper);
            if (index <= 0 || panel.Children[index - 1] is not Grid labelGrid) continue;
            if (!labelGrid.Children.OfType<TextBox>().Any()) continue;

            // This slider already has an original XAML value box. Restore the slider
            // to its original StackPanel position and discard only the generated box.
            wrapper.Children.Remove(slider);
            wrapper.Children.Remove(generatedValue);
            panel.Children.Remove(wrapper);
            slider.Margin = new Thickness(0, 2, 0, 9);
            panel.Children.Insert(index, slider);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
