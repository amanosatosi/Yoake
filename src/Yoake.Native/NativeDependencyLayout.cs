namespace Yoake.Native;

public sealed record NativeDependencyLayout(string Root)
{
    public string RuntimeDirectory => Path.Combine(Root, "win-x64");
    public string RuntimeManifest => Path.Combine(Root, "runtime-manifest.json");

    public static NativeDependencyLayout ForPackagedApplication(string applicationDirectory)
        => new(Path.Combine(applicationDirectory, "native"));
}

public enum NativeOwnership
{
    Borrowed,
    OwnedByManagedSafeHandle,
    OwnedByNativeLibrary,
}

public sealed record NativeAbiContract(
    string Library,
    string Version,
    NativeOwnership Ownership,
    bool ThreadAffine,
    string StringEncoding = "UTF-8");
