using Avalonia.Media;

namespace Yoake.UI;

// UI fallback only. ASS fonts and Mangetsu keep their independent font resolver.
public static class UiFontFallbacks
{
    public static FontManagerOptions CreateOptions()
    {
        var myanmar=UnicodeRange.Parse("U+1000-109F,U+A9E0-A9FF,U+AA60-AA7F,U+116D0-116FF");
        // A cached legacy Zawgyi face can cover Unicode codepoints without
        // interpreting them correctly. Resolve these Unicode fonts before the
        // general installed-font/cache search. Missing families fall through.
        return new FontManagerOptions
        {
            FontFallbacks=new[] { "MUA Office", "Myanmar Text", "Noto Sans Myanmar" }
                .Select(family=>new FontFallback { FontFamily=new FontFamily(family),UnicodeRange=myanmar }).ToArray()
        };
    }
}
