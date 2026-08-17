using System.Windows;

namespace Aetherlight;

public partial class MainWindow
{
    private void Window_Loaded(object sender, RoutedEventArgs e)
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
        CropAngleSlider.ValueChanged += CropAngle_ValueChanged;
        MaskExposureSlider.ValueChanged += MaskExposure_ValueChanged;
        UpdateValueLabels();
        DrawHistogram();
    }
}
