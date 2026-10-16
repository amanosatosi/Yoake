using System.Globalization;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Media;

public enum VideoAspectKind { Default, Full, Wide, Cinematic, Custom }
public sealed record VideoAspect(VideoAspectKind Kind = VideoAspectKind.Default, double Custom = 1)
{
    public double Ratio(double original) => Kind switch
    {
        VideoAspectKind.Default => original, VideoAspectKind.Full => 4d / 3, VideoAspectKind.Wide => 16d / 9,
        VideoAspectKind.Cinematic => 2.35, VideoAspectKind.Custom => Custom, _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public static bool TryCustom(string? text, out VideoAspect aspect)
    {
        aspect = new(); var value = (text ?? "").Trim();
        bool Number(string part, out double result) => double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);
        if (!Number(value, out var ratio))
        {
            var parts = value.Split([':', '/', 'x', 'X']);
            if (parts.Length != 2 || !Number(parts[0], out var numerator) || !Number(parts[1], out var denominator) || numerator <= 0 || denominator <= 0) return false;
            ratio = numerator / denominator;
        }
        if (!double.IsFinite(ratio) || ratio is < 0.5 or > 5) return false;
        aspect = new(VideoAspectKind.Custom, ratio); return true;
    }
}

/// <summary>Maps logical desktop coordinates to script coordinates through one letterboxed viewport.</summary>
public readonly record struct VideoPresentation(double X, double Y, double Width, double Height)
{
    public static VideoPresentation Fit(double viewportWidth, double viewportHeight, double ratio)
    {
        if (!double.IsFinite(ratio) || ratio <= 0 || viewportWidth <= 0 || viewportHeight <= 0) return default;
        var width = Math.Min(viewportWidth, viewportHeight * ratio); var height = width / ratio;
        return new((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
    }
    public bool Contains(double x, double y) => Width > 0 && Height > 0 && x >= X && y >= Y && x <= X + Width && y <= Y + Height;
    public AssPoint ToScript(double x, double y, double scriptWidth, double scriptHeight) =>
        new((x - X) / Width * scriptWidth, (y - Y) / Height * scriptHeight);
    public AssPoint FromScript(AssPoint point, double scriptWidth, double scriptHeight) =>
        new(X + point.X / scriptWidth * Width, Y + point.Y / scriptHeight * Height);
    public static string Coordinates(AssPoint point) => point.X.ToString("0.###", CultureInfo.InvariantCulture) + "," + point.Y.ToString("0.###", CultureInfo.InvariantCulture);
    public static IReadOnlyList<(double Horizontal, double Vertical)> Overscan(double aspect) =>
        aspect > 1.75 ? [(0.1, 0.05), (0.035, 0.035)] : [(0.067, 0.05), (0.033, 0.035)];
}
