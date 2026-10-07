using Avalonia;
using Yoake.Native;

namespace Yoake.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WindowsNativeRuntime.ConfigurePackagedRuntime(AppContext.BaseDirectory);
        if(args.Length==3&&args[0]=="--verify-editor")
        {
            Environment.Exit(EditorVerification.Run(args[1],args[2]));return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
