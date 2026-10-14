namespace Yoake.Core.Media;

/// <summary>
/// Size the embedded video canvas before applying content zoom or panning.
/// Aegisub's window zoom changes the viewport requested by the video, whereas
/// content zoom/pan subsequently transform the image inside that viewport.
/// When the workspace cannot give the video its full requested size, keep the
/// requested image scale and crop within the available viewport (do not fit
/// the image down and silently change the chosen zoom percentage).
/// All dimensions returned here are logical Avalonia pixels.
/// </summary>
public static class EmbeddedVideoSizing
{
    /// <summary>
    /// Reserve a useful 16:9 editing canvas before media metadata exists.
    /// Do not change the workspace's star/splitter column: without a video,
    /// there is no source-relative window zoom to request a new column width.
    /// </summary>
    public static VideoPresentation Placeholder(double availableWidth, double availableHeight)
    {
        if (!double.IsFinite(availableWidth) || !double.IsFinite(availableHeight) ||
            availableWidth <= 0 || availableHeight <= 0)
            return default;
        var height = Math.Min(availableHeight, availableWidth * 9 / 16);
        return new(0, 0, height * 16 / 9, height);
    }

    public static VideoPresentation Viewport(
        double availableWidth, double availableHeight, double sourceHeight,
        double aspect, double dpi, double? windowZoom)
    {
        if (!double.IsFinite(availableWidth) || !double.IsFinite(availableHeight) ||
            !double.IsFinite(sourceHeight) || !double.IsFinite(aspect) ||
            !double.IsFinite(dpi) || availableWidth <= 0 || availableHeight <= 0 ||
            sourceHeight <= 0 || aspect <= 0 || dpi <= 0)
            return default;

        if (windowZoom is null)
        {
            // Until the user chooses a window zoom, fit *the viewport*, not
            // an image in a permanently stretched black rectangle.
            var height = Math.Min(availableHeight, availableWidth / aspect);
            return new(0, 0, height * aspect, height);
        }

        var requestedHeight = sourceHeight * VideoViewState.ClampZoom(windowZoom.Value) / dpi;
        return new(0, 0,
            Math.Min(availableWidth, requestedHeight * aspect),
            Math.Min(availableHeight, requestedHeight));
    }
}
