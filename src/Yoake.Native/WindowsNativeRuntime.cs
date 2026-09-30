using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Yoake.Native;

public static partial class WindowsNativeRuntime
{
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private static readonly object Gate = new();
    private static nint _directoryCookie;
    private static string? _configuredDirectory;

    public static string? ConfiguredDirectory => _configuredDirectory;

    public static void ConfigurePackagedRuntime(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        if (!OperatingSystem.IsWindows())
            return;

        var runtimeDirectory = Path.GetFullPath(Path.Combine(applicationDirectory, "native", "win-x64"));
        if (!Directory.Exists(runtimeDirectory))
            return;

        lock (Gate)
        {
            if (string.Equals(_configuredDirectory, runtimeDirectory, StringComparison.OrdinalIgnoreCase))
                return;
            if (_directoryCookie != 0)
                throw new InvalidOperationException($"Native runtime DLL directory is already configured as '{_configuredDirectory}'.");

            if (SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetDefaultDllDirectories failed.");

            _directoryCookie = AddDllDirectory(runtimeDirectory);
            if (_directoryCookie == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"AddDllDirectory failed for '{runtimeDirectory}'.");

            _configuredDirectory = runtimeDirectory;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetDefaultDllDirectories(uint directoryFlags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint AddDllDirectory(string newDirectory);
}
