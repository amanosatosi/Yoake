using Yoake.Core.Automation;
using Yoake.Core.Subtitles;

namespace Yoake.Core.Tests;

public sealed class AutomationExportTests
{
    private static AssDocument Document() => AssDocument.Parse("[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,source\n");
    [Fact]
    public void DefaultOrderIsStableDescendingPriorityAndDuplicateNamesAreDistinct()
    {
        using var a = new Script([new(1, "Filter", "", 10, false), new(2, "Low", "", -1, false)]);
        using var b = new Script([new(1, "Filter", "", 100, false), new(2, "Equal", "", 10, false)]);
        var filters = AutomationExportPipeline.Filters([a, b]);
        Assert.Equal(new[] { "Filter (1)", "Filter", "Equal", "Low" }, filters.Select(f => f.Name));
        Assert.Same(b, filters[0].Script);
    }

    [Fact]
    public async Task ChosenOrderChainsCopiesWithoutChangingLiveSourceOrUndoRedo()
    {
        var document = Document(); var editor = new SubtitleEditor(document);
        editor.SetField(document.Events[0], "Text", "original", "Fixture edit"); editor.Undo.Undo();
        var before = document.Serialize(); var revision = document.Revision;
        using var script = new Script([new(1, "Append", "", 0, false)]);
        var binding = AutomationExportPipeline.Filters([script])[0];
        var output = await AutomationExportPipeline.RunAsync(before,
            [new(binding, new Dictionary<string, object?> { ["suffix"] = "first" }), new(binding, new Dictionary<string, object?> { ["suffix"] = "second" })], new Services(), default);
        Assert.Equal(document.Events[0].Text + "firstsecond", output.Events[0].Text);
        Assert.Equal(before, document.Serialize()); Assert.Equal(revision, document.Revision);
        Assert.False(editor.Undo.CanUndo); Assert.True(editor.Undo.CanRedo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationAfterMutationCannotReachLiveDocument(bool cancel)
    {
        var document = Document(); var before = document.Serialize();
        using var script = new Script([new(1, "Failure", "", 0, false)], cancel ? "cancel" : "error");
        var binding = AutomationExportPipeline.Filters([script])[0];
        var error = await Record.ExceptionAsync(async () => await AutomationExportPipeline.RunAsync(before,
            [new(binding, new Dictionary<string, object?> { ["suffix"] = "discarded" })], new Services(), default));
        Assert.IsAssignableFrom<Exception>(error);
        if (cancel) Assert.IsType<OperationCanceledException>(error); else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void CancelledSaveLeavesExistingOutputAndNoTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "yoake-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "output.ass"); File.WriteAllText(output, "original bytes");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => AssDocument.CreateEmpty().Save(output, new System.Text.UTF8Encoding(false, true), false, cancellation.Token));
            Assert.Equal("original bytes", File.ReadAllText(output)); Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Script(IReadOnlyList<AutomationExportFilter> filters, string? failure = null) : IAutomationScript
    {
        public string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "export.lua");
        public AutomationScriptMetadata Metadata => new("Fixture", "", "", "");
        public IReadOnlyList<AutomationMacro> Macros => [];
        public IReadOnlyList<AutomationExportFilter> Filters => filters;
        public ValueTask<AutomationMacroResult> RunAsync(int index, AutomationInvocation context, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<AutomationValidation> ValidateAsync(int index, AutomationInvocation context, CancellationToken token) => throw new NotSupportedException();
        public ValueTask RunFilterAsync(int index, AutomationInvocation context, IReadOnlyDictionary<string, object?> settings, CancellationToken token)
        {
            var i = Enumerable.Range(1, context.Subtitles.Count).First(i => Equals(context.Subtitles.Read(i)["class"], "dialogue"));
            var line = context.Subtitles.Read(i); line["text"] = line.String("text") + settings["suffix"]; context.Subtitles.Write(i, line);
            if (failure == "cancel") throw new OperationCanceledException();
            if (failure == "error") throw new InvalidOperationException("Fixture failure");
            return ValueTask.CompletedTask;
        }
        public void Dispose() { }
    }
    private sealed class Services : IAutomationHostServices
    {
        public string? FileName => null;
        public IReadOnlyDictionary<string, object?> ProjectProperties => new Dictionary<string, object?>();
        public int? FrameFromMilliseconds(int milliseconds) => null;
        public int? MillisecondsFromFrame(int frame) => null;
        public AutomationVideoSize? VideoSize => null;
        public IReadOnlyList<int> Keyframes => [];
        public AutomationTextMetrics MeasureText(AutomationLine style, string text) => throw new NotSupportedException();
        public string Translate(string text) => text;
        public string? ClipboardGet() => null;
        public bool ClipboardSet(string text) => false;
        public AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken token) => throw new NotSupportedException();
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) { }
        public void Log(string message, int level) { }
    }
}
