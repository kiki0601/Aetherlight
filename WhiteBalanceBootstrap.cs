using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Aetherlight;

public partial class MainWindow
{
    [ModuleInitializer]
    internal static void InstallWhiteBalanceSliderHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), RangeBase.ValueChangedEvent,
            new RoutedPropertyChangedEventHandler<double>(InterceptTemperatureSlider), handledEventsToo: false);
    }

    private static void InterceptTemperatureSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not MainWindow window || e.OriginalSource != window.TemperatureSlider)
            return;

        // The visual control stores a normalized logarithmic position. The image
        // renderer still expects a relative temperature delta, so translate the
        // position to Kelvin, render with the delta, then restore the visual value.
        if (window._loading || window._originalPixels == null || window._whiteBalanceSliderHandling)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        double visualPosition = window.TemperatureSlider.Value;
        double kelvin = 2000.0 * Math.Pow(50000.0 / 2000.0, Math.Clamp(visualPosition, 0, 1));
        double delta = kelvin - window._asShotTemperature;

        window._whiteBalanceSliderHandling = true;
        try
        {
            window.TemperatureSlider.Value = delta;
            window.ApplyAdjustments();
        }
        finally
        {
            window.TemperatureSlider.Value = visualPosition;
            window._whiteBalanceSliderHandling = false;
            window.UpdateWhiteBalanceLabels();
        }
    }
}
