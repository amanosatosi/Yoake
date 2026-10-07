using Yoake.Native;

namespace Yoake.App;

internal static class MangetsuAlphaVerification
{
    public static string Run(DecodedVideoFrame decoded)
    {
        // Ordinary text with counters/spacing ensures both painted glyphs and
        // completely transparent source pixels occur inside actual RGBA tiles.
        const string subtitle = """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 160
            PlayResY: 90
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,28,&H00FFFFFF,&H00FFFFFF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,5,0,0,0,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\pos(80,45)}O O
            """;
        using var renderer = new MangetsuSubtitleRenderer(subtitle, decoded.Width, decoded.Height);
        // Real FFMS2 output, then padded rows, meaningful alpha, and dimension
        // changes through the very same Composite entry point used by playback.
        var opaque = new DecodedVideoFrame(decoded.Width, decoded.Height, decoded.Stride, (byte[])decoded.Pixels.Clone());
        for (var y = 0; y < opaque.Height; y++)
            for (var x = 0; x < opaque.Width; x++)
                Require(opaque.Pixels[y * opaque.Stride + x * 4 + 3] == 255, "FFMS2 fixture is not opaque BGRA.");
        var details = Verify(renderer, opaque, "decoded opaque");
        details += "; " + Verify(renderer, CreateFrame(160, 90, 20, false), "padded opaque");
        details += "; " + Verify(renderer, CreateFrame(160, 90, 28, true), "padded varying alpha");
        details += "; " + Verify(renderer, CreateFrame(320, 180, 12, true), "resized larger");
        details += "; " + Verify(renderer, CreateFrame(80, 45, 16, true), "resized smaller");
        details += "; " + Verify(renderer, CreateFrame(160, 90, 20, true), "restored dimensions");

        var repeated = CreateFrame(160, 90, 20, true);
        renderer.Composite(repeated, 0.7); // Warm native interop before measuring.
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 5; i++) renderer.Composite(repeated, 0.7);
        Require(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore < 160 * 90,
            "Steady-state composition allocated a full-frame managed alpha buffer.");
        return "Mangetsu destination alpha preserved: " + details + "; scratch reused over five frames";
    }

    private static string Verify(MangetsuSubtitleRenderer renderer, DecodedVideoFrame frame, string name)
    {
        var before = (byte[])frame.Pixels.Clone();
        renderer.Composite(frame, 0.7);
        var coverage = renderer.CaptureCoverageForVerification(0.7);
        var painted = 0; var transparent = 0; var outside = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var index = y * frame.Stride + x * 4;
                Require(frame.Pixels[index + 3] == before[index + 3], $"{name}: destination alpha changed at {x},{y}.");
                var rgbChanged = !frame.Pixels.AsSpan(index, 3).SequenceEqual(before.AsSpan(index, 3));
                switch (coverage[y * frame.Width + x])
                {
                    case 0: Require(!rgbChanged, $"{name}: RGB changed outside subtitle tiles."); outside++; break;
                    case 1: Require(!rgbChanged, $"{name}: transparent tile pixels changed RGB."); transparent++; break;
                    case 2: if (rgbChanged) painted++; break;
                }
            }
            var padding = y * frame.Stride + frame.Width * 4;
            Require(frame.Pixels.AsSpan(padding, frame.Stride - frame.Width * 4).SequenceEqual(before.AsSpan(padding, frame.Stride - frame.Width * 4)),
                $"{name}: row padding changed.");
        }
        Require(painted > 0 && transparent > 0 && outside > 0,
            $"{name}: fixture must exercise painted, transparent-in-tile and outside pixels ({painted}/{transparent}/{outside}).");
        return $"{name} ({painted} RGB changes, {transparent} transparent-in-tile, {outside} outside)";
    }

    private static DecodedVideoFrame CreateFrame(int width, int height, int padding, bool varyingAlpha)
    {
        var stride = checked(width * 4 + padding);
        var pixels = new byte[checked(stride * height)];
        Array.Fill(pixels, (byte)0xA7); // Sentinel padding must survive untouched.
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * stride + x * 4;
                pixels[index] = 20; pixels[index + 1] = 12; pixels[index + 2] = 8;
                pixels[index + 3] = varyingAlpha ? (byte)((x + y * width) % 256) : (byte)255;
            }
        return new(width, height, stride, pixels);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
