using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aetherlight;

/// <summary>
/// Lightweight canvas state used by the functional UI repair layer.
/// Zoom/mask event registration is centralized in FunctionalUiFixes.cs so the
/// editor startup path cannot be interrupted by a fragile dynamic UI pass.
/// </summary>
public partial class MainWindow
{
    private double _canvasZoom = 1.0;
    private Vector _canvasPan;
    private bool _panningCanvas;
    private Point _panStartMouse;
    private Vector _panStartOffset;

    private void ChangeCanvasZoom(double factor)
    {
        if (_editedBitmap == null && _originalSource == null) return;
        _canvasZoom = Math.Clamp(_canvasZoom * factor, 0.1, 16.0);
        ApplyCanvasTransform();
        UpdateCanvasZoomStatus();
    }

    private void ApplyCanvasTransform()
    {
        if (DevelopPreview == null) return;
        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(_canvasZoom, _canvasZoom));
        group.Children.Add(new TranslateTransform(_canvasPan.X, _canvasPan.Y));
        DevelopPreview.RenderTransformOrigin = new Point(.5, .5);
        DevelopPreview.RenderTransform = group;
        RenderOptions.SetBitmapScalingMode(
            DevelopPreview,
            _canvasZoom >= 6 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    private void UpdateCanvasZoomStatus()
    {
        if (StatusText != null)
            StatusText.Text = $"Aetherlight • Zoom {_canvasZoom * 100:0}% • Wheel to zoom • Middle-drag to pan";
    }
}
