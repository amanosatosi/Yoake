namespace Yoake.Core.Media;

/// <summary>Document-owned presentation only. All lengths here are physical pixels;
/// Layout returns the single logical-pixel rectangle used by image and visual tools.</summary>
public sealed class VideoViewState
{
    public double? WindowZoom { get; private set; }
    public double EffectiveWindowZoom { get; private set; } = 1;
    public double ContentZoom { get; private set; } = 1;
    public double PanX { get; private set; }
    public double PanY { get; private set; }
    private bool _detached, _preserveHostSize;
    public bool Detached { get => _detached; set { if (_detached != value) { _detached = value; _preserveHostSize = value; } } }
    private double _baseHeight, _contentHeight, _viewportWidth, _viewportHeight, _dpi = 1;
    private bool _windowZoomChanged;
    public bool ContentActive => ContentZoom != 1 || PanX != 0 || PanY != 0;
    public static double ClampZoom(double value) => double.IsFinite(value) && value > 0
        ? Math.Clamp(value, .125, 10) : throw new ArgumentException("Zoom must be a finite positive percentage.");

    public void SetWindowZoom(double value)
    {
        value = ClampZoom(value);
        var ratio = value / EffectiveWindowZoom;
        PanX *= ratio; PanY *= ratio;
        WindowZoom = EffectiveWindowZoom = value;
        _baseHeight = 0;
        _windowZoomChanged = true;
    }
    public void ResetContent() { PanX = PanY = 0; ContentZoom = 1; _contentHeight = 0; }
    public void Pan(double logicalX, double logicalY)
    {
        if (!double.IsFinite(logicalX) || !double.IsFinite(logicalY)) throw new ArgumentException("Pan must be finite.");
        PanX += logicalX * _dpi; PanY += logicalY * _dpi; ClampPan();
    }
    public void RestorePan(double x, double y) { PanX = x; PanY = y; ClampPan(); }
    public void ZoomContent(double value, double logicalX, double logicalY)
    {
        value = ClampZoom(value);
        var anchorX = (logicalX * _dpi - _viewportWidth / 2 - PanX) / ContentZoom;
        var anchorY = (logicalY * _dpi - _viewportHeight / 2 - PanY) / ContentZoom;
        PanX = logicalX * _dpi - _viewportWidth / 2 - anchorX * value;
        PanY = logicalY * _dpi - _viewportHeight / 2 - anchorY * value;
        _contentWidth *= value / ContentZoom;
        ContentZoom = value; _contentHeight = _baseHeight * value; ClampPan();
    }
    private void ClampPan()
    {
        if (_viewportHeight <= 0 || _baseHeight <= 0) return;
        var maxX = .5 * _contentWidth + .4 * _viewportWidth;
        var maxY = .5 * _baseHeight * ContentZoom + .4 * _viewportHeight;
        PanX = Math.Clamp(PanX, -maxX, maxX); PanY = Math.Clamp(PanY, -maxY, maxY);
    }
    private double _contentWidth;
    public VideoPresentation Layout(double logicalWidth, double logicalHeight, double dpi, double sourceHeight, double aspect)
    {
        if (logicalWidth <= 0 || logicalHeight <= 0 || sourceHeight <= 0 || aspect <= 0 || !double.IsFinite(dpi) || dpi <= 0) return default;
        var width = logicalWidth * dpi; var height = logicalHeight * dpi;
        if (_viewportHeight > 0 && dpi != _dpi)
        {
            var ratio = dpi / _dpi;
            if (WindowZoom is { } zoom) WindowZoom = ClampZoom(zoom * ratio);
            PanX *= ratio; PanY *= ratio; _contentHeight *= ratio;
        }
        var resized = width != _viewportWidth || height != _viewportHeight;
        var baseHeight = Detached || WindowZoom is null ? Math.Min(height, width / aspect) : sourceHeight * WindowZoom.Value;
        if (Detached && !_windowZoomChanged && (resized && ContentActive || _preserveHostSize) && _contentHeight > 0) ContentZoom = _contentHeight / baseHeight;
        _preserveHostSize = false;
        _windowZoomChanged = false;
        _dpi = dpi; _viewportWidth = width; _viewportHeight = height; _baseHeight = baseHeight;
        EffectiveWindowZoom = baseHeight / sourceHeight;
        _contentHeight = baseHeight * ContentZoom; _contentWidth = _contentHeight * aspect;
        ClampPan();
        return new((width - _contentWidth) / 2 / dpi + PanX / dpi,
            (height - _contentHeight) / 2 / dpi + PanY / dpi, _contentWidth / dpi, _contentHeight / dpi);
    }
}
