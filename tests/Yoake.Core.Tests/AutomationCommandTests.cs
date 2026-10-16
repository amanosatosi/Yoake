using Yoake.Core.Automation;
using Yoake.Core.Commands;

namespace Yoake.Core.Tests;

public sealed class AutomationCommandTests
{
    private static string PathFor(string name) => Path.Combine(Path.GetTempPath(), name + ".lua");
    [Fact]
    public async Task StableCommandResolvesIndependentDocumentInterpretersAndUnloadsCleanly()
    {
        var registry = new CommandRegistry(); var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        IAutomationScript? invoked = null;
        using var catalog = new AutomationCommandCatalog(registry, (binding, _, _) => { invoked = binding.Script; return ValueTask.CompletedTask; });
        var first = new Script(PathFor("shared")); var second = new Script(first.Path);
        catalog.SetScript(firstId, first.Path, first); catalog.SetScript(secondId, second.Path, second);
        var id = AutomationCommandCatalog.CommandId(first.Path, "Timing/Adjust");
        Assert.Single(registry.Commands);
        Assert.True(await registry.InvokeAsync(id, new(firstId))); Assert.Same(first, invoked);
        Assert.True(await registry.InvokeAsync(id, new(secondId))); Assert.Same(second, invoked);
        catalog.SetScript(firstId, first.Path, null);
        Assert.False(registry.CanExecute(id, new(firstId))); Assert.True(registry.CanExecute(id, new(secondId)));
        catalog.RemoveDocument(secondId);
        Assert.False(registry.CanExecute(id, new(secondId))); Assert.False(await registry.InvokeAsync(id, new(secondId)));
        Assert.Empty(registry.Commands);
    }
    [Fact]
    public void ValidationAndToggleStateCannotLeakAcrossTabsOrReloads()
    {
        var registry = new CommandRegistry(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var catalog = new AutomationCommandCatalog(registry, (_, _, _) => ValueTask.CompletedTask);
        var script = new Script(PathFor("validation"), validation: true);
        catalog.SetScript(null, script.Path, script);
        var binding = Assert.Single(catalog.Macros(a));
        Assert.False(registry.CanExecute(binding.CommandId, new(a)));
        catalog.UpdateValidation(a, binding, new(true, "Dynamic help", true));
        Assert.True(registry.CanExecute(binding.CommandId, new(a))); Assert.True(registry.GetRequired(binding.CommandId).IsChecked(new(a)));
        Assert.False(registry.CanExecute(binding.CommandId, new(b)));
        var replacement = new Script(script.Path, validation: true);
        catalog.SetScript(null, replacement.Path, replacement);
        catalog.UpdateValidation(a, binding, new(true, "obsolete callback", true));
        Assert.False(registry.CanExecute(binding.CommandId, new(a))); Assert.False(registry.GetRequired(binding.CommandId).IsChecked(new(a)));
        Assert.Same(replacement, Assert.Single(catalog.Macros(a)).Script);
    }
    [Fact]
    public void UnregisterCannotRemoveAnotherOwnersReplacement()
    {
        var registry = new CommandRegistry();
        AppCommand Make() => new(new("fixture", "Fixture", "", "Tests"), (_, _) => ValueTask.CompletedTask);
        var old = Make(); registry.Register(old); Assert.True(registry.Unregister(old));
        var current = Make(); registry.Register(current);
        Assert.False(registry.Unregister(old)); Assert.Same(current, registry.GetRequired("fixture"));
    }
    [Fact]
    public void TimecodeViewUses322StartBoundariesOverVfrTimeline()
    {
        var view = new AutomationTimecodeView([0, 0.05, 0.1, 0.17], 25);
        Assert.Equal(0, view.FrameFromMilliseconds(0)); Assert.Equal(1, view.FrameFromMilliseconds(1));
        Assert.Equal(1, view.FrameFromMilliseconds(50)); Assert.Equal(2, view.FrameFromMilliseconds(51));
        Assert.Equal(25, view.MillisecondsFromFrame(1)); Assert.Equal(75, view.MillisecondsFromFrame(2));
        Assert.Equal(135, view.MillisecondsFromFrame(3)); Assert.Equal(-28, view.MillisecondsFromFrame(0));
        Assert.Equal(199, view.MillisecondsFromFrame(4)); Assert.Equal(255, view.MillisecondsFromFrame(5));
        Assert.Equal(4, view.FrameFromMilliseconds(171)); Assert.Equal(4, view.FrameFromMilliseconds(227));
        Assert.Equal(5, view.FrameFromMilliseconds(228)); Assert.Equal(-1, view.FrameFromMilliseconds(-57));
        var normalized = new AutomationTimecodeView([1, 1.05, 1.1, 1.17], 25);
        Assert.Equal(view.MillisecondsFromFrame(4), normalized.MillisecondsFromFrame(4));
        Assert.Equal(view.FrameFromMilliseconds(228), normalized.FrameFromMilliseconds(228));
    }
    [Fact]
    public void CfrAndSingleFrameStartConversionsMatchReleaseIntegerRounding()
    {
        var cfr = new AutomationTimecodeView([0, .04, .08], 25);
        Assert.Equal(-20, cfr.MillisecondsFromFrame(0)); Assert.Equal(20, cfr.MillisecondsFromFrame(1));
        Assert.Equal(100, cfr.MillisecondsFromFrame(3)); Assert.Equal(140, cfr.MillisecondsFromFrame(4));
        Assert.Equal(3, cfr.FrameFromMilliseconds(120)); Assert.Equal(4, cfr.FrameFromMilliseconds(121));
        Assert.Equal(-1, cfr.FrameFromMilliseconds(-40));
        var single = new AutomationTimecodeView([0], 30000d / 1001);
        Assert.Equal(17, single.MillisecondsFromFrame(1)); Assert.Equal(50, single.MillisecondsFromFrame(2));
        // CFR extrapolation retains fractional rate before rounding, unlike
        // a stored integer timestamp at 33 ms.
        Assert.Equal(1, single.FrameFromMilliseconds(33)); Assert.Equal(1, single.FrameFromMilliseconds(34));
        Assert.Equal(2, single.FrameFromMilliseconds(35));
        Assert.Throws<ArgumentException>(() => new AutomationTimecodeView([0, 0], 25));
    }
    private sealed class Script(string path, bool validation = false) : IAutomationScript
    {
        public string Path => path;
        public AutomationScriptMetadata Metadata => new("Fixture", "", "", "");
        public IReadOnlyList<AutomationMacro> Macros => [new(1, "Timing/Adjust", "", validation, validation)];
        public IReadOnlyList<AutomationExportFilter> Filters => [];
        public ValueTask<AutomationMacroResult> RunAsync(int index, AutomationInvocation invocation, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<AutomationValidation> ValidateAsync(int index, AutomationInvocation invocation, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
