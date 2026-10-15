using System.Diagnostics;
using System.Text;
using Xunit;
using Yoake.Core.Automation;
using Yoake.Core.Logging;
using Yoake.Core.Subtitles;
using Yoake.Native.Automation;

namespace Yoake.Automation.Tests;

public sealed partial class LuaProviderTests
{
    // These are real-provider correctness tests with optional performance output,
    // never shared-runner elapsed-time gates. Three fresh invocations distinguish
    // first-use costs from warm throughput. Script loading is reported separately.
    [Theory]
    [InlineData("replace", 100)]
    [InlineData("replace", 1000)]
    [InlineData("replace", 5000)]
    [InlineData("generate", 5000)]
    [InlineData("generate", 20000)]
    [InlineData("templater", 100)]
    [InlineData("templater", 400)]
    public async Task RealProviderPerformanceAndExactOutput(string workload, int size)
    {
        var reportDirectory = Environment.GetEnvironmentVariable("YOAKE_AUTOMATION_BENCHMARK_OUTPUT");
        var output = new StringBuilder();
        var load = Stopwatch.StartNew();
        using var script = workload == "templater"
            ? await _provider.LoadAsync(Path.Combine(AppContext.BaseDirectory, "autoload", "kara-templater.lua"),
                new(Path.Combine(AppContext.BaseDirectory, "autoload", "kara-templater.lua"), [Path.Combine(AppContext.BaseDirectory, "include")]), default)
            : await Load(workload == "replace" ? """
aegisub.register_macro('Benchmark','',function(subs)
  for i=1,#subs do
    local line=subs[i]
    if line.class=='dialogue' then line.layer=line.layer+1; subs[i]=line end
  end
  aegisub.set_undo_point('Benchmark')
end)
""" : $$"""
aegisub.register_macro('Benchmark','',function(subs)
  local line=subs[#subs]
  for i=1,{{size}} do
    line.text='effect '..i; line.effect='fx'; line.layer=2
    subs.append(line)
  end
  aegisub.set_undo_point('Benchmark')
end)
""", "benchmark.lua");
        output.AppendLine(FormattableString.Invariant($"{workload}/{size}: load_ms={load.Elapsed.TotalMilliseconds:F3}"));
        for (var iteration = 0; iteration < (reportDirectory is null ? 1 : 3); iteration++)
        {
            var source = AssDocument.CreateEmpty().Serialize() + "Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text,Future\n";
            if (workload == "templater")
            {
                source += "Comment: 2,0:00:00.00,0:00:00.00,Default,,0,0,0,template line notext loop 10,!retime(\"line\",0,0)!L,template-secret\n";
                source += "Comment: 3,0:00:00.00,0:00:00.00,Default,,0,0,0,template syl noblank loop 10,!retime(\"syl\",0,0)!S,template-secret\n";
                source += "Comment: 4,0:00:00.00,0:00:00.00,Default,,0,0,0,template char noblank loop 10,!retime(\"syl\",0,0)!C,template-secret\n";
            }
            source += string.Concat(Enumerable.Repeat("Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,," +
                (workload == "templater" ? "{\\k20}ab{\\k30}cd" : "{\\distort(1,2)\\future(x)}日本語 မြန်မာ") + ",secret\n", workload == "generate" ? 1 : size));
            source += "[Unknown]\nMystery: untouched\n";
            if (workload == "templater" && size == 100)
            {
                var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "automation", "kfx-throughput.ass"));
                Assert.Equal(source, fixture); // Keep the committed input identical to the baseline generator.
                source = fixture;
            }
            var document = AssDocument.Parse(source); var editor = new SubtitleEditor(document);
            var before = document.Serialize();
            var diagnostics = reportDirectory is null ? null : new AutomationDiagnostics();
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var elapsed = Stopwatch.StartNew();
            using var subs = new AutomationSubtitleDocument(document, diagnostics: diagnostics);
            // Match the normal desktop's synchronous centralized logging as well
            // as real GDI metrics; a no-op log would conceal KFX trace overhead.
            var log = new FileAppLog(Path.Combine(_root, "benchmark.log"));
            await script.RunAsync(script.Macros.Single().Index, new(subs, [], 0, new BenchmarkServices(log)) { Diagnostics = diagnostics }, default);
            var runMs = elapsed.Elapsed.TotalMilliseconds;
            Assert.Equal(before, document.Serialize());
            subs.Commit(editor, "Benchmark");
            var commitMs = elapsed.Elapsed.TotalMilliseconds - runMs;
            var bytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            output.AppendLine(FormattableString.Invariant($"iteration={iteration}, run_ms={runMs:F3}, commit_ms={commitMs:F3}, allocated_bytes={bytes}"));
            output.AppendLine(diagnostics?.Report());
            if (reportDirectory is not null)
            {
                Directory.CreateDirectory(reportDirectory);
                await File.WriteAllTextAsync(Path.Combine(reportDirectory, $"{workload}-{size}.txt"), output.ToString());
            }
            if (workload == "replace")
            {
                Assert.Equal(size, document.Events.Count);
                Assert.All(document.Events, e => { Assert.Equal("1", e.Layer); Assert.Equal("secret", e.Get("Future")); });
            }
            else if (workload == "generate")
            {
                Assert.Equal(size + 1, document.Events.Count);
                Assert.Equal("effect " + size, document.Events[^1].Text);
                Assert.All(document.Events.Skip(1), e => { Assert.Equal("fx", e.Effect); Assert.Equal("2", e.Layer); Assert.Equal("secret", e.Get("Future")); });
            }
            else
            {
                var generated = document.Events.Where(e => e.Effect == "fx").ToArray();
                Assert.Equal(size * 70, generated.Length);
                // Derived independently from the release's parse_templates,
                // retime and per-character branches; no font-dependent positions.
                // A line template's main text is repeated per syllable; its
                // pre-line portion would be applied just once (release source).
                var expected = new[] { ("LL", "2", 1000L, 2000L), ("Sab", "3", 1000L, 1200L), ("Scd", "3", 1200L, 1500L),
                    ("Ca", "4", 1000L, 1200L), ("Cb", "4", 1000L, 1200L), ("Cc", "4", 1200L, 1500L), ("Cd", "4", 1200L, 1500L) };
                foreach (var (text, layer, start, end) in expected)
                    Assert.Equal(size * 10, generated.Count(e => e.Text == text && e.Layer == layer && e.StartMilliseconds == start && e.EndMilliseconds == end));
                Assert.All(generated, e => { Assert.Equal("Default", e.Style); Assert.False(e.IsComment); Assert.Equal("secret", e.Get("Future")); });
            }
            Assert.Contains("[Unknown]\nMystery: untouched\n", document.Serialize());
            var after = document.Serialize();
            editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
            editor.Undo.Redo(); Assert.Equal(after, document.Serialize());
        }
        if (reportDirectory is not null)
        {
            Directory.CreateDirectory(reportDirectory);
            await File.WriteAllTextAsync(Path.Combine(reportDirectory, $"{workload}-{size}.txt"), output.ToString());
        }
    }

    private sealed class BenchmarkServices(IAppLog log) : IAutomationHostServices
    {
        public string? FileName => null;
        public IReadOnlyDictionary<string, object?> ProjectProperties => new Dictionary<string, object?>();
        public int? FrameFromMilliseconds(int milliseconds) => null;
        public int? MillisecondsFromFrame(int frame) => null;
        public AutomationVideoSize? VideoSize => null;
        public IReadOnlyList<int> Keyframes => [];
        public AutomationTextMetrics MeasureText(AutomationLine style, string text) => WindowsAutomationTextMeasurer.Measure(style, text);
        public string Translate(string text) => text;
        public string? ClipboardGet() => null;
        public bool ClipboardSet(string text) => false;
        public AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) { }
        public void Log(string message, int level) => log.Info($"Automation [{level}]: {message}");
    }
}
