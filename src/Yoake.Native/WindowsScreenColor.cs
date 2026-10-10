using System.Runtime.InteropServices;
using Yoake.Core.Subtitles;

namespace Yoake.Native;

public static partial class WindowsScreenColor
{
    public sealed record Sample(int X,int Y,IReadOnlyList<AssColor> Pixels)
    {
        public AssColor Center=>Pixels[24];
    }
    public static Sample SampleAtCursor(byte transparency)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        if(!GetCursorPos(out var point))throw new InvalidOperationException("Cannot locate screen cursor.");
        var dc=GetDC(0);if(dc==0)throw new InvalidOperationException("Cannot sample screen.");
        try
        {
            var pixels=new AssColor[49];
            for(var y=-3;y<=3;y++)for(var x=-3;x<=3;x++)
            {
                var rgb=GetPixel(dc,point.X+x,point.Y+y);
                // Outside a monitor is a neutral unavailable sample; center
                // failure is reported so it can never accept a fabricated color.
                if(rgb==uint.MaxValue){if(x==0&&y==0)throw new InvalidOperationException("Screen pixel unavailable.");pixels[(y+3)*7+x+3]=new(0,0,0,transparency);}
                else pixels[(y+3)*7+x+3]=new((byte)rgb,(byte)(rgb>>8),(byte)(rgb>>16),transparency);
            }
            return new(point.X,point.Y,pixels);
        }
        finally{ReleaseDC(0,dc);}
    }
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
