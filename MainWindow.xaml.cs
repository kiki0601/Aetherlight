using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
namespace Aetherlight;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }
    private void Library_Click(object sender, RoutedEventArgs e) { }
    private void Develop_Click(object sender, RoutedEventArgs e) { }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Photos|*.cr3;*.arw;*.raf;*.dng;*.tif;*.tiff;*.jpg;*.jpeg;*.png|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        Filmstrip.Children.Clear();
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var img = new BitmapImage(); img.BeginInit(); img.UriSource = new Uri(path); img.DecodePixelWidth = 180; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
                var thumb = new Image { Source = img, Width = 150, Height = 105, Stretch = System.Windows.Media.Stretch.UniformToFill, Margin = new Thickness(4) };
                thumb.MouseLeftButtonUp += (_, _) => LoadPreview(path);
                Filmstrip.Children.Add(thumb);
            }
            catch { }
        }
    }
    private void LoadPreview(string path)
    {
        try { var img = new BitmapImage(new Uri(path)); Preview.Source = img; Preview.Visibility = Visibility.Visible; EmptyHint.Visibility = Visibility.Collapsed; } catch { }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Preview.Source is not BitmapSource source) return;
        var dlg = new SaveFileDialog { Filter = "JPEG|*.jpg|PNG|*.png|TIFF|*.tif", FileName = "Aetherlight Export.jpg" };
        if (dlg.ShowDialog() != true) return;
        BitmapEncoder encoder = Path.GetExtension(dlg.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) ? new PngBitmapEncoder() : new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source)); using var stream = File.Create(dlg.FileName); encoder.Save(stream);
    }
}
