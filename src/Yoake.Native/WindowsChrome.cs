using System.Runtime.InteropServices;
namespace Yoake.Native;

public static partial class WindowsChrome
{
    public static double Configure(nint window,double scaling)
    {
        if(!OperatingSystem.IsWindows()||window==0)return 0;
        // Suppress redundant NC title/icon painting only. WS_CAPTION, native
        // buttons, DWM hit testing, system menu and resize frame stay intact.
        var options=new ThemeOptions{Flags=3,Mask=3};_ = SetWindowThemeAttribute(window,1,in options,(uint)Marshal.SizeOf<ThemeOptions>());
        if(DwmGetWindowAttribute(window,5,out var bounds,(uint)Marshal.SizeOf<Rect>())==0&&bounds.Right>bounds.Left)
            return (bounds.Right-bounds.Left)/Math.Max(1,scaling);
        var dpi=GetDpiForWindow(window);return 3*GetSystemMetricsForDpi(30,dpi)/Math.Max(1,scaling);
    }
    [StructLayout(LayoutKind.Sequential)] private struct ThemeOptions{public uint Flags,Mask;}
    [StructLayout(LayoutKind.Sequential)] private struct Rect{public int Left,Top,Right,Bottom;}
    [LibraryImport("uxtheme.dll")] private static partial int SetWindowThemeAttribute(nint window,int attribute,in ThemeOptions options,uint size);
    [LibraryImport("dwmapi.dll")] private static partial int DwmGetWindowAttribute(nint window,int attribute,out Rect rect,uint size);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index,uint dpi);
}
