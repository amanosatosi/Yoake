using Avalonia;
using Avalonia.Media;

namespace Yoake.UI;

// Interface typography only. Subtitle styles and renderer font selection are independent.
public static class UiFontResources
{
    public static void Apply(string? family, double? size)
    {
        if (Application.Current is not { } app) return;
        try { app.Resources["YoakeUiFontFamily"] = string.IsNullOrWhiteSpace(family) ? FontFamily.Default : new FontFamily(family); }
        catch(ArgumentException) { app.Resources["YoakeUiFontFamily"] = FontFamily.Default; }
        var actualSize = size is {} value && double.IsFinite(value) ? Math.Clamp(value, 9, 24) : 12d;
        app.Resources["YoakeUiFontSize"] = actualSize;
        app.Resources["YoakeUiFontSizeSmall"] = actualSize * 11 / 12;
        app.Resources["YoakeUiFontSizeTiny"] = actualSize * 10 / 12;
        app.Resources["YoakeUiRowHeight"] = Math.Max(26, Math.Ceiling(actualSize * 1.8));
        app.Resources["YoakeUiTabContentHeight"] = Math.Max(30, Math.Ceiling(actualSize * 1.8));
    }
}
