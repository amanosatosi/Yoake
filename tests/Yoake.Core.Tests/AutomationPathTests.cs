using Yoake.Core.Automation;

namespace Yoake.Core.Tests;

public sealed class AutomationPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "yoake-automation-paths-" + Guid.NewGuid().ToString("N"));
    private string Write(string relative, string text = "return true")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
    }

    [Fact]
    public void IncludeIsMasterRelativeAndPlainNamesUseConfiguredOrder()
    {
        var master = Write("master/main.lua"); var local = Write("master/shared.lua");
        Write("include/shared.lua"); var configured = Write("include/only.lua");
        var nested = Write("master/nested/日本語 မြန်မာ.lua");
        var resolver = new AutomationPathResolver(master, [Path.Combine(_root, "include")]);
        Assert.Equal(local, resolver.ResolveInclude("shared.lua"));
        Assert.Equal(configured, resolver.ResolveInclude("only.lua"));
        Assert.Equal(nested, resolver.ResolveInclude("nested/日本語 မြန်မာ.lua"));
        Assert.Equal(nested, resolver.ResolveInclude(nested));
        Assert.Throws<FileNotFoundException>(() => resolver.ResolveInclude("missing.lua"));
    }

    [Fact]
    public void RequirePrefersMoonAndSupportsModuleInitAndModifiedPackagePath()
    {
        var master = Write("master/main.lua"); Write("include/aegisub/util.lua");
        var moon = Write("include/aegisub/util.moon"); var init = Write("include/package/init.lua");
        var custom = Write("master/custom/module.lua");
        var resolver = new AutomationPathResolver(master, [Path.Combine(_root, "include")]);
        Assert.Equal(moon, resolver.ResolveModule("aegisub.util"));
        Assert.Equal(init, resolver.ResolveModule("package"));
        Assert.Equal(custom, resolver.ResolveModule("module", "custom/?.lua"));
        Assert.Null(resolver.ResolveModule("absent"));
    }

    [Fact]
    public void DiscoveryIsTopLevelDeduplicatedDeterministicAndCancellable()
    {
        var first = Write("autoload/a.lua"); var second = Write("autoload/b.moon");
        Write("autoload/nested/hidden.lua"); Write("autoload/ignore.txt");
        var directory = Path.Combine(_root, "autoload");
        Assert.Equal(new[] { first, second }, AutomationPathResolver.Discover([directory, directory], CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => AutomationPathResolver.Discover([directory], new CancellationToken(true)));
    }

    [Fact]
    public void DecodeUsesExplicitTokensWithoutInventingMissingMediaPaths()
    {
        var master = Write("master/main.lua");
        var resolver = new AutomationPathResolver(master, [], new Dictionary<string, string> { ["?user"] = _root });
        Assert.Equal(Path.Combine(_root, "automation", "include"), resolver.DecodePath("?user/automation/include"));
        Assert.Equal("?video", resolver.DecodePath("?video"));
        Assert.Throws<ArgumentException>(() => new AutomationPathResolver("relative.lua", []));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
