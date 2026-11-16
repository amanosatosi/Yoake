using Avalonia;
using Avalonia.Media;

namespace Yoake.UI;

// Interface typography only. Subtitle styles and renderer font selection are independent.
public static class UiFontResources
{
    public static void Apply(string? family, double? size)
    {
        if (Application.Current is not { } app) return;
        app.Resources["YoakeUiFontFamily"] = string.IsNullOrWhiteSpace(family) ? FontFamily.Default : new FontFamily(family);
        app.Resources["YoakeUiFontSize"] = size ?? 12d;
    }
}
