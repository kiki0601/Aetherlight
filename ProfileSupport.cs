using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sdcb.LibRaw;

namespace Aetherlight;

public partial class MainWindow
{
    private string _cameraName = "Camera";
    private string _selectedProfile = "Camera Standard";
    private bool _profileSupportReady;
    private bool _profileSliderCaptured;

    private void InitializeProfileSupport()
    {
        if (_profileSupportReady) return;
        _profileSupportReady = true;
        DevelopView.IsVisibleChanged += DevelopView_IsVisibleChanged;

        foreach (var slider in new[]
        {
            ExposureSlider, ContrastSlider, HighlightsSlider, ShadowsSlider,
            WhitesSlider, BlacksSlider, TemperatureSlider, TintSlider,
            VibranceSlider, SaturationSlider
        })
        {
            slider.GotMouseCapture += Slider_GotMouseCapture;
            slider.LostMouseCapture += Slider_LostMouseCapture;
            slider.ValueChanged += ProfileAwareSliderChanged;
        }
    }

    private void DevelopView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DevelopView.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(_currentPhotoPath))
            LoadCameraName(_currentPhotoPath);
    }

    private void LoadCameraName(string path)
    {
        try
        {
            _cameraName = "Camera";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".cr3" or ".cr2" or ".arw" or ".raf" or ".dng" or ".nef" or ".nrw" or ".orf" or ".rw2" or ".pef" or ".srw")
            {
                using var raw = RawContext.OpenFile(path);
                var info = raw.ImageParams;
                _cameraName = string.IsNullOrWhiteSpace(info.Model) ? info.Make : $"{info.Make} {info.Model}".Trim();
            }
            ProfileValue.Text = _selectedProfile;
            ProfileButton.Content = _selectedProfile;
        }
        catch
        {
            _cameraName = "Camera";
        }
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_originalSource == null) return;
        ApplyAdjustments();
        var source = _editedBitmap ?? _originalSource;
        var dialog = new ProfileBrowserWindow(source, _cameraName, BuildProfiles(), _selectedProfile) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _selectedProfile = dialog.SelectedProfile;
            ApplySelectedProfileOverlay();
            ProfileValue.Text = _selectedProfile;
            ProfileButton.Content = _selectedProfile;
            StatusText.Text = $"Aetherlight • Profile • {_selectedProfile}";
        }
    }

    private IReadOnlyList<ProfileDefinition> BuildProfiles()
    {
        var list = new List<ProfileDefinition>
        {
            new("Adobe Color", "Adobe Raw"),
            new("Adobe Standard", "Adobe Raw"),
            new("Adobe Landscape", "Adobe Raw"),
            new("Adobe Portrait", "Adobe Raw"),
            new("Adobe Neutral", "Adobe Raw"),
            new("Adobe Monochrome", "Adobe Raw")
        };
        string make = _cameraName.ToLowerInvariant();
        if (make.Contains("canon") || make.Contains("nikon") || make.Contains("pentax") || make.Contains("leica") || make.Contains("sony") || make.Contains("fujifilm"))
        {
            list.Add(new("Camera Standard", "Camera Matching"));
            list.Add(new("Camera Faithful", "Camera Matching"));
            list.Add(new("Camera Landscape", "Camera Matching"));
            list.Add(new("Camera Neutral", "Camera Matching"));
            list.Add(new("Camera Portrait", "Camera Matching"));
            list.Add(new("Camera Monochrome", "Camera Matching"));
        }
        return list;
    }

    private void Slider_GotMouseCapture(object sender, MouseEventArgs e) => _profileSliderCaptured = true;

    private void Slider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _profileSliderCaptured = false;
        if (_selectedProfile != "Camera Standard" && _selectedProfile != "Adobe Standard")
            ApplySelectedProfileOverlay();
    }

    private void ProfileAwareSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _originalPixels == null || _profileSliderCaptured) return;
        if (_selectedProfile != "Camera Standard" && _selectedProfile != "Adobe Standard")
            Dispatcher.BeginInvoke(new Action(ApplySelectedProfileOverlay), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ApplySelectedProfileOverlay()
    {
        if (_editedBitmap == null || _selectedProfile is "Camera Standard" or "Adobe Standard") return;
        _renderVersion++;
        ApplyAdjustments();
        if (_editedBitmap == null) return;

        byte[] pixels = new byte[_pixelWidth * _pixelHeight * 4];
        _editedBitmap.CopyPixels(pixels, _pixelWidth * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double b = pixels[i] / 255.0, g = pixels[i + 1] / 255.0, r = pixels[i + 2] / 255.0;
            ProfileRenderer.Apply(_selectedProfile, ref r, ref g, ref b);
            pixels[i] = ToByte(b); pixels[i + 1] = ToByte(g); pixels[i + 2] = ToByte(r);
        }
        var profiled = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgra32, null);
        profiled.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, _pixelWidth * 4, 0);
        profiled.Freeze();
        _editedBitmap = profiled;
        Preview.Source = _editedBitmap;
        DevelopPreview.Source = _editedBitmap;
        DrawHistogram();
    }
}
