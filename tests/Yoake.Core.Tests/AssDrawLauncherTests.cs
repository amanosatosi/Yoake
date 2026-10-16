using Yoake.Core.Settings;
using Yoake.Core.Tools;

namespace Yoake.Core.Tests;

public sealed class AssDrawLauncherTests:IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"Yoake-ASSDraw-"+Guid.NewGuid());
    public AssDrawLauncherTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);
    private string Exe(string relative)
    {var path=Path.GetFullPath(Path.Combine(_root,relative));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"Controlled path fixture; never executed.");return path;}
    [Fact] public void ConfiguredUnicodePathPrecedesBothBundledLocations()
    {var configured=Exe("日本語 custom/Draw.exe");Exe("ASSDraw3.exe");Exe("data/ASSDraw3.exe");var found=AssDrawLauncher.Discover(configured,_root);Assert.Equal(configured,found!.Path);Assert.True(found.Configured);}
    [Fact] public void InstalledAndPortableRootDiscoveryUseTheExecutableDirectory()
    {var executable=Exe("ASSDraw3.exe");Assert.Equal(executable,AssDrawLauncher.Discover(null,_root)!.Path);File.WriteAllText(Path.Combine(_root,"yoake.portable"),"");Assert.Equal(executable,AssDrawLauncher.Discover(null,_root)!.Path);}
    [Fact] public void DataDirectoryIsTheSecondSupportedBundledLocation()
    {var path=Exe("data/ASSDraw3.exe");Assert.Equal(path,AssDrawLauncher.Discover(null,_root)!.Path);var root=Exe("ASSDraw3.exe");Assert.Equal(root,AssDrawLauncher.Discover(null,_root)!.Path);}
    [Fact] public void MissingConfiguredExecutableFallsBackAndDisappearanceIsRechecked()
    {var custom=Exe("custom/Draw.exe");var bundled=Exe("ASSDraw3.exe");Assert.Equal(custom,AssDrawLauncher.Discover(custom,_root)!.Path);File.Delete(custom);Assert.Equal(bundled,AssDrawLauncher.Discover(custom,_root)!.Path);File.Delete(bundled);Assert.Null(AssDrawLauncher.Discover(custom,_root));Assert.Equal("ASSDraw3.exe doesn't exist.",AssDrawLauncher.MissingMessage);}
    [Theory] [InlineData("")] [InlineData("ASSDraw3.exe")] [InlineData("../ASSDraw3.exe")] [InlineData("font.ttf")]
    public void NonExistingRelativeAndNonExecutablePathsAreNotCandidates(string path)=>Assert.False(AssDrawLauncher.IsExecutablePath(path));
    [Fact] public void ExistingNonExeAndDirectoryCannotBecomeExecutablePaths()
    {var text=Exe("not-a-tool.txt");Assert.False(AssDrawLauncher.IsExecutablePath(text));var directory=Path.Combine(_root,"directory.exe");Directory.CreateDirectory(directory);Assert.False(AssDrawLauncher.IsExecutablePath(directory));}
    [Fact] public void ConfigurationSurvivesNormalPortableAndDamagedProfileRecovery()
    {
        var executable=Exe("日本語 tools/ASSDraw3.exe");File.WriteAllText(Path.Combine(_root,"yoake.portable"),"");var path=SettingsPathResolver.Resolve(_root,Path.Combine(_root,"roaming"));var store=new SettingsStore(path);
        store.Save(new(){AssDrawExecutable=executable});Assert.Equal(executable,store.Load().AssDrawExecutable);File.WriteAllText(path,"{\"assDrawExecutable\":\""+executable.Replace("\\","\\\\",StringComparison.Ordinal)+"\",\"theme\":\"damaged\"}");Assert.Equal(executable,store.Load().AssDrawExecutable);
    }
}
