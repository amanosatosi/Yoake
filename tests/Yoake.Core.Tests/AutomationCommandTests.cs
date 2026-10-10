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
        Assert.Equal(135, view.MillisecondsFromFrame(3)); Assert.Equal(-20, view.MillisecondsFromFrame(0));
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
