using System.Runtime.InteropServices;
namespace Yoake.Native;

public static partial class WindowsChrome
{
    public static double CaptionWidth(nint window,double scaling)
    {
        if(!OperatingSystem.IsWindows()||window==0)return 0;
        if(DwmGetWindowAttribute(window,5,out var bounds,(uint)Marshal.SizeOf<Rect>())==0&&bounds.Right>bounds.Left)
            return (bounds.Right-bounds.Left)/Math.Max(1,scaling);
        var dpi=GetDpiForWindow(window);return 3*GetSystemMetricsForDpi(30,dpi)/Math.Max(1,scaling);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect{public int Left,Top,Right,Bottom;}
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint window,int attribute,out Rect rect,uint size);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index,uint dpi);
}
