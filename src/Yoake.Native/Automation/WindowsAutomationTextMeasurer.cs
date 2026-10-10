using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Yoake.Core.Automation;

namespace Yoake.Native.Automation;

// GDI objects remain entirely inside the provider and are released per request.
public static partial class WindowsAutomationTextMeasurer
{
    public static unsafe AutomationTextMetrics Measure(AutomationLine style, string text)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Automation GDI text metrics require Windows.");
        var fontSize = style.Number("fontsize") * 64;
        if (fontSize <= 0 || fontSize > int.MaxValue) throw new ArgumentException("Font size is out of range.");
        var fontName = style.String("fontname");
        LogFont font = new()
        {
            Height = (int)fontSize, Weight = style.Boolean("bold") ? 700 : 400,
            Italic = (byte)(style.Boolean("italic") ? 1 : 0),
            Underline = (byte)(style.Boolean("underline") ? 1 : 0),
            StrikeOut = (byte)(style.Boolean("strikeout") ? 1 : 0),
            CharSet = unchecked((byte)style.Integer("encoding")), OutPrecision = 4, Quality = 4
        };
        var length = Math.Min(31, fontName.Length);
        if (length > 0 && length < fontName.Length && char.IsHighSurrogate(fontName[length - 1])) length--;
        for (var i = 0; i < length; i++) font.FaceName[i] = fontName[i];
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw Error("CreateCompatibleDC");
        nint selected = 0; nint handle = 0;
        try
        {
            if (SetMapMode(dc, 1) == 0) throw Error("SetMapMode");
            handle = CreateFontIndirectW(&font);
            if (handle == 0) throw Error("CreateFontIndirectW");
            selected = SelectObject(dc, handle);
            if (selected == 0 || selected == -1) throw Error("SelectObject");
            Size size = default;
            fixed (char* pointer = text)
                if (GetTextExtentPoint32W(dc, pointer, text.Length, &size) == 0) throw Error("GetTextExtentPoint32W");
            TextMetric metrics = default;
            if (GetTextMetricsW(dc, &metrics) == 0) throw Error("GetTextMetricsW");
            // Keep whole-string GDI shaping. The old nonzero-spacing path measured
            // UTF-16 halves individually; Yoake adds spacing per grapheme instead.
            var spacing = style.Number("spacing") * StringInfo.ParseCombiningCharacters(text).Length;
            var x = style.Number("scale_x") / 100; var y = style.Number("scale_y") / 100;
            return new((size.Width / 64d + spacing) * x, size.Height / 64d * y,
                metrics.Descent / 64d * y, metrics.ExternalLeading / 64d * y);
        }
        finally
        {
            if (selected != 0 && selected != -1) _ = SelectObject(dc, selected);
            if (handle != 0) _ = DeleteObject(handle);
            _ = DeleteDC(dc);
        }
    }

    private static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), $"Automation text measurement: {operation} failed.");
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct LogFont
    {
        public int Height, Width, Escapement, Orientation, Weight;
        public byte Italic, Underline, StrikeOut, CharSet, OutPrecision, ClipPrecision, Quality, PitchAndFamily;
        public fixed char FaceName[32];
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetric
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveCharWidth, MaxCharWidth, Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharSet;
    }
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial int SetMapMode(nint dc, int mode);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static unsafe partial nint CreateFontIndirectW(LogFont* font);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static partial nint SelectObject(nint dc, nint obj);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static unsafe partial int GetTextExtentPoint32W(nint dc, char* text, int length, Size* size);
    [LibraryImport("gdi32.dll", SetLastError = true)] private static unsafe partial int GetTextMetricsW(nint dc, TextMetric* metrics);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] private static partial int DeleteDC(nint dc);
}
