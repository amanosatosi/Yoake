using System.Runtime.InteropServices;
using Yoake.Core.Subtitles;

namespace Yoake.Native;

public static partial class WindowsScreenColor
{
    // Cursor coordinates and desktop DC pixels are physical screen coordinates,
    // including negative coordinates on monitors left/above the primary display.
    public static AssColor AtCursor(byte transparency)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        // The input overlay has closed. Wait for composition so its one-alpha
        // hit-test surface cannot contaminate the sampled desktop pixel.
        _=DwmFlush();
        if(!GetCursorPos(out var point))throw new InvalidOperationException("Cannot locate screen cursor.");
        var dc=GetDC(0);if(dc==0)throw new InvalidOperationException("Cannot sample screen.");
        try{var rgb=GetPixel(dc,point.X,point.Y);if(rgb==uint.MaxValue)throw new InvalidOperationException("Screen pixel unavailable.");return new((byte)rgb,(byte)(rgb>>8),(byte)(rgb>>16),transparency);}
        finally{ReleaseDC(0,dc);}
    }
    [LibraryImport("dwmapi.dll")] private static partial int DwmFlush();
    [StructLayout(LayoutKind.Sequential)] private struct Point{public int X,Y;}
    [LibraryImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint window);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint window,nint dc);
    [LibraryImport("gdi32.dll")] private static partial uint GetPixel(nint dc,int x,int y);
}
