using System.Diagnostics;

namespace Yoake.Core.Tools;

public sealed record AssDrawLocation(string Path,bool Configured);
public interface IExternalToolLauncher { void Launch(string executable); }
public sealed class ExternalToolLauncher:IExternalToolLauncher
{
    public void Launch(string executable)
    {
        var full=Path.GetFullPath(executable);
        using var process=Process.Start(new ProcessStartInfo{FileName=full,WorkingDirectory=Path.GetDirectoryName(full)!,UseShellExecute=false});
        if(process is null)throw new IOException("Windows did not start ASSDraw3.");
        // Dispose our process handle; the external application remains alive.
    }
}
public static class AssDrawLauncher
{
    public const string MissingMessage="ASSDraw3.exe doesn't exist.";
    public static bool IsExecutablePath(string? path)
    {
        if(string.IsNullOrWhiteSpace(path))return false;
        try{return Path.IsPathFullyQualified(path)&&Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase)&&File.Exists(path);}
        catch(Exception error)when(error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException){return false;}
    }
    public static AssDrawLocation? Discover(string? configured,string applicationDirectory)
    {
        if(IsExecutablePath(configured))return new(Path.GetFullPath(configured!),true);
        var root=Path.GetFullPath(applicationDirectory);
        foreach(var candidate in new[]{Path.Combine(root,"ASSDraw3.exe"),Path.Combine(root,"data","ASSDraw3.exe")})
            if(IsExecutablePath(candidate))return new(candidate,false);
        return null;
    }
}
