using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aetherlight;

public partial class ProfileBrowserWindow : Window
{
    private readonly BitmapSource _source;
    private readonly IReadOnlyList<ProfileDefinition> _profiles;
    private string _filter = "ALL";
    public string SelectedProfile { get; private set; }

    public ProfileBrowserWindow(BitmapSource source, string cameraName, IReadOnlyList<ProfileDefinition> profiles, string selectedProfile)
    {
        InitializeComponent();
        _source = source;
        _profiles = profiles;
        SelectedProfile = selectedProfile;
        CameraText.Text = string.IsNullOrWhiteSpace(cameraName) ? "Camera Matching" : cameraName;
        RenderProfiles();
    }

    private void RenderProfiles()
    {
        ProfileGrid.Children.Clear();
        foreach (var profile in _profiles)
        {
            if (_filter == "COLOR" && profile.Name.Contains("Monochrome", StringComparison.OrdinalIgnoreCase)) continue;
            if (_filter == "B&W" && !profile.Name.Contains("Monochrome", StringComparison.OrdinalIgnoreCase)) continue;

            var image = new Image
            {
                Source = ProfileRenderer.Render(_source, profile, 190),
                Width = 190,
                Height = 125,
                Stretch = Stretch.UniformToFill
            };
            var name = new TextBlock
            {
                Text = profile.Name,
                Margin = new Thickness(10, 8, 10, 2),
                FontWeight = profile.Name == SelectedProfile ? FontWeights.SemiBold : FontWeights.Normal
            };
            var group = new TextBlock
            {
                Text = profile.Group,
                Margin = new Thickness(10, 0, 10, 10),
                Foreground = new SolidColorBrush(Color.FromRgb(145, 145, 145)),
                FontSize = 11
            };
            var panel = new StackPanel();
            panel.Children.Add(image);
            panel.Children.Add(name);
            panel.Children.Add(group);

            var button = new Button
            {
                Content = panel,
                Width = 208,
                Margin = new Thickness(5),
                Padding = new Thickness(3),
                BorderThickness = new Thickness(profile.Name == SelectedProfile ? 2 : 1),
                BorderBrush = new SolidColorBrush(profile.Name == SelectedProfile ? Color.FromRgb(217, 164, 65) : Color.FromRgb(55, 55, 55)),
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 28)),
                ToolTip = $"Apply {profile.Name}"
            };
            button.Click += (_, _) =>
            {
                SelectedProfile = profile.Name;
                DialogResult = true;
            };
            ProfileGrid.Children.Add(button);
        }
    }

    private void All_Click(object sender, RoutedEventArgs e) { _filter = "ALL"; RenderProfiles(); }
    private void Color_Click(object sender, RoutedEventArgs e) { _filter = "COLOR"; RenderProfiles(); }
    private void Bw_Click(object sender, RoutedEventArgs e) { _filter = "B&W"; RenderProfiles(); }
    private void Close_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
