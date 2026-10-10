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
        public AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) { }
        public void Log(string message, int level) { }
    }
}
