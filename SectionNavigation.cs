using System.Windows;
using System.Windows.Controls;

namespace Aetherlight;

public partial class MainWindow
{
    private void SectionNav_Click(object sender, RoutedEventArgs e)
    {
        string tag = (sender as Button)?.Tag?.ToString() ?? string.Empty;
        Expander? target = tag switch
        {
            "Basic" => BasicSection,
            "Curves" => CurvesSection,
            "Color" => ColorSection,
            "Detail" => DetailSection,
            "Effects" => EffectsSection,
            _ => null
        };
        if (target == null) return;
        target.IsExpanded = true;
        target.BringIntoView();
    }
}
