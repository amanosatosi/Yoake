namespace Yoake.Core.Media;

/// <summary>Source-over coverage for Mangetsu's premultiplied RGBA tiles.</summary>
public static class SubtitleAlpha
{
    public static void AccumulateRow(ReadOnlySpan<byte> rgba, Span<byte> bgra)
    {
        if (rgba.Length != bgra.Length || rgba.Length % 4 != 0) throw new ArgumentException("Expected matching RGBA/BGRA rows.");
        for (var i = 3; i < rgba.Length; i += 4)
        {
            var source = rgba[i];
            bgra[i] = (byte)(source + (bgra[i] * (255 - source) + 127) / 255);
        }
    }
    public static void UnpremultiplyBgra(Span<byte> pixels)
    {
        if (pixels.Length % 4 != 0) throw new ArgumentException("Expected BGRA pixels.");
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            for (var channel = 0; channel < 3; channel++)
                pixels[i + channel] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (pixels[i + channel] * 255 + alpha / 2) / alpha);
        }
    }
}
