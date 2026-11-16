using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;

namespace Yoake.UI;

// Interface typography only. Subtitle styles and renderer font selection are independent.
public static class UiFontResources
{
    public static void Apply(string? family, double? size)
    {
        if (Application.Current is not { } app) return;
        try { app.Resources["YoakeUiFontFamily"] = string.IsNullOrWhiteSpace(family) ? FontFamily.Default : new FontFamily(family); }
        catch(Exception error) when(error is ArgumentException or FormatException) { app.Resources["YoakeUiFontFamily"] = FontFamily.Default; }
        var actualSize = size is {} value && double.IsFinite(value) ? Math.Clamp(value, 9, 24) : 12d;
        app.Resources["YoakeUiFontSize"] = actualSize;
        app.Resources["YoakeUiEditorFontSize"] = actualSize*14/12;
        app.Resources["YoakeUiFontSizeSmall"] = actualSize * 11 / 12;
        app.Resources["YoakeUiFontSizeTiny"] = actualSize * 10 / 12;
        app.Resources["YoakeUiCompactButtonHeight"] = Math.Max(24,Math.Ceiling(actualSize*1.6));
        app.Resources["YoakeUiRowHeight"] = Math.Max(26, Math.Ceiling(actualSize * 1.8));
        app.Resources["YoakeUiTabContentHeight"] = Math.Max(30, Math.Ceiling(actualSize * 1.8));
        app.Resources["YoakeUiGridHeaderHeight"] = new GridLength(Math.Max(24, Math.Ceiling(actualSize * 1.8)));
        app.Resources["YoakeUiTimeFieldWidth"] = new GridLength(96*actualSize/12);
        app.Resources["YoakeUiDurationFieldWidth"] = new GridLength(72*actualSize/12);
        var metadataScale=Math.Clamp(actualSize/12,1,1.5);
        app.Resources["YoakeUiStyleGroupWidth"] = 240*metadataScale;
        app.Resources["YoakeUiOtherGroupWidth"] = 336*metadataScale;
    }
}
