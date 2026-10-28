using Xunit;
using Yoake.Core.Automation;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.Native.Automation;

namespace Yoake.Automation.Tests;

public sealed partial class LuaProviderTests : IDisposable
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
    public async Task ModernScriptResolutionUsesCapturedDocumentAndExpiresWithSubtitleObject()
    {
        using var script=await Load("""
local retained
aegisub.register_macro('Resolution','',function(subs)
  local x,y=subs.script_resolution();assert(x==1280 and y==1024)
  retained=subs
end)
aegisub.register_macro('Expired','',function()
  local ok=pcall(function() return retained.script_resolution() end);assert(not ok)
end)
""");
        var doc=AssDocument.Parse("[Script Info]\nPlayResX: 1280\n[Events]\nFormat: Start, End, Text\nDialogue: 0:00:00.00,0:00:01.00,test\n");
        using(var subs=new AutomationSubtitleDocument(doc))await script.RunAsync(1,Context(subs),CancellationToken.None);
        using(var subs=new AutomationSubtitleDocument(doc))await script.RunAsync(2,Context(subs),CancellationToken.None);
    }

    [Fact]
    public async Task Aegisub351TemplaterUsesItsOwnKaraskelAndGeneratesEffects()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"automation-3.5-above");
        var path=Path.Combine(root,"autoload","kara-templater.lua");
        using var script=await _provider.LoadAsync(path,new(path,[Path.Combine(root,"include")]),CancellationToken.None);
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);
        var template=editor.Insert(null,true);template.Effect="template syl noblank";template.Text=@"{\k$sdur}";
        var song=editor.Insert(template,true);song.Effect="";song.Text=@"{\k20}日{\kf30}本語";song.Start="0:00:01.00";song.End="0:00:03.00";
        editor.ToggleComment([template]);var before=doc.Serialize();using var subs=new AutomationSubtitleDocument(doc);
        var macro=Assert.Single(script.Macros);await script.RunAsync(macro.Index,Context(subs),CancellationToken.None);
        Assert.Equal(before,doc.Serialize());subs.Commit(editor,macro.Name);
        var generated=doc.Events.Where(line=>line.Effect=="fx").ToArray();
        Assert.Contains(generated,line=>line.Text.EndsWith("日",StringComparison.Ordinal));
        Assert.Contains(generated,line=>line.Text.EndsWith("本語",StringComparison.Ordinal));
        editor.Undo.Undo();Assert.Equal(before,doc.Serialize());
    }

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
aegisub.register_macro("Loop", "", function() jit.on(); assert(not jit.status()); while true do end end)
aegisub.register_macro("After", "", function(subs) return {2},2 end)
""");
        using var subs = new AutomationSubtitleDocument(Document());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await script.RunAsync(1, Context(subs), cancellation.Token));
        var after = await script.RunAsync(2, Context(subs), CancellationToken.None);
        Assert.Equal(2, after.ActiveLine);
    }

    [Fact]
    public async Task ReturnedSelectionUsesLuaNumericCoercionAndStopsAtInvalidRows()
    {
        using var script = await Load("""
aegisub.register_macro('Coerce','',function(subs)
  local line=subs[2]; line.text='generated'; subs.append(line)
  local cycle={}; cycle.self=cycle
  return {'0x3',3,3.9,true,cycle,'ignored'},'3.9'
end)
aegisub.register_macro('Invalid','',function() return {2,999,3},999 end)
aegisub.register_macro('Non-dialogue','',function() return {1,2} end)
""");
        using var subs = new AutomationSubtitleDocument(Document()); var services = new Services();
        var context = new AutomationInvocation(subs, [2], 2, services);
        var coerced = await script.RunAsync(1, context, default);
        Assert.Equal(new[] { 3 }, coerced.Selection); Assert.Equal(3, coerced.ActiveLine);
        var invalid = await script.RunAsync(2, context, default);
        Assert.Equal(new[] { 2 }, invalid.Selection); Assert.Equal(2, invalid.ActiveLine);
        Assert.Contains(services.Logs, message => message.Contains("Active row 999", StringComparison.Ordinal));
        Assert.Contains(services.Logs, message => message.Contains("Selected row 999", StringComparison.Ordinal));
        var nonDialogue = await script.RunAsync(3, context, default);
        Assert.Empty(nonDialogue.Selection!); Assert.Contains(services.Logs, message => message.Contains("Selected row 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallbackErrorsPreserveIndependentValidationToggleAndCancellationBehavior()
    {
        using var script = await Load("""
aegisub.register_macro('Toggle fails','',function() end,function() return true,'Dynamic help' end,
  function() error('toggle fixture failure') end)
aegisub.register_macro('Validation fails','',function() end,function(subs)
  local line=subs[2]; line.text='readonly edit'; subs[2]=line
end,function() return true end)
aegisub.register_macro('Cancelled validation','',function() end,function() while true do end end)
""");
        var document = Document(); var before = document.Serialize(); var services = new Services();
        using var subs = new AutomationSubtitleDocument(document, writable: false);
        var invocation = new AutomationInvocation(subs, [2], 2, services);
        Assert.Equal(new AutomationValidation(true, "Dynamic help", false), await script.ValidateAsync(1, invocation, default));
        Assert.Contains(services.Logs, text => text.Contains("toggle fixture failure", StringComparison.Ordinal) && text.Contains("stack traceback:", StringComparison.Ordinal));
        Assert.Equal(new AutomationValidation(false, null, true), await script.ValidateAsync(2, invocation, default));
        Assert.Contains(services.Logs, text => text.Contains("read-only", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await script.ValidateAsync(3, invocation, cancellation.Token));
        Assert.True((await script.ValidateAsync(1, invocation, default)).Enabled);
    }

    [Fact]
    public async Task BrokenIncludesModulesAndAutomationThreeCannotPublishFeatures()
    {
        File.WriteAllText(Path.Combine(_root, "recursive.lua"), "include('recursive.lua')");
        foreach (var source in new[] { "include('missing.lua')", "require('missing_fixture_module')", "include('recursive.lua')", "version=3" })
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Load("aegisub.register_macro('Staged','',function() end); " + source, "broken-context.lua"));
            Assert.Contains(Path.Combine(_root, "broken-context.lua"), failure.Message);
        }
        using var healthy = await Load("aegisub.register_macro('Healthy after failures','',function() end)");
        Assert.Equal("Healthy after failures", Assert.Single(healthy.Macros).Name);
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

    [Fact]
    public async Task Lua51AndJitExtensionsWorkWithEditedUnicodeModuleSearchPaths()
    {
        var modules = Path.Combine(_root, "modules-日本語"); Directory.CreateDirectory(modules);
        File.WriteAllText(Path.Combine(modules, "preferred.lua"), "error('MoonScript must take precedence')");
        File.WriteAllText(Path.Combine(modules, "preferred.moon"), "return {value: 'မြန်မာ'}\n");
        var init = Path.Combine(modules, "nested"); Directory.CreateDirectory(init);
        File.WriteAllText(Path.Combine(init, "init.lua"), "module(...,package.seeall); value=42");
        using var script = await Load("""
assert(_VERSION=='Lua 5.1' and unpack({12,34})==12)
local proxy=newproxy(true); getmetatable(proxy).__len=function() return 17 end
assert(#proxy==17 and type(getfenv(1))=='table')
local fn=assert(loadstring('return answer')); setfenv(fn,{answer=42}); assert(fn()==42)
local co=coroutine.create(function() coroutine.yield('ready'); return 'done' end)
local ok,value=coroutine.resume(co); assert(ok and value=='ready')
ok,value=coroutine.resume(co); assert(ok and value=='done')
assert(math.mod(9,4)==1 and type(io.open)=='function' and type(os.clock)=='function')
assert(math.mod==math.fmod and string.gfind==string.gmatch)
local match=string.gfind('日本語 42','%d+'); assert(match()=='42')
local bit=require('bit'); assert(bit.bxor(0xff,0x0f)==0xf0)
local ffi=require('ffi'); local integers=ffi.new('int32_t[2]',{42,-17})
assert(tonumber(integers[0])==42 and tonumber(integers[1])==-17)
assert(not jit.status()); jit.on(); assert(not jit.status())
package.path='modules-日本語/?.lua;modules-日本語/?/init.lua;'..package.path
assert(require('preferred').value=='မြန်မာ')
assert(require('nested').value==42)
assert(require('nested')==package.loaded.nested)
aegisub.register_macro('Language and modules','',function() end)
""");
        Assert.Single(script.Macros);
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
    public async Task NativeModulesPreserveBinaryStringsAndReportDefinedInvalidInputs()
    {
        using var script = await Load("""
local bins=require('luabins')
local binary=string.char(0,255,128,1)..'日本語'
local ok,value=bins.load(assert(bins.save(binary)))
assert(ok and value==binary)
local bad,message=bins.load('not a luabins stream')
assert(not bad and type(message)=='string')
assert(not bins.save(function() end))
local lpeg=require('lpeg')
assert(lpeg.match(lpeg.P(binary),binary)==#binary+1)
local re=require('aegisub.re')
assert(not pcall(re.compile,'['))
assert(not pcall(re.compile,string.char(255)))
local unicode=require('aegisub.unicode')
-- The release's Lua character iterator assumes valid UTF-8; retain its
-- defined truncated-input behavior rather than inventing strict decoding.
assert(unicode.len(string.char(255))==1)
assert(not pcall(unicode.codepoint,string.char(255)))
assert(unicode.codepoint('😀')==0x1f600)
aegisub.register_macro('Binary modules','',function() end)
""");
        Assert.Single(script.Macros);
    }

    [Fact]
    public async Task WindowsFilesystemFixtureCoversUnicodeErrorsTimestampsAndIteratorClose()
    {
        var path = Path.Combine(_root, "filesystem-日本語 မြန်မာ").Replace('\\', '/').Replace("'", "\\'");
        using var script = await Load($$"""
local fs=require('lfs')
local root='{{path}}'
local original=assert(fs.currentdir())
local ok,problem=pcall(function()
  assert(fs.mkdir(root..'/nested'))
  assert(fs.attributes(root,'mode')=='directory')
  assert(fs.touch(root..'/日本語.txt'))
  local attributes=assert(fs.attributes(root..'/日本語.txt'))
  assert(attributes.mode=='file' and attributes.size==0)
  assert(math.abs(attributes.modification-os.time())<=2)
  assert(not pcall(fs.attributes,root,'unsupported'))
  local absent,message=fs.chdir(root..'/missing')
  assert(absent==nil and type(message)=='string' and #message>0)
  assert(fs.chdir(root))
  assert(fs.currentdir():gsub('\\','/')==root)
  local next_entry,iterator=fs.dir(root)
  local names={}
  for entry in next_entry,iterator do names[entry]=true end
  assert(names['日本語.txt'] and names.nested)
  assert(next_entry(iterator)==nil)
  iterator:close(); iterator:close(); assert(iterator:next()==nil)
  assert(not pcall(fs.dir,root..'/missing'))
  assert(fs.rmdir(root..'/nested'))
  assert(fs.attributes(root..'/nested','mode')==nil)
end)
assert(fs.chdir(original))
assert(ok,problem)
aegisub.register_macro('Filesystem fixture','',function() end)
""");
        Assert.Single(script.Macros);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public async Task ShippedKaraokeTemplaterGeneratesRealEffectsAndUndoesLosslessly(bool noBlank, int expected)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "autoload", "kara-templater.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), CancellationToken.None);
        var document = AssDocument.CreateEmpty(); var editor = new SubtitleEditor(document);
        var template = editor.Insert(null, after: true);
        template.Effect = noBlank ? "template syl noblank" : "template syl"; template.Text = "{\\pos($scenter,$smiddle)\\k$sdur}";
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
        // 3.2.2 applies syllable templates to index zero unless noblank is set.
        Assert.Equal(expected, generated.Length);
        Assert.Equal(noBlank ? 0 : 1, generated.Count(line => line.Text.EndsWith("\\k0}", StringComparison.Ordinal)));
        Assert.All(generated, line => { Assert.Contains("\\pos(", line.Text); Assert.False(line.IsComment); });
        Assert.Contains(generated, line => line.Text.EndsWith("日", StringComparison.Ordinal));
        Assert.Contains(generated, line => line.Text.EndsWith("本語", StringComparison.Ordinal));
        Assert.True(song.IsComment);
        Assert.Contains(document.Styles, style => style.Name == "Default-furigana");
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
        editor.Undo.Redo(); Assert.Equal(expected, document.Events.Count(line => line.Effect == "fx"));
    }

    [Fact]
    public async Task MangetsuKaraskelAdapterRetainsTimingAndFallsBackForLegacySyntax()
    {
        using var script=await Load("""
local adapter=include('mangetsu-karaskel.lua')
local function check(text,expected)
  local result=assert(adapter.parse_karaoke_data({text=text}))
  assert(#result==#expected)
  for i,e in ipairs(expected) do
    assert(result[i].text_stripped==e[1] and result[i].start_time==e[2] and result[i].duration==e[3])
  end
end
check('<噛|{\\k60}か>ん{\\k70}だ',{{'噛|<か',0,600},{'ん',0,600},{'だ',600,700}})
check('<水|{\\k30}みず><鏡|{\\k40}かがみ>',{{'水|<みず',0,300},{'鏡|<かがみ',300,400}})
check('<病|{\\k30}や{\\k26}ま{\\k10}い>',{{'病|<や',0,300},{'#|ま',300,260},{'#|い',560,100}})
check('{\\k50}<love|ai>',{{'love|<ai',0,500}})
assert(adapter.parse_karaoke_data({text='漢字|<かな'})==nil)
assert(adapter.parse_karaoke_data({text='{\\k20}ordinary'})==nil)
assert(adapter.parse_karaoke_data({text='<漢字|かな|KANJI>'})==nil)
aegisub.register_macro('Adapter fixture','',function() end)
""");
        Assert.Single(script.Macros);
    }

    [Fact]
    public async Task KaraokeTemplaterProcessesNativeMangetsuFuriganaAndUndoesLosslessly()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"autoload","kara-templater.lua");
        using var script=await _provider.LoadAsync(path,new(path,[Path.Combine(AppContext.BaseDirectory,"include")]),CancellationToken.None);
        var document=AssDocument.CreateEmpty();var editor=new SubtitleEditor(document);
        var template=editor.Insert(null,true);template.Effect="template furi";template.Text=@"{\k$kdur}";
        var song=editor.Insert(template,true);song.Effect="";song.Text=@"<病|{\k30}や{\k26}ま{\k10}い>";song.Start="0:00:01.00";song.End="0:00:03.00";
        editor.ToggleComment([template]);
        editor.MarkSaved();var before=document.Serialize();using var subs=new AutomationSubtitleDocument(document);
        var macro=Assert.Single(script.Macros);await script.RunAsync(macro.Index,Context(subs),CancellationToken.None);
        Assert.Equal(before,document.Serialize());subs.Commit(editor,macro.Name);
        var generated=document.Events.Where(line=>line.Effect=="fx").ToArray();
        Assert.Contains(generated,line=>line.Text.EndsWith("や",StringComparison.Ordinal));
        Assert.Contains(generated,line=>line.Text.EndsWith("ま",StringComparison.Ordinal));
        Assert.Contains(generated,line=>line.Text.EndsWith("い",StringComparison.Ordinal));
        Assert.Equal(@"<病|{\k30}や{\k26}ま{\k10}い>",song.Text);
        editor.Undo.Undo();Assert.Equal(before,document.Serialize());editor.Undo.Redo();Assert.Equal(generated.Length,document.Events.Count(line=>line.Effect=="fx"));
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
    public async Task AmbiguousCopiedRawLinesCannotSilentlyRenameUnknownColumns()
    {
        using var script = await Load("""
include('utils.lua')
aegisub.register_macro('Safe copy','',function(subs)
  local first,second=subs[2],subs[3]
  assert(first.raw==second.raw)
  local copied=table.copy(second); copied.text='generated'
  local ok,message=pcall(subs.append,copied)
  assert(not ok and message:match('ambiguous ASS Format provenance'))
  -- Original detached tables still carry exact identities across schemas.
  second.text='generated'; subs.append(second)
end)
""");
        const string fields = "Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text,";
        const string raw = "Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,same,evidence\n";
        var document = AssDocument.Parse("[Script Info]\nTitle: Preserve\n[Events]\nFormat: " + fields + "FirstSecret\n" + raw +
            "Format: " + fields + "SecondSecret\n" + raw);
        var editor = new SubtitleEditor(document); var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(1, Context(subs), default);
        subs.Commit(editor, "Safe copy");
        Assert.Equal(3, document.Events.Count);
        Assert.Equal("generated", document.Events[2].Text);
        Assert.Equal("evidence", document.Events[2].Get("SecondSecret"));
        Assert.Equal("", document.Events[2].Get("FirstSecret"));
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task RealLuaGeneratesFiveThousandEffectsInOneUndoTransaction()
    {
        using var script = await Load("""
aegisub.register_macro('Bulk effects','',function(subs,selected,active)
  local line=subs[active]
  line.effect='fx'
  for i=1,5000 do
    line.text='{\\pos('..i..',100)\\distort(0,0,1,1)}日本語 မြန်မာ'
    line.start_time=i*10; line.end_time=i*10+1000
    subs.append(line)
  end
  aegisub.set_undo_point('Bulk effects')
  return {#subs},#subs
end)
""");
        var document = Document(); var editor = new SubtitleEditor(document); var before = document.Serialize();
        var original = document.Events[0];
        using var subs = new AutomationSubtitleDocument(document);
        var result = await script.RunAsync(1, Context(subs), default);
        Assert.Equal(5002, result.ActiveLine);
        Assert.Single(document.Events); // Worker generation never streams partial live edits.
        subs.Commit(editor, "Bulk effects");
        Assert.Equal(5001, document.Events.Count);
        Assert.All(document.Events.Skip(1), line => { Assert.Equal("fx", line.Effect); Assert.Equal("evidence", line.Get("Future")); });
        Assert.Contains("\\pos(5000,100)", document.Events[^1].Text);
        Assert.Equal(50000, document.Events[^1].StartMilliseconds);
        Assert.Equal("Bulk effects", editor.Undo.NextUndoName);
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize()); Assert.Same(original, Assert.Single(document.Events));
        editor.Undo.Redo(); Assert.Equal(5001, document.Events.Count);
    }

    [Fact]
    public async Task NumericFieldsAndMutationArgumentsUseLuaCoercionIncludingHexStrings()
    {
        using var script = await Load("""
aegisub.register_macro('Numeric strings','',function(subs)
  assert(not pcall(function() return subs['2'] end)) -- read indexes use actual numbers
  local line=subs[2]; line.layer='0x10'; line.start_time='0x64'; line.end_time='0xc8'
  line.margin_l='0xa'; subs['0x2']=line
  subs.insert('0x3',line); subs.delete({'0x3'})
  subs.deleterange('0x3','0x3')
  assert(subs[2].layer==16 and subs[2].margin_l==10 and #subs==2)
  assert(aegisub.frame_from_ms('0x11')==nil and aegisub.ms_from_frame('0x11')==nil)
  aegisub.progress.set('0x11')
end)
""");
        var document = Document(); var editor = new SubtitleEditor(document); var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(1, Context(subs), default); subs.Commit(editor, "Numeric strings");
        Assert.Equal("16", document.Events[0].Layer); Assert.Equal("10", document.Events[0].MarginL);
        Assert.Equal(100, document.Events[0].StartMilliseconds); Assert.Equal(200, document.Events[0].EndMilliseconds);
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task ReleaseRetimingFixtureGeneratesAllModesAndRestoresExactSource()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "autoload", "kara-templater.lua");
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), default);
        var document = AssDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "automation", "kara-templater-retime.ass"));
        var editor = new SubtitleEditor(document); var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(script.Macros.Single().Index, new(subs, [], 0, new Services()), default);
        subs.Commit(editor, "Retiming fixture");
        var generated = document.Events.Where(e => e.Effect == "fx").ToArray();
        Assert.Equal(27, generated.Length); // Nine noblank templates × three syllables.
        Assert.Equal(3, generated.Count(e => e.StartMilliseconds == 1000 && e.EndMilliseconds == 2000));
        Assert.Contains(generated, e => e.StartMilliseconds == 10000 && e.EndMilliseconds == 10300 && e.Text.EndsWith("ha", StringComparison.Ordinal));
        Assert.Contains(generated, e => e.StartMilliseconds == 10300 && e.EndMilliseconds == 10500 && e.Text.EndsWith("hi", StringComparison.Ordinal));
        Assert.Contains(generated, e => e.StartMilliseconds == 10500 && e.EndMilliseconds == 11000 && e.Text.EndsWith("ho", StringComparison.Ordinal));
        Assert.All(generated, e => { Assert.False(e.IsComment); Assert.Contains("\\pos(", e.Text); });
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
        editor.Undo.Redo(); Assert.Equal(27, document.Events.Count(e => e.Effect == "fx"));
    }

    [Fact]
    public async Task TextExtentsAtInitializationRequiresACompleteStyleAndNoDocumentContext()
    {
        using var script = await Load("""
local style={class='style',name='Default',fontname='Arial',fontsize=30,
 color1='&H00FFFFFF&',color2='&H00FFFFFF&',color3='&H00000000&',color4='&H00000000&',
 bold=false,italic=false,underline=false,strikeout=false,scale_x=100,scale_y=100,
 spacing=0,angle=0,borderstyle=1,outline=1,shadow=0,align=2,encoding=1,
 margin_l=0,margin_r=0,margin_t=0}
local w,h,d,e=aegisub.text_extents(style,'日本語')
assert(w>0 and h>0 and d>=0 and e>=0)
style.outline=nil
assert(not pcall(aegisub.text_extents,style,'missing field'))
style.class='dialogue'
assert(not pcall(aegisub.text_extents,style,'wrong class'))
aegisub.register_macro('Initialized metrics','',function() end)
""");
        Assert.Single(script.Macros);
    }

    [Fact]
    public async Task MoonScriptFailuresReportOriginalSourceLineAndFullUnicodePath()
    {
        const string name = "日本語-trace.moon";
        using var script = await Load("aegisub.register_macro 'Moon error', '', ->\n  value = 1\n  error 'moon fixture failure'\n", name);
        using var subs = new AutomationSubtitleDocument(Document());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await script.RunAsync(1, Context(subs), default));
        Assert.Contains("moon fixture failure", error.Message); Assert.Contains("stack traceback:", error.Message);
        Assert.True(error.Message.Contains(Path.Combine(_root, name) + ":3:", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public async Task MoonScriptLocationTrackingRetainsTightLoopCancellationAndReuse()
    {
        using var script = await Load("aegisub.register_macro 'Loop', '', ->\n  while true\n    value = 1\naegisub.register_macro 'Reuse', '', ->\n  value = 2\n", "cancel.moon");
        using var subs = new AutomationSubtitleDocument(Document());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await script.RunAsync(1, Context(subs), cancellation.Token));
        await script.RunAsync(2, Context(subs), default);
    }

    [Fact]
    public async Task InitializationClipboardHasPlatformServicesWithoutInstallingDocumentContext()
    {
        const string source = """
local clipboard=require('aegisub.clipboard')
assert(clipboard.get()==nil)
assert(clipboard.set('日本語 မြန်မာ é 😀'))
assert(clipboard.get()=='日本語 မြန်မာ é 😀')
assert(aegisub.file_name()==nil and aegisub.project_properties()==nil)
assert(aegisub.video_size()==nil and aegisub.keyframes()==nil)
assert(aegisub.frame_from_ms(100)==nil and aegisub.ms_from_frame(1)==nil)
aegisub.register_macro('No video','',function()
  assert(aegisub.video_size()==nil and aegisub.frame_from_ms(100)==nil and aegisub.ms_from_frame(1)==nil)
  assert(type(aegisub.keyframes())=='table' and #aegisub.keyframes()==0)
  assert(type(aegisub.project_properties())=='table' and aegisub.file_name()==nil)
end)
""";
        var path = Path.Combine(_root, "platform.lua"); File.WriteAllText(path, source);
        var services = new Services(clipboardAvailable: true);
        using var script = await _provider.LoadAsync(path, new(path, [Path.Combine(AppContext.BaseDirectory, "include")]), services, default);
        using var subs = new AutomationSubtitleDocument(Document());
        await script.RunAsync(1, new(subs, [], 0, services), default);
        using var unavailable = await Load("local c=require('aegisub.clipboard'); assert(c.get()==nil and c.set('x')==false)", "no-platform.lua");
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

    [Fact]
    public async Task SuppressedTracesDoNotFormatOrReachTheHost()
    {
        using var script = await Load("""
aegisub.register_macro('Trace','',function()
  aegisub.debug.out(5, '%d', {}) -- Formatting this suppressed value would fail.
  aegisub.log(4, 'hidden')
  aegisub.log(3.9, 'visible %s', '日本語') -- lua_tointeger truncation.
end)
""");
        var services = new Services();
        using var subs = new AutomationSubtitleDocument(Document());
        await script.RunAsync(1, Context(subs) with { Services = services }, default);
        Assert.Equal("visible 日本語", Assert.Single(services.Logs));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    public async Task InvocationTraceLevelCanRetainVerboseDiagnostics(int traceLevel, int expected)
    {
        using var script = await Load("""
aegisub.register_macro('Trace','',function()
  aegisub.debug.out(5, 'verbose %s', 'diagnostic')
  aegisub.log('visible')
end)
""");
        var services = new Services(traceLevel: traceLevel);
        using var subs = new AutomationSubtitleDocument(Document());
        await script.RunAsync(1, Context(subs) with { Services = services }, default);
        Assert.Equal(expected, services.Logs.Count);
        Assert.Contains("visible", services.Logs);
        if (traceLevel == 6) Assert.Contains("verbose diagnostic", services.Logs);
    }

    [Fact]
    public async Task NativeRepliesPreserveNilArityBinaryStringsDetachedTablesAndErrorRecovery()
    {
        using var script = await Load("""
aegisub.register_macro('Reply data','',function(subs)
  local a,b=subs[2],subs[2]
  assert(a~=b and a.extra~=b.extra)
  assert(a.text=='quotes " \\ nul\0 日本語 မြန်မာ')
  a.text='detached'; assert(b.text==subs[2].text)
  assert(select('#',aegisub.video_size())==1 and aegisub.video_size()==nil)
  local ok,message=pcall(function() return subs[999999] end)
  assert(not ok and message:match('out%-of%-range'))
  assert(subs[2].text==b.text)
  b.text=b.text..' changed'; subs[2]=b
  aegisub.set_undo_point('Reply data')
end)
""");
        var source = Document().Serialize();
        var document = AssDocument.Parse(source);
        document.Events[0].Text = "quotes \" \\ nul\0 日本語 မြန်မာ";
        var before = document.Serialize(); var editor = new SubtitleEditor(document);
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(1, Context(subs), default);
        subs.Commit(editor, "Reply data");
        Assert.Equal("quotes \" \\ nul\0 日本語 မြန်မာ changed", document.Events[0].Text);
        Assert.Equal("evidence", document.Events[0].Get("Future"));
        editor.Undo.Undo(); Assert.Equal(before, document.Serialize());
    }

    [Fact]
    public async Task NativeReplyBuffersSurviveHostCallsFromGcFinalizers()
    {
        using var script = await Load("""
aegisub.register_macro('Finalizer replies','',function(subs)
  local expected=subs[2].text
  local finalized=0
  local pause=collectgarbage('setpause',50)
  for i=1,1000 do
    local proxy=newproxy(true)
    getmetatable(proxy).__gc=function()
      finalized=finalized+1
      assert(subs[2].text==expected)
    end
    local line=subs[2]
    assert(line.text==expected and line.extra~=nil)
    proxy=nil -- No live VM register may retain the final iteration's userdata.
  end
  collectgarbage('collect')
  collectgarbage('setpause',pause)
  assert(finalized==1000)
end)
""");
        var document = Document(); document.Events[0].Text = new string('x', 4096) + " 日本語 မြန်မာ";
        var before = document.Serialize();
        using var subs = new AutomationSubtitleDocument(document);
        await script.RunAsync(1, Context(subs), default);
        Assert.Equal(before, document.Serialize());
    }

    private sealed class Services(Func<AutomationFileDialogRequest, IReadOnlyList<string>?>? pick = null, bool clipboardAvailable = false, int traceLevel = 3) : IAutomationHostServices
    {
        public int TraceLevel => traceLevel;
        private string? _clipboard;
        public List<string> Logs { get; } = [];
        public string? FileName => null;
        public IReadOnlyDictionary<string, object?> ProjectProperties => new Dictionary<string, object?>();
        public int? FrameFromMilliseconds(int milliseconds) => null;
        public int? MillisecondsFromFrame(int frame) => null;
        public AutomationVideoSize? VideoSize => null;
        public IReadOnlyList<int> Keyframes => [];
        public AutomationTextMetrics MeasureText(AutomationLine style, string text) => WindowsAutomationTextMeasurer.Measure(style, text);
        public string Translate(string text) => text;
        public string? ClipboardGet() => _clipboard;
        public bool ClipboardSet(string text) { if (!clipboardAvailable) return false; _clipboard = text; return true; }
        public AutomationDialogResult DisplayDialog(AutomationDialogRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public IReadOnlyList<string>? PickFiles(AutomationFileDialogRequest request, CancellationToken token) => pick?.Invoke(request);
        public void ReportProgress(double? percent = null, string? task = null, string? title = null) { }
        public void Log(string message, int level) => Logs.Add(message);
    }
}
