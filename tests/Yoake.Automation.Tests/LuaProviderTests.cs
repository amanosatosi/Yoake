using Xunit;
using Yoake.Core.Automation;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.Native.Automation;

namespace Yoake.Automation.Tests;

public sealed class LuaProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "yoake-lua-fixture-" + Guid.NewGuid().ToString("N"));
    private readonly LuaAutomationProvider _provider = new(Path.Combine(AppContext.BaseDirectory, "host.lua"));
    public LuaProviderTests()
    {
        var layout = Environment.GetEnvironmentVariable("YOAKE_AUTOMATION_TEST_RUNTIME")
            ?? throw new InvalidOperationException("This suite requires the CI-built real native Automation runtime.");
        WindowsNativeRuntime.ConfigurePackagedRuntime(layout);
        Directory.CreateDirectory(_root);
    }

    private ValueTask<IAutomationScript> Load(string source, string name = "日本語 မြန်မာ.lua")
    {
        var path = Path.Combine(_root, name); File.WriteAllText(path, source);
        return _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), CancellationToken.None);
    }
    private static AssDocument Document() => AssDocument.Parse("[Script Info]\nTitle: Preserve\n[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text,Future\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\distort(1,2)\\1grd&HFFFFFF&}日本語 မြန်မာ,evidence\n");
    private static AutomationInvocation Context(AutomationSubtitleDocument subs) => new(subs, [2], 2, new Services());

    [Fact]
    public async Task RealMacroEditsInsertsDeletesCheckpointsAndReturnsSelection()
    {
        using var script = await Load("""
script_name = "Real authoring macro"
aegisub.register_macro("Timing/Generate", "Authoring fixture", function(subs,sel,active)
  assert(#subs == subs.n and active == 2 and subs[1].class == "info")
  local line = subs[sel[1]]
  local prior = line.text
  line.start_time = 500
  subs[sel[1]] = line
  aegisub.set_undo_point("Timing")
  subs[-2] = line
  subs[0] = line
  subs.delete(3)
  aegisub.set_undo_point("Generation")
  assert(subs[2].text == prior)
  return {2,3}, 3
end, function() return true,"Dynamic help" end, function() return true end)
""");
        Assert.Equal("Real authoring macro", script.Metadata.Name);
        var macro = Assert.Single(script.Macros);
        var document = Document(); var editor = new SubtitleEditor(document); var before = document.Serialize();
        using (var validation = new AutomationSubtitleDocument(document, writable: false))
            Assert.Equal(new AutomationValidation(true, "Dynamic help", true), await script.ValidateAsync(macro.Index, Context(validation), CancellationToken.None));
        using var subs = new AutomationSubtitleDocument(document);
        var result = await script.RunAsync(macro.Index, Context(subs), CancellationToken.None);
        Assert.Equal(new[] { 2, 3 }, result.Selection); Assert.Equal(3, result.ActiveLine);
        Assert.Equal(before, document.Serialize());
        subs.Commit(editor, macro.Name);
        Assert.Equal(2, document.Events.Count);
        Assert.All(document.Events, line => { Assert.Equal(500, line.StartMilliseconds); Assert.Equal("evidence", line.Get("Future")); });
        Assert.Equal("Generation", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Single(document.Events);
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task NativeErrorsRetainTracebackAndDiscardWorkingChanges()
    {
        using var script = await Load("""
aegisub.register_macro("Broken", "", function(subs)
  local line=subs[2]; line.text="temporary"; subs[2]=line
  aegisub.set_undo_point("not persistent")
  error("intentional fixture failure")
end)
""");
        var document = Document(); var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await script.RunAsync(1, Context(subs), CancellationToken.None));
        Assert.Contains("intentional fixture failure", error.Message);
        Assert.Contains("stack traceback", error.Message);
        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task TightLoopCancelsAndInterpreterRemainsUsable()
    {
        using var script = await Load("""
aegisub.register_macro("Loop", "", function() while true do end end)
aegisub.register_macro("After", "", function(subs) return {2},2 end)
""");
        using var subs = new AutomationSubtitleDocument(Document());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await script.RunAsync(1, Context(subs), cancellation.Token));
        var after = await script.RunAsync(2, Context(subs), CancellationToken.None);
        Assert.Equal(2, after.ActiveLine);
    }

    [Fact]
    public async Task ScriptCancelRollsBackAllPointsAndOldUserdataCannotRevive()
    {
        using var script = await Load("""
local old, append
aegisub.register_macro('Cancel','',function(subs)
  old=subs; append=subs.append
  local count=0; for i,line in ipairs(subs) do count=count+1 end
  assert(count==#subs)
  local line=subs[2]; line.text='discard'; subs[2]=line
  aegisub.set_undo_point('discard checkpoint')
  aegisub.cancel()
end)
aegisub.register_macro('After','',function(subs)
  assert(not pcall(function() return #old end))
  assert(not pcall(function() return old[1] end))
  assert(not pcall(function() return ipairs(old) end))
  assert(not pcall(function() append(subs[2]) end))
  assert(subs[2].text~='discard')
end)
""");
        var document = Document(); var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await script.RunAsync(1, Context(subs), CancellationToken.None));
        Assert.Equal(before, document.Serialize());
        using var after = new AutomationSubtitleDocument(document);
        await script.RunAsync(2, Context(after), CancellationToken.None);
        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task StatesAreIsolatedAndBrokenLoadsCanRegisterNoExecutableFeatures()
    {
        using var first = await Load("global_fixture = 42; aegisub.register_macro('First','',function() end)", "first.lua");
        using var second = await Load("assert(global_fixture==nil); aegisub.register_macro('Second','',function() end)", "second.lua");
        Assert.Equal("First", Assert.Single(first.Macros).Name);
        Assert.Equal("Second", Assert.Single(second.Macros).Name);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Load("aegisub.register_macro('A','',function() end); error('load failure')", "broken.lua"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Load("this is invalid lua !", "syntax.lua"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Load("aegisub.register_macro('A','',function() end); aegisub.register_macro('A','',function() end)", "duplicate.lua"));
    }

    [Fact]
    public async Task UnicodeIncludeSupportsReturnValuesAndRequireUsesMasterDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "日本語.lua"), "return 42, 'မြန်မာ'");
        File.WriteAllText(Path.Combine(_root, "helper.lua"), "return {value=123}");
        using var script = await Load("""
assert(dofile==nil and loadfile==nil)
local a,b=include('日本語.lua'); assert(a==42 and b=='မြန်မာ')
assert(require('helper').value==123)
aegisub.register_macro('Include passed','',function() end)
""");
        Assert.Equal("Include passed", Assert.Single(script.Macros).Name);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ExactShippedModulesSupportLuaBinsMoonScriptRegexAndUnicode()
    {
        using var script = await Load("""
local bins=require('luabins')
local packed=assert(bins.save({name='日本語 မြန်မာ',number=42,nested={true,false}},'tail'))
local ok,values,tail=bins.load(packed)
assert(ok and values.name=='日本語 မြန်မာ' and values.nested[1]==true and tail=='tail')
local lpeg=require('lpeg')
assert(lpeg.match(lpeg.P('abc'), 'abcdef')==4)
local unicode=require('aegisub.unicode')
assert(unicode.len('日本語 မြန်မာ')==10)
assert(unicode.len('á😀')==3)
assert(unicode.to_upper_case('straße')=='STRASSE')
local re=require('aegisub.re')
local matches=re.find('☃☃','.')
assert(#matches==2 and matches[1].first==1 and matches[1].last==3 and matches[2].first==4)
assert(re.sub('aab','a','x')=='xxb')
local fs=require('lfs')
assert(type(fs.currentdir())=='string')
assert(fs.attributes('this-file-does-not-exist','mode')==nil)
include('utils.lua')
include('karaskel.lua')
assert(type(karaskel.collect_head)=='function' and type(karaskel.preproc_line)=='function')
aegisub.register_macro('Modules loaded','',function() end)
""", "modules.lua");
        Assert.Equal("Modules loaded", Assert.Single(script.Macros).Name);
        using var moon = await Load("aegisub.register_macro 'Moon macro', '', (subs, selected, active) ->\n  selected, active\n", "fixture.moon");
        Assert.Equal("Moon macro", Assert.Single(moon.Macros).Name);
    }

    [Fact]
    public async Task UnmodifiedKaraokeTemplaterGeneratesRealEffectsAndUndoesLosslessly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "autoload", "kara-templater.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), CancellationToken.None);
        var document = AssDocument.CreateEmpty(); var editor = new SubtitleEditor(document);
        var template = editor.Insert(null, after: true);
        template.Effect = "template syl"; template.Text = "{\\pos($scenter,$smiddle)\\k$sdur}";
        var song = editor.Insert(template, after: true); song.Effect = ""; song.Text = "{\\k20}日{\\kf30}本語";
        song.Start = "0:00:01.00"; song.End = "0:00:03.00";
        editor.ToggleComment([template]); editor.MarkSaved();
        var before = document.Serialize();
        var macro = Assert.Single(script.Macros);
        using (var validation = new AutomationSubtitleDocument(document, writable: false))
            Assert.True((await script.ValidateAsync(macro.Index, Context(validation), CancellationToken.None)).Enabled);
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(macro.Index, Context(subs), CancellationToken.None);
        Assert.Equal(before, document.Serialize());
        subs.Commit(editor, macro.Name);
        var generated = document.Events.Where(line => line.Effect == "fx").ToArray();
        Assert.Equal(2, generated.Length);
        Assert.All(generated, line => { Assert.Contains("\\pos(", line.Text); Assert.False(line.IsComment); });
        Assert.Contains(generated, line => line.Text.EndsWith("日", StringComparison.Ordinal));
        Assert.Contains(generated, line => line.Text.EndsWith("本語", StringComparison.Ordinal));
        Assert.True(song.IsComment);
        Assert.Contains(document.Styles, style => style.Name == "Default-furigana");
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
        editor.Undo.Redo(); Assert.Equal(2, document.Events.Count(line => line.Effect == "fx"));
    }

    [Fact]
    public async Task UnmodifiedTableCopyPreservesUnknownColumnsAndIgnoresCyclicHelpers()
    {
        using var script = await Load("""
include('utils.lua')
aegisub.register_macro('Copy','',function(subs)
  local line=table.copy(subs[2])
  line.text='generated'
  line.script_data={}; line.script_data.self=line.script_data
  subs.append(line)
end)
""");
        var document = Document(); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(1, Context(subs), CancellationToken.None);
        subs.Commit(editor, "Copy");
        Assert.Equal("generated", document.Events[1].Text);
        Assert.Equal("evidence", document.Events[1].Get("Future"));
    }

    [Fact]
    public async Task NativeKaraokeArrayIncludesNumericZeroAndContiguousSyllables()
    {
        using var script = await Load("""
aegisub.register_macro('Karaoke','',function(subs)
  local line=subs[2]; line.text='{\\k20}日{\\kf30}本語'
  local kara=aegisub.parse_karaoke_data(line)
  assert(type(kara[0])=='table' and kara[0].text=='' and #kara==2)
  assert(kara[1].text_stripped=='日' and kara[1].duration==200)
  assert(kara[2].text_stripped=='本語' and kara[2].start_time==200 and kara[2].duration==300)
end)
""");
        using var subs = new AutomationSubtitleDocument(Document());
        await script.RunAsync(1, Context(subs), default);
    }

    [Fact]
    public async Task UnmodifiedReleaseRegexAndUnicodeModuleSuitesPassAllAssertions()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var fixtures = new[] { Path.Combine(directory, "modules", "re.moon"), Path.Combine(directory, "modules", "unicode.moon") };
        var expected = fixtures.Sum(path => File.ReadLines(path).Count(line => line.TrimStart().StartsWith("it '", StringComparison.Ordinal)));
        Assert.True(expected > 50);
        string Include(string path) => "include([[" + path + "]])\n";
        using var script = await Load(Include(Path.Combine(directory, "module-runner.lua")) + string.Concat(fixtures.Select(Include)) +
            $"finish_module_tests({expected})\naegisub.register_macro('All upstream assertions','',function() end)");
        Assert.Single(script.Macros);
    }

    [Fact]
    public async Task UnmodifiedReleaseExportAndNameClashScriptsRunOnCopies()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "automation");
        var path = Path.Combine(directory, "basic-export-test.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), default);
        var document = AssDocument.CreateEmpty(); var editor = new SubtitleEditor(document);
        var song = editor.Insert(null, false); song.Text = "{\\k20}日{\\kf30}本語";
        var before = document.Serialize();
        var filter = AutomationExportPipeline.Filters([script]).Single(f => f.Name == "Stupid karaoke");
        var output = await AutomationExportPipeline.RunAsync(before, [new(filter, new Dictionary<string, object?>())], new Services(), default);
        Assert.Equal(2, output.Events.Count); Assert.All(output.Events, line => Assert.Contains("\\t(", line.Text));
        Assert.Contains(output.Events, line => line.Text.EndsWith("日", StringComparison.Ordinal));
        Assert.Contains(output.Events, line => line.Text.EndsWith("本語", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize());
        path = Path.Combine(directory, "test-filter-name-clash.lua");
        using var clash = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), default);
        var filters = AutomationExportPipeline.Filters([clash]);
        Assert.Equal(new[] { "Export breaker", "Export breaker (1)" }, filters.Select(f => f.Name));
        var unchanged = await AutomationExportPipeline.RunAsync(before, filters.Select(f => new AutomationFilterSettings(f, new Dictionary<string, object?>())).ToArray(), new Services(), default);
        Assert.Equal(before, unchanged.Serialize());
    }

    [Fact]
    public async Task UnmodifiedReleaseFuriganaLayoutUsesRealKaraskelAndGdi()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "automation");
        var path = Path.Combine(directory, "test-furi.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), default);
        var document = AssDocument.Load(Path.Combine(directory, "furi-test.ass")); var editor = new SubtitleEditor(document);
        var before = document.Serialize(); var count = document.Events.Count;
        using var subs = new AutomationSubtitleDocument(document);
        var macro = script.Macros.Single(m => m.Name == "Test furi layout");
        await script.RunAsync(macro.Index, new(subs, [], 0, new Services()), default);
        subs.Commit(editor, macro.Name);
        Assert.True(document.Events.Count > count); Assert.Contains(document.Styles, style => style.Name.EndsWith("-furigana", StringComparison.Ordinal));
        Assert.Contains(document.Events.Skip(count), line => line.Style.EndsWith("-furigana", StringComparison.Ordinal) && line.Text.Contains("\\pos(", StringComparison.Ordinal));
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task FileDialogTransportUsesReleaseArgumentOrderTruthinessAndUnicodeReturnTypes()
    {
        using var script = await Load("""
aegisub.register_macro('Files','',function(subs)
  assert(aegisub.dialog.open('Open','directory','name.txt','Text|*.txt')==nil)
  local paths=aegisub.dialog.open('Multiple','directory','name.txt','Text|*.txt',0,false)
  assert(#paths==2 and paths[1]=='日本語.txt' and paths[2]=='မြန်မာ.txt')
  assert(aegisub.dialog.save('Save','directory','name.txt','Text|*.txt')=='日本語.txt')
  assert(aegisub.dialog.save('Quiet','directory','name.txt','Text|*.txt',true)=='မြန်မာ.txt')
end)
""");
        List<AutomationFileDialogRequest> requests = [];
        var services = new Services(request =>
        {
            requests.Add(request);
            return requests.Count switch { 1 => null, 2 => new[] { "日本語.txt", "မြန်မာ.txt" }, 3 => new[] { "日本語.txt" }, _ => new[] { "မြန်မာ.txt" } };
        });
        using var subs = new AutomationSubtitleDocument(Document());
        await script.RunAsync(1, Context(subs) with { Services = services }, default);
        Assert.Equal(4, requests.Count);
        Assert.All(requests, request => { Assert.Equal("directory", request.DefaultDirectory); Assert.Equal("name.txt", request.DefaultFile); });
        Assert.False(requests[0].AllowMultiple); Assert.True(requests[0].MustExist);
        Assert.True(requests[1].AllowMultiple); Assert.False(requests[1].MustExist);
        Assert.True(requests[2].Save); Assert.True(requests[2].PromptOverwrite);
        Assert.True(requests[3].Save); Assert.False(requests[3].PromptOverwrite);
    }

    [Fact]
    public async Task FilterConfigurationIsReadOnlyAndProcessingGetsOnlySettingsOnACopy()
    {
        using var script = await Load("""
aegisub.register_filter('Configured','',17,function(subs,settings)
  assert(aegisub.set_undo_point==nil and aegisub.dialog==nil)
  assert(type(aegisub.progress.set)=='function')
  local line=subs[2]; line.text=settings.text; subs[2]=line
end,function(subs,old)
  assert(next(old)==nil and aegisub.progress==nil and aegisub.dialog==nil)
  local line=subs[2]
  assert(not pcall(function() subs[2]=line end))
  return {{class='edit',name='text',text='日本語 မြန်မာ',x=0,y=0},
    {class='dropdown',name='mode',items={'One','Two'},value='Two',x=0,y=1}}
end)
""");
        var document = Document(); var before = document.Serialize();
        // The provider enforces readonly configuration even for a writable view.
        using var subs = new AutomationSubtitleDocument(document);
        var filter = Assert.Single(script.Filters); Assert.Equal(17, filter.Priority);
        var controls = await script.ConfigureFilterAsync(filter.Index, Context(subs), default);
        Assert.Equal(2, controls.Count); Assert.Equal("日本語 မြန်မာ", controls[0]["text"]);
        var output = await AutomationExportPipeline.RunAsync(before,
            [new(AutomationExportPipeline.Filters([script])[0], new Dictionary<string, object?> { ["text"] = "exported" })], new Services(), default);
        Assert.Equal("exported", output.Events[0].Text); Assert.Equal("evidence", output.Events[0].Get("Future"));
        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task UnmodifiedCleanTagsFilterRunsThroughIsolatedExportChain()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "autoload", "cleantags-autoload.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), default);
        var document = Document(); var editor = new SubtitleEditor(document);
        editor.SetField(document.Events[0], "Text", "{\\b1}{\\i1}日本語", "Fixture"); editor.MarkSaved();
        var before = document.Serialize(); var revision = document.Revision;
        var filter = Assert.Single(AutomationExportPipeline.Filters([script]));
        var output = await AutomationExportPipeline.RunAsync(before, [new(filter, new Dictionary<string, object?>())], new Services(), default);
        Assert.Equal("{\\b1\\i1}日本語", output.Events[0].Text);
        Assert.Equal("evidence", output.Events[0].Get("Future"));
        Assert.Equal(before, document.Serialize()); Assert.Equal(revision, document.Revision); Assert.False(editor.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledNativeFilterDiscardsAllChanges(bool cancel)
    {
        using var script = await Load("""
aegisub.register_filter('Failure','',0,function(subs,settings)
  local line=subs[2]; line.text='partial'; subs[2]=line
  if settings.cancel then aegisub.cancel() else error('filter fixture failure') end
end)
""");
        var document = Document(); var before = document.Serialize();
        var binding = AutomationExportPipeline.Filters([script])[0];
        var error = await Record.ExceptionAsync(async () => await AutomationExportPipeline.RunAsync(before,
            [new(binding, new Dictionary<string, object?> { ["cancel"] = cancel })], new Services(), default));
        if (cancel) Assert.IsType<OperationCanceledException>(error);
        else { Assert.IsType<InvalidOperationException>(error); Assert.Contains("filter fixture failure", error.Message); }
        Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public void GdiMetricsApplyScalingAndSpacingWithoutSplittingGraphemes()
    {
        using var subs = new AutomationSubtitleDocument(AssDocument.CreateEmpty());
        var style = Enumerable.Range(1, subs.Count).Select(subs.Read).Single(line => Equals(line["class"], "style"));
        var original = WindowsAutomationTextMeasurer.Measure(style, "á😀日本語");
        Assert.True(original.Width > 0); Assert.True(original.Height > 0);
        Assert.True(original.Descent >= 0); Assert.True(original.ExternalLeading >= 0);
        style["spacing"] = 3;
        var spaced = WindowsAutomationTextMeasurer.Measure(style, "á😀日本語");
        Assert.Equal(original.Width + 15, spaced.Width, precision: 8);
        style["scale_x"] = 150; style["scale_y"] = 200;
        var scaled = WindowsAutomationTextMeasurer.Measure(style, "á😀日本語");
        Assert.Equal(spaced.Width * 1.5, scaled.Width, precision: 8);
        Assert.Equal(spaced.Height * 2, scaled.Height, precision: 8);
    }

    private sealed class Services(Func<AutomationFileDialogRequest, IReadOnlyList<string>?>? pick = null) : IAutomationHostServices
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
        public IReadOnlyList<string>? PickFiles(AutomationFileDialogRequest request, CancellationToken token) => pick?.Invoke(request);
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) { }
        public void Log(string message, int level) { }
    }
}
