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
    [Fact]
    public void ModernScriptsPreferIsolatedModernIncludesWithoutChangingLegacyResolution()
    {
        var tokens=new Dictionary<string,string>{{"?user",Path.Combine(_root,"user")},{"?data",Path.Combine(_root,"app")}};
        var modern=Write("user/automation-3.5-above/autoload/new.lua");
        var modernModule=Write("user/automation-3.5-above/include/shared.lua","return 'modern'");
        var legacy=Write("user/automation/autoload/old.lua");var oldModule=Write("user/automation/include/shared.lua","return 'legacy'");
        var includes=new[]{"?user/automation/include"};
        var resolver=new AutomationPathResolver(modern,includes,tokens);
        Assert.Equal(modernModule,resolver.ResolveInclude("shared.lua"));
        Assert.Equal(Path.Combine(_root,"user","automation-3.5-above","include","l0"),resolver.DecodePath("?user/automation/include/l0"));
        Assert.Equal(Path.Combine(_root,"user","automation-extra"),resolver.DecodePath("?user/automation-extra"));
        Assert.Equal(oldModule,new AutomationPathResolver(legacy,includes,tokens).ResolveInclude("shared.lua"));
        var lookalike=Write("user/automation-3.5-above-other/main.lua");
        Assert.Equal(oldModule,new AutomationPathResolver(lookalike,includes,tokens).ResolveInclude("shared.lua"));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
