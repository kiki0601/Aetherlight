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
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => RemoveDuplicateSliderValueBoxes(window)));
    }

    private static void RemoveDuplicateSliderValueBoxes(MainWindow window)
    {
        foreach (Slider slider in FindVisualChildren<Slider>(window).ToList())
        {
            if (slider.Parent is not Grid wrapper || wrapper.Children.Count != 2) continue;
            TextBox? valueBox = wrapper.Children.OfType<TextBox>().FirstOrDefault();
            if (valueBox == null || valueBox.Tag?.ToString() != slider.Name) continue;

            // A slider with an existing XAML value box has a sibling Grid immediately before it.
            if (wrapper.Parent is not Panel panel) continue;
            int index = panel.Children.IndexOf(wrapper);
            if (index <= 0 || panel.Children[index - 1] is not Grid labelGrid) continue;
            TextBox? existing = labelGrid.Children.OfType<TextBox>().FirstOrDefault();
            if (existing == null) continue;

            panel.Children.Remove(wrapper);
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
