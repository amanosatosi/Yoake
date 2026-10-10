using System.Globalization;

namespace Yoake.Core.Subtitles;

// ASS stores inverted alpha and BGR, not CSS RGB. Opacity is a UI concern.
public readonly record struct AssColor(byte Red, byte Green, byte Blue, byte Transparency)
{
    public byte Opacity => (byte)(255 - Transparency);
    public string StyleValue => $"&H{Transparency:X2}{Blue:X2}{Green:X2}{Red:X2}";
    public string RgbOverride => $"&H{Blue:X2}{Green:X2}{Red:X2}&";
    public string AlphaOverride => $"&H{Transparency:X2}&";
    public static bool TryParse(string? source, out AssColor color)
    {
        color = default; if (source is null) return false;
        var text = source.Trim(); uint packed;
        if (text.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            var hex = text[2..].TrimEnd('&');
            if (hex.Length is < 1 or > 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed)) return false;
        }
        else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed)) packed = unchecked((uint)signed);
        else if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out packed)) return false;
        color = new((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24)); return true;
    }
}
