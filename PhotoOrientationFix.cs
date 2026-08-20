using Sdcb.LibRaw;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace Aetherlight;

/// <summary>
/// Keeps camera orientation in the pixel pipeline instead of relying on WPF to
/// interpret EXIF/RAW orientation. This is important because editing coordinates,
/// masks and crops must all operate on the correctly oriented bitmap.
/// </summary>
internal static class PhotoOrientationFix
{
    private static readonly Dictionary<MainWindow, string?> AppliedPaths = new();

    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded),
            true);

        EventManager.RegisterClassHandler(
            typeof(Button),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnButtonClick),
            true);

        EventManager.RegisterClassHandler(
            typeof(Image),
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnImageClick),
            true);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyCurrentPhoto(window);
            OrientFilmstripThumbnails(window);
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !string.Equals(button.Content?.ToString(), "Import", StringComparison.OrdinalIgnoreCase)) return;
        if (Window.GetWindow(button) is not MainWindow window) return;

        // Import_Click opens the dialog and populates the filmstrip first. Run after
        // that handler has completed so the newly selected photo is available.
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplyCurrentPhoto(window);
            OrientFilmstripThumbnails(window);
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image || Window.GetWindow(image) is not MainWindow window) return;
        if (!image.IsDescendantOf(window.Filmstrip)) return;

        // The existing thumbnail handler selects the file on MouseLeftButtonUp.
        // Defer orientation until that handler has updated _currentPhotoPath/_originalSource.
        window.Dispatcher.BeginInvoke(new Action(() => ApplyCurrentPhoto(window)),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static void OrientFilmstripThumbnails(MainWindow window)
    {
        foreach (Image image in FindVisualChildren<Image>(window.Filmstrip))
        {
            if (image.Source is not BitmapSource source) continue;
            int orientation = GetBitmapOrientation(source);
            if (orientation != 1)
                image.Source = Transform(source, orientation);
        }
    }

    private static void ApplyCurrentPhoto(MainWindow window)
    {
        string? path = window._currentPhotoPath;
        if (string.IsNullOrWhiteSpace(path) || window._originalSource == null) return;
        if (string.Equals(AppliedPaths.GetValueOrDefault(window), path, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            int orientation = GetOrientation(path, window._originalSource);
            AppliedPaths[window] = path;

            if (orientation == 1) return;

            BitmapSource oriented = Transform(window._originalSource, orientation);
            window._originalSource = oriented;
            window.RefreshBasePixels();
            window.ResetAdjustments();
            window.ApplyAdjustments();
            window.Preview.Source = window._editedBitmap;
            window.DevelopPreview.Source = window._editedBitmap;
            window.DevelopEmpty.Visibility = Visibility.Collapsed;
            window.StatusText.Text = $"Aetherlight • {System.IO.Path.GetFileName(path)} • Orientation corrected";
        }
        catch
        {
            // Orientation metadata is optional. A failed metadata read must never
            // prevent the photo from opening.
            AppliedPaths[window] = path;
        }
    }

    private static int GetOrientation(string path, BitmapSource currentSource)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".cr3" or ".cr2" or ".arw" or ".raf" or ".dng" or ".nef" or ".nrw" or ".orf" or ".rw2" or ".pef" or ".srw")
        {
            int flip = GetLibRawFlip(path);
            if (flip == 0) return 1;

            // LibRaw's processed width/height already account for rotated output
            // on some versions/configurations. Only apply the flip ourselves when
            // the decoded bitmap is still in sensor orientation.
            if (flip is 5 or 6)
            {
                using RawContext raw = RawContext.OpenFile(path);
                bool alreadyRotated = raw.Width == currentSource.PixelWidth && raw.Height == currentSource.PixelHeight;
                if (alreadyRotated) return 1;
            }

            return flip switch
            {
                3 => 3,
                5 => 8, // LibRaw 5 = 90° CCW, EXIF 8 = 270° CW.
                6 => 6, // LibRaw 6 = 90° CW.
                _ => 1
            };
        }

        return GetRasterOrientation(path);
    }

    private static int GetLibRawFlip(string path)
    {
        using RawContext raw = RawContext.OpenFile(path);

        // RawContext intentionally exposes the high-level metadata but not the
        // native sizes.flip field. Read the public wrapper's internal RawData
        // structure reflectively so this fix remains compatible with Sdcb.LibRaw
        // releases without depending on an internal type in our project.
        PropertyInfo? rawDataProperty = typeof(RawContext).GetProperty(
            "RawData", BindingFlags.Instance | BindingFlags.NonPublic);
        object? rawData = rawDataProperty?.GetValue(raw);
        if (rawData == null) return 0;

        object? sizes = GetMember(rawData, "Sizes");
        object? flip = sizes == null ? null : GetMember(sizes, "Flip");
        return flip is null ? 0 : Convert.ToInt32(flip, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static object? GetMember(object instance, string name)
    {
        Type type = instance.GetType();
        FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) return field.GetValue(instance);
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance);
    }

    private static int GetRasterOrientation(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (frame.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation"))
            {
                object? value = metadata.GetQuery("System.Photo.Orientation");
                if (value is ushort u) return NormalizeExifOrientation(u);
                if (value is short s) return NormalizeExifOrientation((ushort)s);
                if (value is uint ui) return NormalizeExifOrientation((ushort)ui);
                if (value is int i) return NormalizeExifOrientation((ushort)i);
            }
        }
        catch
        {
            // Some formats/codecs do not expose EXIF through WIC. The image can
            // still be opened normally, so leave it unmodified in that case.
        }
        return 1;
    }

    private static int GetBitmapOrientation(BitmapSource source)
    {
        try
        {
            if (source.Metadata is BitmapMetadata metadata && metadata.ContainsQuery("System.Photo.Orientation"))
            {
                object? value = metadata.GetQuery("System.Photo.Orientation");
                if (value is ushort u) return NormalizeExifOrientation(u);
                if (value is short s) return NormalizeExifOrientation((ushort)s);
                if (value is uint ui) return NormalizeExifOrientation((ushort)ui);
                if (value is int i) return NormalizeExifOrientation((ushort)i);
            }
        }
        catch { }
        return 1;
    }

    private static int NormalizeExifOrientation(ushort value) => value is >= 1 and <= 8 ? value : 1;

    private static BitmapSource Transform(BitmapSource source, int orientation)
    {
        Transform transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1, source.PixelWidth / 2.0, source.PixelHeight / 2.0),
            3 => new RotateTransform(180, source.PixelWidth / 2.0, source.PixelHeight / 2.0),
            4 => new ScaleTransform(1, -1, source.PixelWidth / 2.0, source.PixelHeight / 2.0),
            5 => new MatrixTransform(new Matrix(0, 1, 1, 0, 0, 0)),
            6 => new RotateTransform(90, source.PixelWidth / 2.0, source.PixelHeight / 2.0),
            7 => new MatrixTransform(new Matrix(0, -1, -1, 0, source.PixelWidth, source.PixelHeight)),
            8 => new RotateTransform(270, source.PixelWidth / 2.0, source.PixelHeight / 2.0),
            _ => Transform.Identity
        };

        if (orientation is 5 or 7)
        {
            // The EXIF 5/7 cases combine a mirror and a 90° rotation. A simple
            // MatrixTransform needs the output translation explicitly.
            Matrix matrix = orientation == 5
                ? new Matrix(0, 1, 1, 0, 0, 0)
                : new Matrix(0, -1, -1, 0, source.PixelWidth, source.PixelHeight);
            transform = new MatrixTransform(matrix);
        }

        var result = new TransformedBitmap();
        result.BeginInit();
        result.Source = source;
        result.Transform = transform;
        result.EndInit();
        result.Freeze();
        return result;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
