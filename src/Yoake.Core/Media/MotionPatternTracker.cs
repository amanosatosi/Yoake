namespace Yoake.Core.Media;

public readonly record struct TrackingPoint(int Frame, double X, double Y, double Confidence = 1);
public readonly record struct TrackingRegion(double X, double Y, int Width, int Height, int SearchWidth, int SearchHeight)
{
    public void Validate(int width, int height)
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || Width < 8 || Height < 8 || Width > 256 || Height > 256 ||
            SearchWidth < Width + 4 || SearchHeight < Height + 4 || SearchWidth > 512 || SearchHeight > 512 ||
            X - Width / 2d < 0 || Y - Height / 2d < 0 || X + Width / 2d > width || Y + Height / 2d > height)
            throw new ArgumentException("Keep an 8–256 px pattern inside the video and a larger search area (up to 512 px).");
    }
}

// Bounded normalized cross-correlation against an actual reference-frame patch.
// Samples at most 24x24 pixels per candidate; exhaustive coarse search followed
// by full-resolution local refinement. No decoder, frame cache or clock.
public sealed class MotionPatternTracker
{
    private readonly (int X, int Y, double Value)[] _pattern;
    private readonly double _energy;
    private readonly TrackingRegion _region;
    public MotionPatternTracker(byte[] bgra, int width, int height, int stride, TrackingRegion region)
    {
        ValidateImage(bgra, width, height, stride); region.Validate(width, height); _region = region;
        var left = (int)Math.Round(region.X - region.Width / 2d); var top = (int)Math.Round(region.Y - region.Height / 2d);
        List<(int X, int Y, double Value)> values = [];
        var stepX = Math.Max(1, (region.Width + 23) / 24); var stepY = Math.Max(1, (region.Height + 23) / 24);
        for (var y = 0; y < region.Height; y += stepY)
            for (var x = 0; x < region.Width; x += stepX) values.Add((x, y, Gray(bgra, (top + y) * stride + (left + x) * 4)));
        var mean = values.Average(p => p.Value);
        _pattern = values.Select(p => (p.X, p.Y, p.Value - mean)).ToArray();
        _energy = _pattern.Sum(p => p.Value * p.Value);
        if (_energy / _pattern.Length < 4) throw new ArgumentException("The pattern has too little detail. Select a textured edge or object.");
    }
    public TrackingPoint Track(byte[] bgra, int width, int height, int stride, TrackingPoint previous, int frame, CancellationToken token = default)
    {
        ValidateImage(bgra, width, height, stride); token.ThrowIfCancellationRequested();
        var minX = Math.Max(0, (int)Math.Ceiling(previous.X - _region.SearchWidth / 2d));
        var minY = Math.Max(0, (int)Math.Ceiling(previous.Y - _region.SearchHeight / 2d));
        var maxX = Math.Min(width - _region.Width, (int)Math.Floor(previous.X + _region.SearchWidth / 2d - _region.Width));
        var maxY = Math.Min(height - _region.Height, (int)Math.Floor(previous.Y + _region.SearchHeight / 2d - _region.Height));
        if (minX > maxX || minY > maxY) return previous with { Frame = frame, Confidence = 0 };
        var best = double.NegativeInfinity; var bx = minX; var by = minY;
        // Fine details must survive coarse sampling; retain several candidate
        // peaks so a one-pixel phase does not exclude the correct match.
        List<(double Score, int X, int Y)> peaks = [];
        for (var y = minY; y <= maxY; y += 2)
        {
            token.ThrowIfCancellationRequested();
            for (var x = minX; x <= maxX; x += 2)
            {
                var score = Score(x, y);
                if (peaks.Count < 8 || score > peaks[^1].Score)
                {
                    peaks.Add((score, x, y)); peaks.Sort((a, b) => b.Score.CompareTo(a.Score));
                    if (peaks.Count > 8) peaks.RemoveAt(8);
                }
            }
        }
        foreach (var peak in peaks)
            for (var y = Math.Max(minY, peak.Y - 2); y <= Math.Min(maxY, peak.Y + 2); y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = Math.Max(minX, peak.X - 2); x <= Math.Min(maxX, peak.X + 2); x++)
                {
                    var score = Score(x, y);
                    var distance = Math.Abs(x + _region.Width / 2d - previous.X) + Math.Abs(y + _region.Height / 2d - previous.Y);
                    var bestDistance = Math.Abs(bx + _region.Width / 2d - previous.X) + Math.Abs(by + _region.Height / 2d - previous.Y);
                    if (score > best + 1e-10 || Math.Abs(score - best) < 1e-10 && distance < bestDistance) { best = score; bx = x; by = y; }
                }
            }
        var dx = bx > minX && bx < maxX ? Peak(Score(bx - 1, by), best, Score(bx + 1, by)) : 0;
        var dy = by > minY && by < maxY ? Peak(Score(bx, by - 1), best, Score(bx, by + 1)) : 0;
        return new(frame, bx + _region.Width / 2d + dx, by + _region.Height / 2d + dy, Math.Clamp(best, 0, 1));

        double Score(int x, int y)
        {
            double sum = 0, squares = 0, product = 0;
            foreach (var p in _pattern)
            {
                var value = Gray(bgra, (y + p.Y) * stride + (x + p.X) * 4);
                sum += value; squares += value * value; product += p.Value * value;
            }
            var energy = squares - sum * sum / _pattern.Length;
            return energy <= 1 ? 0 : product / Math.Sqrt(_energy * energy);
        }
    }
    private static double Peak(double before, double center, double after)
    {
        var denominator = before - 2 * center + after;
        return Math.Abs(denominator) < 1e-9 ? 0 : Math.Clamp(.5 * (before - after) / denominator, -.5, .5);
    }
    private static double Gray(byte[] pixels, int offset) => (29 * pixels[offset] + 150 * pixels[offset + 1] + 77 * pixels[offset + 2]) / 256d;
    private static void ValidateImage(byte[] pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < (long)width * 4 || pixels.LongLength < (long)stride * height) throw new ArgumentException("Invalid BGRA video frame.");
    }
}
