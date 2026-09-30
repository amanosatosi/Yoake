using Avalonia;
using Yoake.Native;

namespace Yoake.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WindowsNativeRuntime.ConfigurePackagedRuntime(AppContext.BaseDirectory);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
