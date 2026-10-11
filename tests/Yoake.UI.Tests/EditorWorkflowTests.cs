using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;
using Yoake.Core.Undo;
using Yoake.Core.Workspace;
using Yoake.UI.Services;
using Yoake.UI.ViewModels;

namespace Yoake.UI.Tests;

public sealed class EditorWorkflowTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"Yoake-tests-"+Guid.NewGuid());
    private readonly WorkspaceManager _workspace=new();
    private readonly MainWindowViewModel _model;
    private readonly TestDialogs _dialogs=new();
    public EditorWorkflowTests()
    {
        Directory.CreateDirectory(_root);
        _model=new(new CommandRegistry(),_workspace,new UndoManager(),new ThemeService(),new SettingsStore(Path.Combine(_root,"settings.json")),new AppSettings());
        _model.Dialogs=_dialogs;
    }
    private async Task Command(string id,object? parameter=null)=>Assert.True(await _model.Registry.InvokeAsync(id,new(),parameter));
    [Fact] public async Task TimecodesAreDocumentLocalMetadataWithAtomicValidationCancellationExportAndPersistedRecents()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="{\\future(x)\\distort(0,0,1,1)}日本語 é မြန်မာ";await Command(CommandIds.EditCommit);
        var document=_model.ActiveDocumentId;var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        _model.CurrentTimeSeconds=.7;var originalStart=_model.Draft!.Start;_model.Draft.Start="unfinished";var draft=_model.Draft;var selection=_model.SelectedEvents.ToArray();
        var input=Path.Combine(_root,"日本語.tc.txt");File.WriteAllText(input,"# timecode format v1\nAssume 25\n0,1,50\n");
        Assert.False(_model.CanShiftByFrames);Assert.False(_model.Actions["timecode/close"].CanExecute(null));Assert.False(_model.Actions["timecode/save"].CanExecute(null));
        Assert.False(await _model.Registry.InvokeAsync("timecode/open",new(Guid.NewGuid()),input));
        await Command("timecode/open",input);Assert.Equal(input,_model.ExternalTimecodePath);Assert.True(_model.CanShiftByFrames);Assert.Empty(_model.FrameTimes);
        Assert.True(_model.Actions["timecode/save"].CanExecute(null));Assert.False(_model.Actions["time/snap/start_video"].CanExecute(null));Assert.False(_model.Actions["edit/line/split/before"].CanExecute(null));
        Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Equal(selection,_model.SelectedEvents);Assert.Equal(.7,_model.CurrentTimeSeconds);
        Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        var exported=Path.Combine(_root,"export.tc.txt");await Command("timecode/save",exported);Assert.Equal(new long[]{0,20,40},Yoake.Core.Media.TimecodeFile.Load(exported).Milliseconds);
        Assert.Equal(exported,_model.RecentTimecodeItems[0].Path);Assert.Equal("timecode/open",_model.RecentTimecodeItems[0].Command.CommandId);
        var bad=Path.Combine(_root,"bad.tc.txt");File.WriteAllText(bad,"# timecode format v2\n0\nNaN");
        Assert.False(await _model.Registry.InvokeAsync("timecode/open",new(),bad));Assert.Equal(input,_model.ExternalTimecodePath);Assert.Same(draft,_model.Draft);Assert.Equal(source,editor.Document.Serialize());
        _model.Draft.Start=originalStart;await Command(CommandIds.SubtitleNew);Assert.False(_model.CanShiftByFrames);Assert.Null(_model.ExternalTimecodePath);
        await Command(CommandIds.WorkspaceActivateTab,document);Assert.True(_model.CanShiftByFrames);Assert.Equal(input,_model.ExternalTimecodePath);
        await Command("timecode/close");Assert.False(_model.CanShiftByFrames);Assert.False(_model.Actions["timecode/save"].CanExecute(null));
        System.ComponentModel.PropertyChangedEventHandler cancel=(_,args)=>{if(args.PropertyName==nameof(MainWindowViewModel.HasTimecodeOperation)&&_model.HasTimecodeOperation)_=_model.Registry.InvokeAsync("timecode/close",new());};
        _model.PropertyChanged+=cancel;await Command("timecode/open",input);_model.PropertyChanged-=cancel;Assert.Null(_model.ExternalTimecodePath);Assert.False(_model.CanShiftByFrames);
        File.Delete(exported);Assert.False(await _model.Registry.InvokeAsync("timecode/open",new(),exported));Assert.DoesNotContain(_model.RecentTimecodeItems,item=>item.Path==exported);
        var restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Contains(input,restored.RecentTimecodes!);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        File.WriteAllText(Path.Combine(_root,"settings.json"),"""{"recentTimecodes":[null,7,"日本語.tc.txt","日本語.tc.txt"],"toolbarVisible":false}""");
        restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Equal(new[]{"日本語.tc.txt"},restored.RecentTimecodes);Assert.False(restored.ToolbarVisible);
    }
    [Fact] public async Task ExternalTimecodesEnableUndoableFrameShiftingWithoutVideoAndCloseDisablesIt()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;var editor=_model.ActiveEditor!;
        editor.SetTiming(line,40,120);editor.SetField(line,"Text","{\\future(x)\\distort(0,0,1,1)}é 日本語","Set text");
        var source=editor.Document.Serialize();var path=Path.Combine(_root,"frames.tc.txt");File.WriteAllText(path,"Assume 25\n0,1,50");await Command("timecode/open",path);
        await Command("time/shift/apply",new SubtitleShiftToolRequest(new(){ByFrames=true,Frames=1},_model.SelectedEvents.ToArray()));
        Assert.NotEqual(source,editor.Document.Serialize());Assert.Contains("\\future(x)\\distort(0,0,1,1)",line.Text);await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        await Command("timecode/close");Assert.False(await _model.Registry.InvokeAsync("time/shift/apply",new(),new SubtitleShiftToolRequest(new(){ByFrames=true,Frames=1},_model.SelectedEvents.ToArray())));
        Assert.Equal(source,editor.Document.Serialize());
    }
    [Fact] public async Task KeyframeCommandsAreDocumentLocalCancellableNonDestructiveAndPersistSemanticRecents()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="{\\future(x)\\distort(0,0,1,1)}日本語 é";await Command(CommandIds.EditCommit);
        var document=_model.ActiveDocumentId;var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        _model.CurrentTimeSeconds=.7;_model.Draft!.Actor="unfinished မြန်မာ";var draft=_model.Draft;var selection=_model.SelectedEvents.ToArray();
        var input=Path.Combine(_root,"日本語.key.txt");File.WriteAllText(input,"# WWXD log file, using qpfile format\n0 I -1\n5 P -1\n7 I -1\n");
        Assert.False(_model.Actions["keyframe/close"].CanExecute(null));Assert.False(_model.Actions["keyframe/save"].CanExecute(null));
        Assert.False(await _model.Registry.InvokeAsync("keyframe/open",new(Guid.NewGuid()),input));
        await Command("keyframe/open",input);Assert.Equal(new[]{0,7},_model.Keyframes);Assert.Equal(input,_model.ExternalKeyframePath);
        Assert.True(_model.Actions["keyframe/save"].CanExecute(null));Assert.True(_model.Actions["keyframe/close"].CanExecute(null));
        Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Equal(selection,_model.SelectedEvents);Assert.Equal(.7,_model.CurrentTimeSeconds);
        Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        var exported=Path.Combine(_root,"export.key.txt");await Command("keyframe/save",exported);Assert.Equal(new[]{0,7},Yoake.Core.Media.KeyframeFile.Load(exported));
        Assert.Equal(exported,_model.RecentKeyframeItems[0].Path);Assert.Equal("keyframe/open",_model.RecentKeyframeItems[0].Command.CommandId);
        var malformed=Path.Combine(_root,"bad.key.txt");File.WriteAllText(malformed,"bad header");
        Assert.False(await _model.Registry.InvokeAsync("keyframe/open",new(),malformed));Assert.Equal(new[]{0,7},_model.Keyframes);Assert.Equal(input,_model.ExternalKeyframePath);
        _model.Draft.Actor="";await Command(CommandIds.SubtitleNew);Assert.Empty(_model.Keyframes);Assert.Null(_model.ExternalKeyframePath);
        await Command(CommandIds.WorkspaceActivateTab,document);Assert.Equal(new[]{0,7},_model.Keyframes);Assert.Equal(input,_model.ExternalKeyframePath);
        await Command("keyframe/close");Assert.Empty(_model.Keyframes);Assert.Null(_model.ExternalKeyframePath);Assert.False(_model.Actions["keyframe/close"].CanExecute(null));
        System.ComponentModel.PropertyChangedEventHandler cancel=(_,args)=>{if(args.PropertyName==nameof(MainWindowViewModel.HasKeyframeOperation)&&_model.HasKeyframeOperation)_=_model.Registry.InvokeAsync("keyframe/close",new());};
        _model.PropertyChanged+=cancel;await Command("keyframe/open",input);_model.PropertyChanged-=cancel;Assert.Empty(_model.Keyframes);Assert.Null(_model.ExternalKeyframePath);
        File.Delete(exported);Assert.False(await _model.Registry.InvokeAsync("keyframe/open",new(),exported));Assert.DoesNotContain(_model.RecentKeyframeItems,item=>item.Path==exported);
        var restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Contains(input,restored.RecentKeyframes!);Assert.Equal(history,editor.Undo.NextUndoName);Assert.Equal(source,editor.Document.Serialize());
        File.WriteAllText(Path.Combine(_root,"settings.json"),"""{"recentKeyframes":[null,7,"日本語.key.txt","日本語.key.txt"],"toolbarVisible":false}""");
        restored=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Equal(new[]{"日本語.key.txt"},restored.RecentKeyframes);Assert.False(restored.ToolbarVisible);
    }
    [Fact] public async Task TimingCommandsOwnSelectedScopeHistoryCancellationAndAbsentVideoAvailability()
    {
        var path=Path.Combine(_root,"timing.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\future(x)\\distort(0,0,1,1)}日本語\n"+
            "Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0,0,0,,မြန်မာ é\n"+
            "Dialogue: 0,0:00:05.00,0:00:06.00,Default,,0,0,0,,漢\n");
        Assert.True(_model.OpenSubtitle(path));var rows=_model.Events.ToArray();var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        _model.SelectedEvent=rows[0];_model.SetSelectedEvents([rows[0],rows[2]]);
        Assert.False(_model.Actions["time/continuous/start"].CanExecute(null));
        foreach(var id in new[]{"time/snap/start_video","time/snap/end_video","time/snap/scene","time/frame/current"})Assert.False(_model.Actions[id].CanExecute(null));
        Assert.Equal("Ctrl-3",_model.Actions["time/snap/start_video"].Shortcut);Assert.Equal("Ctrl-I",_model.Actions["time/shift"].Shortcut);
        var preferences=new SubtitleShiftPreferences{Milliseconds=100,Scope=SubtitleTimingScope.Selected};
        await Command("time/shift/apply",new SubtitleShiftToolRequest(preferences,[rows[0],rows[2]]));
        Assert.Equal(1100,rows[0].StartMilliseconds);Assert.Equal(3000,rows[1].StartMilliseconds);Assert.Equal(5100,rows[2].StartMilliseconds);
        Assert.Same(rows[0],_model.SelectedEvent);Assert.Equal(2,_model.SelectedEvents.Count);Assert.Single(_model.ShiftHistory);
        Assert.Equal(preferences,new SettingsStore(Path.Combine(_root,"settings.json")).Load().ShiftPreferences);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        _model.SelectedEvent=rows[1];_model.SetSelectedEvents([rows[1]]);await Command("time/continuous/start");Assert.Equal(2000,rows[1].StartMilliseconds);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        await Command("time/shift/history/clear");Assert.Empty(_model.ShiftHistory);Assert.False(_model.Actions["time/shift/history/clear"].CanExecute(null));
        _model.Draft!.Text="unfinished {\\future(x)}日本語";var pending=_model.Draft;
        System.ComponentModel.PropertyChangedEventHandler cancel=(_,args)=>{if(args.PropertyName==nameof(MainWindowViewModel.HasEditOperation)&&_model.HasEditOperation)_=_model.Registry.InvokeAsync("editor/cancel-operation",new());};
        _model.PropertyChanged+=cancel;await Command("time/shift/apply",new SubtitleShiftToolRequest(preferences,[rows[1]]));_model.PropertyChanged-=cancel;
        Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);Assert.Empty(_model.ShiftHistory);Assert.False(_model.HasEditOperation);
        Assert.Same(pending,_model.Draft);Assert.True(pending.IsChanged);
        Assert.False(await _model.Registry.InvokeAsync("time/shift/apply",new(),new SubtitleShiftToolRequest(preferences with{Milliseconds=5000,Fields=SubtitleTimingFields.Start},[rows[1]])));
        Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        Assert.Same(pending,_model.Draft);Assert.True(pending.IsChanged);
        await Command("time/shift/apply",new SubtitleShiftToolRequest(preferences,[rows[1]]));Assert.Equal(pending.Text,rows[1].Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
    }
    [Fact] public async Task VideoAutoseekIsPersistedCheckedStateWithoutChangingDraftHistoryOrPosition()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="unfinished 日本語";_model.CurrentTimeSeconds=.5;
        var editor=_model.ActiveEditor!;var draft=_model.Draft;var line=_model.SelectedEvent;var history=editor.Undo.NextUndoName;
        Assert.True(_model.VideoAutoSeek);Assert.True(_model.Actions["video/autoseek"].IsChecked);await Command("video/autoseek");
        Assert.False(_model.VideoAutoSeek);Assert.False(_model.Actions["video/autoseek"].IsChecked);Assert.Equal(.5,_model.CurrentTimeSeconds);
        Assert.Same(draft,_model.Draft);Assert.Same(line,_model.SelectedEvent);Assert.Equal(history,editor.Undo.NextUndoName);
        var store=new SettingsStore(Path.Combine(_root,"settings.json"));Assert.False(store.Load().VideoAutoSeek);
        File.WriteAllText(store.Path,"""{"videoAutoSeek":"bad","toolbarVisible":false}""");Assert.True(store.Load().VideoAutoSeek);Assert.False(store.Load().ToolbarVisible);
    }
    [Fact] public async Task SearchCommandsPreserveSelectedScopeAndOwnAtomicVisibleTextReplacement()
    {
        var path=Path.Combine(_root,"search.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,a{\\future(x)\\distort(0,0,1,1)}bc abc\n"+
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,abc unselected\n"+
            "Dialogue: 0,0:00:02.00,0:00:03.00,Default,,0,0,0,,abc é 👩‍👩‍👧‍👦\n");
        Assert.True(_model.OpenSubtitle(path));var rows=_model.Events.ToArray();var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        _model.SelectedEvent=rows[0];_model.SetSelectedEvents([rows[0],rows[2]]);
        var preferences=new SubtitleSearchPreferences{Query="abc",SelectionOnly=true,Replacement="日本語"};
        await Command("search/next",new SubtitleSearchRequest(preferences,[rows[0],rows[2]]));
        Assert.Same(rows[0],_model.SelectedEvent);Assert.Equal(2,_model.SelectedEvents.Count);Assert.Equal(0,_model.TextSelectionStart);Assert.True(_model.TextSelectionEnd>3);Assert.False(editor.Undo.CanUndo);
        await Command(CommandIds.FindNext);Assert.Same(rows[0],_model.SelectedEvent);Assert.True(_model.TextSelectionStart>0);
        await Command(CommandIds.FindNext);Assert.Same(rows[2],_model.SelectedEvent);Assert.Equal(2,_model.SelectedEvents.Count);
        Assert.Equal("F3",_model.Actions[CommandIds.FindNext].Shortcut);Assert.Equal("Ctrl-H",_model.Actions[CommandIds.FindReplace].Shortcut);
        await Command("search/replace-all",new SubtitleSearchRequest(preferences,[rows[0],rows[2]]));Assert.Same(rows[2],_model.SelectedEvent);
        Assert.Equal("abc unselected",rows[1].Text);Assert.Contains("\\future(x)\\distort(0,0,1,1)",rows[0].Text);Assert.Contains("日本語",rows[2].Text);
        Assert.Contains("Changed 2",_model.SearchState(_model.ActiveDocumentId!.Value).Status);Assert.False(_model.HasEditOperation);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        Assert.Equal(preferences,new SettingsStore(Path.Combine(_root,"settings.json")).Load().SearchPreferences);
    }
    [Fact] public async Task FindNextAdvancesPastZeroWidthRegexMatchesAndWraps()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語";await Command(CommandIds.EditCommit);
        var line=_model.SelectedEvent!;var history=_model.ActiveEditor!.Undo.NextUndoName;
        await Command("search/next",new SubtitleSearchRequest(new(){Query="^|$",RegularExpression=true}));
        Assert.Equal(0,_model.TextSelectionStart);Assert.Equal(0,_model.TextSelectionEnd);
        await Command(CommandIds.FindNext);Assert.Equal(line.Text.Length,_model.TextSelectionStart);Assert.Equal(line.Text.Length,_model.TextSelectionEnd);
        await Command(CommandIds.FindNext);Assert.Equal(0,_model.TextSelectionStart);Assert.Equal(history,_model.ActiveEditor.Undo.NextUndoName);
    }
    [Fact] public async Task SearchIsDocumentLocalAndCancellationOrInvalidRegexCannotCommitAnUnfinishedDraft()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="abc 日本語";await Command(CommandIds.EditCommit);
        var first=_model.ActiveDocumentId!.Value;var editor=_model.ActiveEditor!;var tool=_model.SearchState(first);var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        System.ComponentModel.PropertyChangedEventHandler cancel=(sender,args)=>{if(args.PropertyName==nameof(MainWindowViewModel.HasEditOperation)&&_model.HasEditOperation)_=_model.Registry.InvokeAsync("search/cancel",new(first));};
        _model.PropertyChanged+=cancel;await Command("search/next",new SubtitleSearchRequest(new(){Query="abc"}));_model.PropertyChanged-=cancel;
        Assert.Equal("Search canceled.",tool.Status);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);Assert.False(_model.HasEditOperation);
        _model.Draft!.Text="unfinished é";var draft=_model.Draft;
        Assert.False(await _model.Registry.InvokeAsync("search/next",new(first),new SubtitleSearchRequest(new(){Query="[",RegularExpression=true})));
        Assert.StartsWith("Invalid regular expression:",tool.Status);
        Assert.Same(draft,_model.Draft);Assert.True(draft.IsChanged);Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);
        await Command(CommandIds.EditCancel);await Command(CommandIds.SubtitleNew);var second=_model.ActiveDocumentId!.Value;
        Assert.NotSame(tool,_model.SearchState(second));Assert.False(_model.Registry.CanExecute("search/next",new(first)));
        await Command("search/next",new SubtitleSearchRequest(new(){Query="second"}));Assert.Equal("abc",tool.Preferences.Query);
        await Command(CommandIds.WorkspaceActivateTab,first);Assert.Same(tool,_model.SearchState(first));Assert.Equal("abc",tool.Preferences.Query);
        await Command(CommandIds.FindNext);Assert.Same(editor,_model.ActiveEditor);Assert.Equal(source,editor.Document.Serialize());
    }

    [Fact] public async Task ModernSplitBindingsAreDisabledWithoutVideoAndCursorCommandsPreserveUnknownUnicodeSource()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="A{\\future(opaque)\\distort(0,0,1,1)}é👩‍👩‍👧‍👦漢";await Command(CommandIds.EditCommit);
        var editor=_model.ActiveEditor!;var line=_model.SelectedEvent!;var source=editor.Document.Serialize();
        Assert.False(_model.Actions["edit/line/split/before"].CanExecute(null));Assert.False(_model.Actions["edit/line/split/after"].CanExecute(null));
        Assert.Equal("edit/line/split/before",_model.Hotkeys.Resolve(new("D",Yoake.Core.Hotkeys.KeyModifiers.Control),[Yoake.Core.Hotkeys.HotkeyContext.Default])!.CommandId);
        Assert.Equal("edit/line/split/after",_model.Hotkeys.Resolve(new("D",Yoake.Core.Hotkeys.KeyModifiers.Control|Yoake.Core.Hotkeys.KeyModifiers.Shift),[Yoake.Core.Hotkeys.HotkeyContext.Default])!.CommandId);
        _model.TextCursor=0;Assert.False(_model.Actions["edit/line/split/preserve"].CanExecute(null));
        _model.TextCursor=line.Text.IndexOf("é",StringComparison.Ordinal)+2;Assert.True(_model.Actions["edit/line/split/preserve"].CanExecute(null));
        Assert.False(_model.Actions["edit/line/split/video"].CanExecute(null));await Command("edit/line/split/preserve");
        Assert.Equal(2,_model.Events.Count);Assert.Equal(line.Start,_model.SelectedEvent!.Start);Assert.Equal(line.End,_model.SelectedEvent.End);
        Assert.Equal("{\\future(opaque)\\distort(0,0,1,1)}👩‍👩‍👧‍👦漢",_model.SelectedEvent.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());_model.SelectedEvent=line;
        await Command("edit/clear/text");Assert.Equal("{\\future(opaque)\\distort(0,0,1,1)}",line.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());await Command("edit/clear");Assert.Equal("",line.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
    }
    [Theory] [InlineData("concatenate")][InlineData("keep_first")][InlineData("as_karaoke")]
    public async Task JoinVariantsAndDuplicationUseTheActiveDocumentSelectionAndUndo(string variant)
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;_model.Draft!.Text="{\\future(x)}日本語";await Command(CommandIds.EditCommit);
        await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent!;_model.Draft!.Text="မြန်မာ 👩‍👩‍👧‍👦";await Command(CommandIds.EditCommit);
        Assert.False(_model.Actions["edit/line/join/"+variant].CanExecute(null));
        _model.SelectedEvent=first;_model.SetSelectedEvents([first,second]);var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        await Command("edit/line/join/"+variant);Assert.Single(_model.Events);Assert.Same(first,_model.SelectedEvent);Assert.Single(_model.SelectedEvents);Assert.Contains("\\future(x)",first.Text);
        if(variant=="as_karaoke")Assert.StartsWith("{\\k200}",first.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        _model.SelectedEvent=first;_model.SetSelectedEvents([first,second]);await Command(CommandIds.GridDuplicate);
        Assert.Equal(4,_model.Events.Count);Assert.Equal(2,_model.SelectedEvents.Count);Assert.Same(_model.Events[2],_model.SelectedEvent);
        Assert.All(_model.SelectedEvents,item=>Assert.DoesNotContain(item,new[]{first,second}));await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
    }

    [Theory]
    [InlineData(SubtitleSortKey.Start)][InlineData(SubtitleSortKey.End)][InlineData(SubtitleSortKey.Style)]
    [InlineData(SubtitleSortKey.Actor)][InlineData(SubtitleSortKey.Effect)][InlineData(SubtitleSortKey.Layer)]
    public async Task SortingCommandsOwnStableSelectionAndOneUndoOperation(SubtitleSortKey key)
    {
        var path=Path.Combine(_root,"sort.ass");
        File.WriteAllText(path,"[Events]\nFormat: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Extra,Text\n"+
            string.Concat(new[]{4,2,3,1}.Select(n=>$"Dialogue: {n},0:00:0{n}.00,0:00:0{n+4}.00,{n},{n},0,0,0,{n},opaque,{n}日本語 {{\\future(opaque)\\distort(0,0,1,1)}}\n")));
        Assert.True(_model.OpenSubtitle(path));var editor=_model.ActiveEditor!;var rows=_model.Events.ToArray();var source=editor.Document.Serialize();
        _model.SelectedEvent=rows[0];_model.SetSelectedEvents([rows[0],rows[1],rows[3]]);
        var id="grid/sort/"+key.ToString().ToLowerInvariant();
        Assert.True(_model.Actions[id+"/selected"].CanExecute(null));
        Assert.False(_model.Registry.CanExecute(id,new(Guid.NewGuid())));
        await Command(id+"/selected");Assert.Equal(new[]{rows[1],rows[0],rows[2],rows[3]},_model.Events);
        Assert.Same(editor,_model.ActiveEditor);Assert.Same(rows[0],_model.SelectedEvent);Assert.Equal(new[]{rows[0],rows[1],rows[3]},_model.SelectedEvents);
        Assert.Equal("Sort subtitles",editor.Undo.NextUndoName);Assert.False(_model.HasEditOperation);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());Assert.False(editor.Undo.CanUndo);
        await Command(CommandIds.EditRedo);Assert.Equal(new[]{rows[1],rows[0],rows[2],rows[3]},_model.Events);
        await Command(id);Assert.Equal(new[]{rows[3],rows[1],rows[2],rows[0]},_model.Events);Assert.Same(rows[0],_model.SelectedEvent);
        Assert.Equal("opaque",rows[0].Get("Extra"));Assert.Contains("\\future(opaque)\\distort(0,0,1,1)",rows[0].Text);
    }
    [Fact] public async Task SwapAndCanceledSortRespectAvailabilityAndPreserveHistory()
    {
        Assert.False(_model.Actions["grid/swap"].CanExecute(null));Assert.False(_model.Actions["grid/sort/start"].CanExecute(null));
        await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridInsertAfter);var editor=_model.ActiveEditor!;var rows=_model.Events.ToArray();
        editor.SetTiming(rows[0],4000,6000);editor.SetTiming(rows[1],2000,3000);_model.SelectedEvent=rows[0];_model.SetSelectedEvents(rows);
        Assert.True(_model.Actions["grid/swap"].CanExecute(null));var source=editor.Document.Serialize();var history=editor.Undo.NextUndoName;
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.False(await _model.Registry.InvokeAsync("grid/sort/start",new(),cancellationToken:cancellation.Token));
        Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);Assert.False(_model.HasEditOperation);
        System.ComponentModel.PropertyChangedEventHandler cancel=(_,args)=>{if(args.PropertyName==nameof(MainWindowViewModel.HasEditOperation)&&_model.HasEditOperation)_=_model.Registry.InvokeAsync("editor/cancel-operation",new());};
        _model.PropertyChanged+=cancel;
        await Command("grid/sort/start");_model.PropertyChanged-=cancel;
        Assert.Equal(source,editor.Document.Serialize());Assert.Equal(history,editor.Undo.NextUndoName);Assert.False(_model.HasEditOperation);
        await Command("grid/swap");Assert.Equal(new[]{rows[1],rows[0]},_model.Events);Assert.Same(rows[0],_model.SelectedEvent);
        await Command(CommandIds.EditUndo);Assert.Equal(source,editor.Document.Serialize());
        _model.SetSelectedEvents([rows[0]]);Assert.False(_model.Actions["grid/swap"].CanExecute(null));Assert.False(_model.Actions["grid/sort/start/selected"].CanExecute(null));
    }
    [Fact] public async Task TagDisplayCommandsArePersistedRadioPresentationAndRetainDraftAndHistory()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="{\\future(opaque)}日本語 unfinished";
        var editor=_model.ActiveEditor!;var draft=_model.Draft;var line=_model.SelectedEvent;var history=editor.Undo.NextUndoName;
        var ids=new[]{"grid/tags/show","grid/tags/simplify","grid/tags/hide"};
        foreach(var mode in Enum.GetValues<GridTagDisplayMode>())
        {
            await Command(ids[(int)mode]);Assert.Equal(mode,_model.GridTagDisplay);
            Assert.Equal(1,ids.Count(id=>_model.Actions[id].IsChecked));Assert.True(_model.Actions[ids[(int)mode]].IsChecked);
            Assert.Same(draft,_model.Draft);Assert.Same(line,_model.SelectedEvent);Assert.Same(editor,_model.ActiveEditor);Assert.Equal(history,editor.Undo.NextUndoName);
            Assert.True(draft.IsChanged);Assert.Equal(mode,new SettingsStore(Path.Combine(_root,"settings.json")).Load().GridTagDisplay);
        }
        await Command("grid/tag/cycle_hiding");Assert.Equal(GridTagDisplayMode.Full,_model.GridTagDisplay);
        await Command("grid/tag/cycle_hiding");Assert.Equal(GridTagDisplayMode.Simplified,_model.GridTagDisplay);
    }

    [Fact] public async Task ToolbarVisibilityIsCheckedPersistedPresentationAndRecoversIndependently()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="unfinished 日本語";
        var editor=_model.ActiveEditor;var line=_model.SelectedEvent;var draft=_model.Draft;var undo=editor!.Undo.NextUndoName;
        Assert.True(_model.ToolbarVisible);Assert.True(_model.Actions[CommandIds.AppToggleToolbar].IsChecked);
        await Command(CommandIds.AppToggleToolbar);
        Assert.False(_model.ToolbarVisible);Assert.False(_model.Actions[CommandIds.AppToggleToolbar].IsChecked);
        Assert.Same(editor,_model.ActiveEditor);Assert.Same(line,_model.SelectedEvent);Assert.Same(draft,_model.Draft);Assert.Equal(undo,editor.Undo.NextUndoName);
        var store=new SettingsStore(Path.Combine(_root,"settings.json"));Assert.False(store.Load().ToolbarVisible);
        File.WriteAllText(store.Path,"""{"toolbarVisible":false,"viewMode":"corrupt"}""");
        Assert.False(store.Load().ToolbarVisible);Assert.Equal(WorkspaceViewMode.Default,store.Load().ViewMode);
        File.WriteAllText(store.Path,"""{"toolbarVisible":"corrupt","viewMode":2}""");Assert.True(store.Load().ToolbarVisible);Assert.Equal(WorkspaceViewMode.Video,store.Load().ViewMode);
        await Command(CommandIds.AppToggleToolbar);Assert.True(_model.ToolbarVisible);
    }

    [Fact] public async Task ExitCommandHonorsUnsavedCancellationAndRequestsNativeCloseAfterSuccess()
    {
        await Command(CommandIds.GridInsertAfter);var document=_workspace.ActiveDocument!.Id;var editor=_model.ActiveEditor;var requests=0;
        _model.WindowCloseRequested+=(_,_)=>requests++;
        await Command(CommandIds.AppExit);Assert.Equal(0,requests);Assert.Same(editor,_model.ActiveEditor);Assert.Equal(document,_workspace.ActiveDocument.Id);Assert.True(editor!.Undo.CanUndo);
        _dialogs.Choice=UnsavedChoice.Discard;await Command(CommandIds.AppExit);Assert.Equal(1,requests);Assert.DoesNotContain(_workspace.Documents,item=>item.Id==document);
    }
    [Fact] public void KeyAliasesAndModifierBitsMatchTheShippingInputProtocol()
    {
        foreach(var key in new[]{Avalonia.Input.Key.Return,Avalonia.Input.Key.Enter})Assert.Equal("Enter",Yoake.UI.Commands.EditorKeyNames.FromKey(key));
        Assert.Equal("Backspace",Yoake.UI.Commands.EditorKeyNames.FromKey(Avalonia.Input.Key.Back));
        Assert.Equal("OEM1",Yoake.UI.Commands.EditorKeyNames.FromKey(Avalonia.Input.Key.OemSemicolon));
        Assert.Equal("KP_2",Yoake.UI.Commands.EditorKeyNames.FromKey(Avalonia.Input.Key.NumPad2));
        Assert.Equal("KP_Enter",Yoake.UI.Commands.EditorKeyNames.FromEvent(new(){Key=Avalonia.Input.Key.Enter,PhysicalKey=Avalonia.Input.PhysicalKey.NumPadEnter}));
        var modifiers=Yoake.UI.Commands.EditorKeyNames.FromModifiers(Avalonia.Input.KeyModifiers.Control|Avalonia.Input.KeyModifiers.Alt);
        Assert.Equal(Yoake.Core.Hotkeys.KeyModifiers.Control|Yoake.Core.Hotkeys.KeyModifiers.Alt,modifiers);
        Assert.Equal(Avalonia.Input.KeyModifiers.Control|Avalonia.Input.KeyModifiers.Alt,Yoake.UI.Commands.EditorKeyNames.ToGesture(new("K",modifiers))!.KeyModifiers);
    }
    [Fact] public async Task HotkeySettingsCommandsUpdateAdaptersAndPersistenceWithoutChangingTheEditor()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="日本語 {\\future(1,2)}";
        var editor=_model.ActiveEditor;var draft=_model.Draft;var line=_model.SelectedEvent;
        var command=_model.Actions[CommandIds.SubtitleNew];Assert.Equal("Ctrl-N",command.Shortcut);
        await Command("settings/hotkeys/set",new HotkeyChange(Yoake.Core.Hotkeys.HotkeyContext.Default,CommandIds.SubtitleNew,["F10","Ctrl-Alt-N"]));
        Assert.Equal("F10",command.Shortcut);Assert.Equal(Avalonia.Input.Key.F10,command.InputGesture!.Key);
        Assert.Contains("F10",command.Tooltip);
        Assert.Same(editor,_model.ActiveEditor);Assert.Same(draft,_model.Draft);Assert.Same(line,_model.SelectedEvent);
        var persisted=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Single(persisted.HotkeyOverrides!);
        await Command("settings/hotkeys/set",new HotkeyChange(Yoake.Core.Hotkeys.HotkeyContext.Default,CommandIds.SubtitleNew,[]));
        Assert.Equal("",command.Shortcut);Assert.Null(command.InputGesture);
        await Command("settings/hotkeys/reset-command",new HotkeyCommandReset(Yoake.Core.Hotkeys.HotkeyContext.Default,CommandIds.SubtitleNew));
        Assert.Equal("Ctrl-N",command.Shortcut);
    }
    [Fact] public async Task CommandAdapterHistoryLabelsFollowDraftUndoRedoAndActiveDocument()
    {
        var undo = _model.Actions[CommandIds.EditUndo]; var redo = _model.Actions[CommandIds.EditRedo];
        Assert.Equal("Nothing to undo", undo.Label); Assert.Equal("Nothing to redo", redo.Label);
        await Command(CommandIds.GridInsertAfter);
        _model.Draft!.Text = "日本語 draft";
        Assert.Equal("Undo draft", undo.Label);
        await Command(CommandIds.EditUndo); Assert.Equal("", _model.Draft.Text);
        Assert.Equal("Undo " + _model.ActiveEditor!.Undo.NextUndoName, undo.Label);
        await Command(CommandIds.EditUndo); Assert.Equal("Nothing to undo", undo.Label);
        Assert.Equal("Redo " + _model.ActiveEditor.Undo.NextRedoName, redo.Label);
        Assert.True(redo.CanExecute(null));
        var first = _workspace.ActiveDocument!.Id;
        await Command(CommandIds.SubtitleNew);
        Assert.Equal("Nothing to redo", redo.Label); Assert.False(redo.CanExecute(null));
        await Command(CommandIds.WorkspaceActivateTab, first);
        Assert.StartsWith("Redo ", redo.Label); Assert.True(redo.CanExecute(null));
    }

    [Fact] public async Task VisualRadioStateIsOwnedBySemanticCommandsIncludingDirectVectorModeSelection()
    {
        foreach (var tool in MainWindowViewModel.VisualToolNames)
        {
            var id = "video/tool/" + (tool == "Position" ? "position" : tool.ToLowerInvariant());
            await Command(id);
            Assert.Equal(tool, _model.ActiveVisualTool);
            Assert.Single(MainWindowViewModel.VisualToolNames.Where(name => _model.Actions["video/tool/" + (name == "Position" ? "position" : name.ToLowerInvariant())].IsChecked));
        }
        foreach (var mode in MainWindowViewModel.VectorModes)
        {
            await Command("video/vector/" + mode.ToLowerInvariant());
            Assert.True(_model.IsVectorClipTool); Assert.Equal(mode, _model.VectorMode);
            Assert.Single(MainWindowViewModel.VectorModes.Where(name => _model.Actions["video/vector/" + name.ToLowerInvariant()].IsChecked));
        }
        await Command("video/tool/crosshair");
        Assert.DoesNotContain(MainWindowViewModel.VectorModes, name => _model.Actions["video/vector/" + name.ToLowerInvariant()].IsChecked);
    }

    [Fact] public async Task ViewCommandsOnlyChangePresentationAndRestoreOnNextLaunch()
    {
        await Command(CommandIds.GridInsertAfter);
        await Command(CommandIds.GridInsertAfter);
        await Command(CommandIds.GridSelectAll);
        _model.Draft!.Text="committed"; await Command(CommandIds.EditCommit);
        await Command(CommandIds.EditUndo); // Keep both undo and redo branches.
        _model.Draft!.End="unfinished timing";
        _model.CurrentTimeSeconds=1.25;
        _model.TextCursor=3;
        var editor=_model.ActiveEditor!; var session=_workspace.ActiveDocument;
        var draft=_model.Draft; var line=_model.SelectedEvent;
        var selection=_model.SelectedEvents.ToArray(); var source=editor.Document.Serialize();
        var undo=editor.Undo.NextUndoName; var redo=editor.Undo.NextRedoName;
        var modes=new[] {
            (CommandIds.ViewDefault,WorkspaceViewMode.Default,false,false),
            (CommandIds.ViewAudio,WorkspaceViewMode.Audio,true,false),
            (CommandIds.ViewVideo,WorkspaceViewMode.Video,false,true),
            (CommandIds.ViewAudioVideo,WorkspaceViewMode.AudioVideo,true,true),
        };
        foreach(var (id,mode,audio,video) in modes.Concat(modes))
        {
            await Command(id);
            Assert.Equal(mode,_model.ViewMode); Assert.Equal(audio,_model.ShowAudio); Assert.Equal(video,_model.ShowVideo);
            foreach(var item in modes)Assert.Equal(item.Item1==id,_model.Registry.GetRequired(item.Item1).IsChecked(new()));
            Assert.Single(modes.Where(item=>_model.Registry.GetRequired(item.Item1).IsChecked(new())));
            Assert.Same(editor,_model.ActiveEditor); Assert.Same(session,_workspace.ActiveDocument);
            Assert.Same(draft,_model.Draft); Assert.Same(line,_model.SelectedEvent); Assert.Equal(selection,_model.SelectedEvents);
            Assert.Equal("unfinished timing",_model.Draft!.End); Assert.Equal(source,editor.Document.Serialize());
            Assert.Equal(undo,editor.Undo.NextUndoName); Assert.Equal(redo,editor.Undo.NextRedoName);
            Assert.Equal(1.25,_model.CurrentTimeSeconds); Assert.Equal(3,_model.TextCursor);
            var store=new SettingsStore(Path.Combine(_root,"settings.json"));
            using var restored=new MainWindowViewModel(new(),new(),new(),new ThemeService(),store,store.Load());
            Assert.Equal(mode,restored.ViewMode); Assert.Null(restored.VideoFrame); Assert.Null(restored.WaveformSamples);
        }
        await Command(CommandIds.ViewDefault);
        await Command(CommandIds.EditCancel); await Command(CommandIds.EditRedo);
        Assert.Equal("committed",line!.Text); // History remains usable.
    }
    [Fact] public async Task DraftFeedbackIsLiveCoalescedAndRevertible()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;var revision=_model.PreviewRevision;
        foreach(var text in new[]{"m","mi","mixed မြန်မာ"})_model.Draft.Text=text;
        Assert.Equal("mixed မြန်မာ",line.DisplayText);Assert.Equal("",line.Text);Assert.True(_model.PreviewRevision>revision);
        await Command(CommandIds.EditCancel);Assert.Equal("",line.DisplayText);Assert.Equal("",_model.Draft.Text);
        _model.Draft.Text="one burst";_model.Draft.Text="one burst 日本語";await Command(CommandIds.EditCommit);Assert.Equal("one burst 日本語",line.Text);
        await Command(CommandIds.EditUndo);Assert.Equal("",line.Text);Assert.Single(_model.Events);
    }
    [Fact] public async Task AudioCommandsPersistVolumeMuteAndIndependentDisplayControls()
    {
        await Command("audio/volume",0.35);await Command("audio/mute",true);await Command("audio/display/intensity",3d);await Command("audio/display/height",210d);await Command("styles/layout",new double[]{1.1,1.2,3.5});
        var settings=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.Equal(0.35,settings.PlaybackVolume);Assert.True(settings.PlaybackMuted);Assert.Equal(3,settings.AudioIntensity);Assert.Equal(210,settings.AudioDisplayHeight);Assert.Equal(new double[]{1.1,1.2,3.5},settings.StyleSplitWeights);
    }
    [Fact] public async Task NewDocumentIsConnectedAndInsertCanBeEdited()
    {
        Assert.NotNull(_model.ActiveEditor);Assert.Empty(_model.Events);Assert.Null(_model.Draft);Assert.Equal("",_model.EditorDraft.Text);await Command(CommandIds.GridInsertAfter);
        Assert.Single(_model.Events);Assert.NotNull(_model.Draft);_model.Draft!.Text="日本語 မြန်မာ 👩‍👩‍👧‍👦";
        Assert.True(_workspace.ActiveDocument!.IsDirty);await Command(CommandIds.EditCommit);
        Assert.Equal("日本語 မြန်မာ 👩‍👩‍👧‍👦",_model.Events[0].Text);Assert.Equal("Edit subtitle text",_model.ActiveEditor!.Undo.NextUndoName);
    }
    [Fact] public async Task TabsHaveIndependentHistoryAndCommitBeforeSwitching()
    {
        await Command(CommandIds.GridInsertAfter);var first=_workspace.ActiveDocumentId!.Value;_model.Draft!.Text="first draft";
        await Command(CommandIds.SubtitleNew);Assert.Empty(_model.Events);Assert.False(_model.ActiveEditor!.Undo.CanUndo);
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="second draft";
        await Command(CommandIds.WorkspaceActivateTab,first);Assert.Equal("first draft",_model.Events[0].Text);
        await Command(CommandIds.EditUndo);Assert.Equal("",_model.Events[0].Text);
        await Command(CommandIds.WorkspaceActivateTab,_workspace.Documents[1].Id);Assert.Equal("second draft",_model.Events[0].Text);
    }
    [Fact] public async Task SaveAsChangesPathAndSavedUndoState()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="saved";var path=Path.Combine(_root,"saved.ass");
        Assert.True(_model.SaveActiveSubtitle(path));Assert.Equal(path,_workspace.ActiveDocument!.Path);Assert.Equal("saved.ass",_workspace.ActiveDocument.Title);Assert.False(_workspace.ActiveDocument.IsDirty);
        _model.Draft!.Actor="Actor";await Command(CommandIds.EditCommit);Assert.True(_workspace.ActiveDocument.IsDirty);
        await Command(CommandIds.EditUndo);Assert.False(_workspace.ActiveDocument.IsDirty);
        Assert.Equal("saved",AssDocument.Load(path).Events[0].Text);
    }
    [Fact] public void SaveAsKeepsExistingAutomationScriptLocationAndRollsBackFailedSave()
    {
        _model.AutomationRuntimeProvider = new UnavailableAutomationProvider();
        var script = Path.Combine(_root, "日本語.lua"); File.WriteAllText(script, "script_name='Fixture'");
        var original = Path.Combine(_root, "original.ass"); Assert.True(_model.SaveActiveSubtitle(original));
        _model.ActiveEditor!.SetProjectProperties(new Dictionary<string, string> { ["Automation Scripts"] = "~日本語.lua|~missing.lua|!future.lua" });
        var before = _model.ActiveEditor.Document.Serialize();
        var dirty = _model.ActiveEditor.IsDirty; var undo = _model.ActiveEditor.Undo.NextUndoName;
        var destination = Path.Combine(_root, "moved"); Directory.CreateDirectory(destination);
        Assert.False(_model.SaveActiveSubtitle(destination));
        Assert.Equal(original, _workspace.ActiveDocument!.Path); Assert.Equal(before, _model.ActiveEditor.Document.Serialize());
        Assert.Equal(dirty, _model.ActiveEditor.IsDirty); Assert.Equal(undo, _model.ActiveEditor.Undo.NextUndoName);
        var saved = Path.Combine(destination, "new.ass"); Assert.True(_model.SaveActiveSubtitle(saved));
        var references = AssDocument.Load(saved).GetSectionValue("[Aegisub Project Garbage]", "Automation Scripts");
        Assert.EndsWith("|~missing.lua|!future.lua", references);
        Assert.Equal(script, Yoake.Core.Automation.AutomationScriptReference.Resolve(references.Split('|')[0], saved, _model.AutomationBaseDirectory));
        Assert.False(_model.ActiveEditor.IsDirty);
        _model.ActiveEditor.Undo.Undo(); Assert.Equal(before, _model.ActiveEditor.Document.Serialize());
    }
    [Fact] public async Task CancelUnsavedCloseRetainsDocumentAndDiscardClosesIt()
    {
        await Command(CommandIds.GridInsertAfter);var id=_workspace.ActiveDocumentId!.Value;
        _dialogs.Choice=UnsavedChoice.Cancel;Assert.False(await _model.CloseDocumentAsync(id));Assert.Contains(_workspace.Documents,d=>d.Id==id);
        _dialogs.Choice=UnsavedChoice.Discard;Assert.True(await _model.CloseDocumentAsync(id));Assert.DoesNotContain(_workspace.Documents,d=>d.Id==id);
    }
    [Fact] public async Task FailedValidationKeepsDraftAndCurrentRow()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent;await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent;
        _model.Draft!.End="bad";_model.SelectedEvent=first;Assert.Same(second,_model.SelectedEvent);Assert.Same(second,Assert.Single(_model.SelectedEvents));Assert.Equal("bad",_model.Draft.End);Assert.False(_model.CommitDraft());
    }
    [Fact] public async Task ProgrammaticRowJumpSelectsDestinationAndRetainsAnExistingBulkSelection()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        await Command(CommandIds.GridInsertAfter);var second=_model.SelectedEvent!;
        _model.Draft!.Text="pending second";_model.SelectedEvent=first;
        Assert.Same(first,Assert.Single(_model.SelectedEvents));Assert.Equal("pending second",second.Text);
        await Command(CommandIds.GridSelectAll);_model.SelectedEvent=second;
        Assert.Equal(2,_model.SelectedEvents.Count);Assert.Contains(first,_model.SelectedEvents);Assert.Contains(second,_model.SelectedEvents);
        _model.SelectedEvent=null;Assert.Empty(_model.SelectedEvents);Assert.Null(_model.Draft);
    }
    [Fact] public async Task StyleManagerMutationsRefreshMainStyleChoicesBeforeDraftSelection()
    {
        await Command(CommandIds.GridInsertAfter);var style=_model.ActiveEditor!.Document.Styles[0];
        var observed=false;
        _model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MainWindowViewModel.EditorDraft)&&_model.Draft?.Style=="Renamed 日本")observed=_model.StyleNames.Contains(_model.Draft.Style);};
        _model.ActiveEditor.EditStyle(style,new Dictionary<string,string>{{"Name","Renamed 日本"}});
        Assert.True(observed);Assert.Equal("Renamed 日本",_model.Draft!.Style);Assert.Contains("Renamed 日本",_model.StyleNames);
        await Command(CommandIds.EditUndo);Assert.Equal("Default",_model.Draft.Style);Assert.Contains("Default",_model.StyleNames);Assert.DoesNotContain("Renamed 日本",_model.StyleNames);
    }
    [Fact] public async Task TimingAndVisualGesturesRollbackOnCancelAndCommitOnce()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;
        Assert.True(_model.BeginGesture("Timing gesture"));_model.UpdateTimingGesture(2,1);_model.UpdateTimingGesture(2,2);_model.CancelGesture();Assert.Equal(0,line.StartMilliseconds);
        Assert.True(_model.BeginGesture("Position gesture"));_model.UpdatePositionGesture(100,200);Assert.Equal(new AssPoint(100,200),AssVisualTags.Position(_model.VisualText));Assert.Contains("\\pos(100,200)",_model.PreviewSource());_model.UpdatePositionGesture(300,400);_model.EndGesture();Assert.Equal(new AssPoint(300,400),AssVisualTags.Position(line.Text));
        Assert.Equal("Position gesture",_model.ActiveEditor!.Undo.NextUndoName);await Command(CommandIds.EditUndo);Assert.Equal("",line.Text);
    }
    [Fact] public async Task VisualSelectionIsVisibleOnlyTransactionalAndUsesLiveGestureText()
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        _model.ActiveEditor!.SetTiming(first,0,1000);_model.ActiveEditor.SetField(first,"Text","{\\pos(100,100)\\org(50,50)\\future(x)}first","Fixture");
        await Command(CommandIds.GridDuplicate);var second=_model.SelectedEvent!;_model.ActiveEditor.SetField(second,"Text","{\\pos(200,150)}second","Fixture");
        await Command(CommandIds.GridDuplicate);var comment=_model.SelectedEvent!;_model.ActiveEditor.ToggleComment([comment]);
        await Command(CommandIds.GridDuplicate);var hidden=_model.SelectedEvent!;_model.ActiveEditor.ToggleComment([hidden]);_model.ActiveEditor.SetTiming(hidden,2000,3000);
        _model.SelectedEvent=first;await Command(CommandIds.GridSelectAll);_model.CurrentTimeSeconds=0.5;
        Assert.Equal(2,_model.VisibleVisualLines().Count);var originals=_model.Events.Select(l=>l.Text).ToArray();
        Assert.True(_model.BeginGesture("Move two visible lines"));
        foreach(var dx in new[]{10d,20d,30d})_model.UpdateVisualGesture(l=>AssVisualTags.ShiftPosition(l.Text,l.DefaultPosition,new(dx,5)));
        Assert.Equal(new AssPoint(130,105),AssVisualTags.Position(first.Text));Assert.Equal(new AssPoint(230,155),AssVisualTags.Position(second.Text));
        Assert.Contains("\\pos(130,105)",_model.PreviewSource());Assert.Equal(originals[2],comment.Text);Assert.Equal(originals[3],hidden.Text);
        _model.EndGesture();Assert.Equal("Move two visible lines",_model.ActiveEditor.Undo.NextUndoName);await Command(CommandIds.EditUndo);
        Assert.Equal(originals,_model.Events.Select(l=>l.Text).ToArray());
    }
    [Fact] public async Task VisibleSelectionRemainsEditableWhenCurrentRowIsOutsideTheFrame()
    {
        await Command(CommandIds.GridInsertAfter);var visible=_model.SelectedEvent!;_model.ActiveEditor!.SetTiming(visible,0,1000);
        await Command(CommandIds.GridInsertAfter);var hidden=_model.SelectedEvent!;_model.ActiveEditor.SetTiming(hidden,2000,3000);
        _model.SetSelectedEvents([visible,hidden]);_model.CurrentTimeSeconds=.5;
        var primary=Assert.Single(_model.VisibleVisualLines());Assert.Same(visible,primary.Line);Assert.True(primary.Active);
        Assert.True(_model.BeginGesture("Visible selection"));_model.UpdateVisualGesture(l=>AssVisualTags.SetOrigin(l.Text,new(40,50)));_model.EndGesture();
        Assert.Equal(new AssPoint(40,50),AssVisualTags.Origin(visible.Text));Assert.Equal("",hidden.Text);
        await Command(CommandIds.EditUndo);Assert.Equal("",visible.Text);
    }
    [Theory][InlineData("edit/cancel")][InlineData("video/tool/rotatez")][InlineData("video/tool/vectorclip")]
    public async Task VisualCancellationAndToolSwitchRestoreTheBatch(string command)
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;_model.ActiveEditor!.SetTiming(line,0,1000);var source=line.Text;
        Assert.True(_model.BeginGesture("Visual gesture"));_model.UpdateVisualGesture(l=>AssVisualTags.SetOrigin(l.Text,new(20,30)));Assert.NotEqual(source,line.Text);
        await Command(command);Assert.Equal(source,line.Text);Assert.False(_model.HasGesture);Assert.DoesNotContain("\\org(20,30)",_model.PreviewSource());
    }
    [Theory]
    [InlineData(@"{\iclip(3,m 0 0 l 80 0 80 80)\clippos(5,6)\future(x)}vector")]
    [InlineData(@"{\clip(future(mask))\clippos(5,6)\future(x)}unknown")]
    public async Task RectangleBatchCreationProtectsOtherSelectedVectorAndUnknownClips(string protectedText)
    {
        await Command(CommandIds.GridInsertAfter);var first=_model.SelectedEvent!;
        _model.ActiveEditor!.SetTiming(first,0,1000);_model.ActiveEditor.SetField(first,"Text",@"{\future(keep)}plain","Fixture");
        await Command(CommandIds.GridInsertAfter);var other=_model.SelectedEvent!;
        _model.ActiveEditor.SetTiming(other,0,1000);_model.ActiveEditor.SetField(other,"Text",protectedText,"Fixture");
        _model.SelectedEvent=first;_model.SetSelectedEvents([first,other]);_model.CurrentTimeSeconds=.5;
        var original=first.Text;
        var context=new Yoake.UI.VisualTools.VisualToolContext(_model,new Avalonia.Rect(0,0,384,288),_model.VisibleVisualLines());
        var tool=new Yoake.UI.VisualTools.RectangleClipTool();
        var a=new Yoake.UI.VisualTools.VisualPointer(new Avalonia.Point(20,20),new(20,20),Avalonia.Input.KeyModifiers.None);
        var b=new Yoake.UI.VisualTools.VisualPointer(new Avalonia.Point(80,60),new(80,60),Avalonia.Input.KeyModifiers.None);
        Assert.True(tool.Press(context,a));tool.Move(context,b);tool.Release(context,b);_model.EndGesture();
        Assert.NotNull(AssVisualTags.Clip(first.Text));Assert.Equal(protectedText,other.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(original,first.Text);Assert.Equal(protectedText,other.Text);
    }
    [Fact] public async Task VisualExceptionAndSeekRollbackWithoutPartialEdits()
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;_model.ActiveEditor!.SetTiming(line,0,1000);
        Assert.True(_model.BeginGesture("Visual gesture"));_model.UpdateVisualGesture(l=>AssVisualTags.SetOrigin(l.Text,new(20,30)));_model.CurrentTimeSeconds=.5;
        Assert.Equal("",line.Text);Assert.False(_model.HasGesture);
        Assert.True(_model.BeginGesture("Visual failure"));_model.UpdateVisualGesture(_=>throw new InvalidOperationException("deterministic failure"));
        Assert.False(_model.HasGesture);Assert.Equal("",line.Text);
    }
    [Fact] public async Task AudioZoomAndLinkPersistAndUnlinkRestoresIndependentControls()
    {
        _model.AudioHorizontalZoom=75;_model.AudioAmplitude=75;_model.AudioVolumeLinked=true;
        Assert.Equal(.75,_model.PlaybackVolume,8);Assert.False(_model.IsVolumeIndependent);
        var settings=new SettingsStore(Path.Combine(_root,"settings.json")).Load();Assert.True(settings.AudioVolumeLinked);Assert.Equal(_model.AudioWindowSeconds,settings.AudioWindowSeconds);
        using(var restored=new MainWindowViewModel(new CommandRegistry(),new WorkspaceManager(),new UndoManager(),new ThemeService(),new SettingsStore(Path.Combine(_root,"settings.json")),settings))
        {Assert.True(restored.AudioVolumeLinked);Assert.Equal(.75,restored.PlaybackVolume,8);Assert.Equal(75,restored.AudioAmplitude,8);Assert.False(restored.IsVolumeIndependent);}
        _model.AudioVolumeLinked=false;_model.PlaybackVolume=.2;_model.AudioAmplitude=50;Assert.Equal(.2,_model.PlaybackVolume);
        await Command("audio/display/height",230d);Assert.Equal(230,_model.AudioDisplayHeight);
    }
    [Fact] public async Task AudioZoomControlReflectsEachDocumentsIndependentViewport()
    {
        var first=_model.Tabs.Single(t=>t.IsActive).Id;_model.AudioWindowSeconds=1;_model.RememberAudioViewport(12,1);
        await Command(CommandIds.SubtitleNew);var second=_model.Tabs.Single(t=>t.IsActive).Id;
        _model.AudioWindowSeconds=8;_model.RememberAudioViewport(20,8);
        await Command(CommandIds.WorkspaceActivateTab,first);
        Assert.Equal((12d,1d),_model.AudioViewport);Assert.Equal(1,_model.AudioWindowSeconds);
        await Command(CommandIds.WorkspaceActivateTab,second);
        Assert.Equal((20d,8d),_model.AudioViewport);Assert.Equal(8,_model.AudioWindowSeconds);
    }
    [Fact] public async Task MultiRowCommandsWorkAcrossUndo()
    {
        await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridInsertAfter);await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridDuplicate);Assert.Equal(4,_model.Events.Count);
        await Command(CommandIds.EditUndo);Assert.Equal(2,_model.Events.Count);await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridToggleComment);Assert.All(_model.Events,l=>Assert.True(l.IsComment));
    }
    [Fact] public async Task ReopenConnectsLoadedRowsAndRecentFiles()
    {
        var doc=AssDocument.CreateEmpty();var editor=new SubtitleEditor(doc);editor.Insert(null,false);var path=Path.Combine(_root,"open.ass");doc.Save(path);
        Assert.True(_model.OpenSubtitle(path));Assert.Single(_model.Events);Assert.Equal(path,_model.RecentFiles[0]);await Command(CommandIds.GridDuplicate);Assert.Equal(2,_model.Events.Count);
    }

    [Fact] public async Task UndoCancelsDraftBeforeUndoingCommittedChanges()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="uncommitted";await Command(CommandIds.EditUndo);Assert.Single(_model.Events);Assert.Equal("",_model.Draft!.Text);
        await Command(CommandIds.EditUndo);Assert.Empty(_model.Events);
    }
    [Fact] public async Task StartingGestureCommitsPendingTextAndCancellationKeepsThatCommit()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="pending";Assert.True(_model.BeginGesture("Drag"));_model.UpdateTimingGesture(2,1);_model.CancelGesture();Assert.Equal("pending",_model.SelectedEvent!.Text);Assert.Equal(0,_model.SelectedEvent.StartMilliseconds);Assert.Equal("Edit subtitle text",_model.ActiveEditor!.Undo.NextUndoName);
    }
    [Fact] public async Task LowercaseFormatFieldsRemainEditable()
    {
        var path=Path.Combine(_root,"lowercase.ass");File.WriteAllText(path,"[Events]\nFormat: layer,start,end,style,name,marginl,marginr,marginv,effect,text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,Old,0,0,0,,Text\n");Assert.True(_model.OpenSubtitle(path));_model.Draft!.Actor="New";await Command(CommandIds.EditCommit);Assert.Equal("New",_model.Events[0].Actor);
    }

    [Fact] public async Task LargeSelectAllPublishesOneSelectionChange()
    {
        var path=Path.Combine(_root,"large.ass");File.WriteAllText(path,"[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+"\n"+string.Concat(Enumerable.Range(0,5000).Select(i=>$"Dialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,Row {i}\n")));Assert.True(_model.OpenSubtitle(path));
        var changes=0;((System.Collections.Specialized.INotifyCollectionChanged)_model.SelectedEvents).CollectionChanged+=(_,_)=>changes++;
        await Command(CommandIds.GridSelectAll);Assert.Equal(5000,_model.SelectedEvents.Count);Assert.Equal(1,changes);
    }

    [Fact] public async Task StructuralUndoAndRedoKeepCurrentDraftAttachedToALiveRow()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="first";await Command(CommandIds.EditCommit);
        await Command(CommandIds.GridDuplicate);var duplicate=_model.SelectedEvent!;
        await Command(CommandIds.EditUndo);Assert.DoesNotContain(duplicate,_model.Events);Assert.Contains(_model.SelectedEvent!,_model.Events);Assert.Equal("first",_model.Draft!.Text);
        await Command(CommandIds.EditRedo);Assert.Equal(2,_model.Events.Count);Assert.Contains(_model.SelectedEvent!,_model.Events);
        await Command(CommandIds.GridSelectAll);await Command(CommandIds.GridDelete);Assert.Empty(_model.Events);Assert.Null(_model.Draft);
        await Command(CommandIds.EditUndo);Assert.Equal(2,_model.Events.Count);Assert.Contains(_model.SelectedEvent!,_model.Events);Assert.All(_model.SelectedEvents,line=>Assert.Contains(line,_model.Events));
        _model.SelectedEvent=_model.Events[1];_model.Draft!.Actor="restored";await Command(CommandIds.EditCommit);Assert.Equal("restored",_model.Events[1].Actor);
        Assert.False(_model.IsSynchronizingSelection);
    }
    [Fact] public async Task SelectionFormattingCommitsDraftAndHasIndependentUndo()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="Hello world";
        _model.TextSelectionStart=6;_model.TextSelectionEnd=11;
        await Command(CommandIds.FormatBold);Assert.Equal("Hello {\\b1}world{\\b0}",_model.SelectedEvent!.Text);
        Assert.Equal("world",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        await Command(CommandIds.EditUndo);Assert.Equal("Hello world",_model.SelectedEvent.Text);
        await Command(CommandIds.EditRedo);Assert.Equal("Hello {\\b1}world{\\b0}",_model.SelectedEvent.Text);
    }
    [Fact] public async Task FormattingSwatchesAndFlagsFollowDraftStyleAndCaretWithoutMutatingText()
    {
        await Command(CommandIds.GridInsertAfter);
        var style=_model.ActiveEditor!.AddStyle();_model.ActiveEditor.SetField(style,"Italic","-1","Style italic");
        _model.Draft!.Text=@"Hello {\b1\1c&H402010&\1a&H80&}world";
        var original=_model.Draft.Text;_model.TextSelectionStart=10;_model.TextSelectionEnd=10;
        Assert.True(_model.Formatting.Bold);Assert.Equal(new AssColor(16,32,64,128),_model.Formatting.Primary);
        Assert.Equal(original,_model.Draft.Text);
        _model.TextSelectionStart=0;_model.TextSelectionEnd=original.Length;Assert.Null(_model.Formatting.Bold);
        _model.Draft.Style=style.Name;Assert.True(_model.Formatting.Italic);
        await Command(CommandIds.FormatUnderline);
        Assert.Contains("world",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        await Command(CommandIds.EditUndo);Assert.Equal(original,_model.Draft!.Text);
    }
    [Fact] public async Task HardNewlineCommandReplacesSelectionImmediatelyAndUndoesWithItsEditBurst()
    {
        await Command(CommandIds.GridInsertAfter);_model.Draft!.Text="abမြန်မာef";await Command(CommandIds.EditCommit);
        var original=_model.Draft.Text;_model.TextSelectionStart=2;_model.TextSelectionEnd=original.Length-2;
        await Command(CommandIds.TextHardNewline);Assert.Equal(@"ab\Nef",_model.Draft.Text);Assert.Equal(4,_model.TextCursor);
        Assert.Equal(4,_model.TextSelectionStart);Assert.Equal(4,_model.TextSelectionEnd);Assert.DoesNotContain("\n",_model.Draft.Text);
        await Command(CommandIds.EditCommit);await Command(CommandIds.EditUndo);Assert.Equal(original,_model.Draft.Text);
    }
    [Fact] public void EnterEnumAliasesResolveTheActualEditorBindings()
    {
        var name=Yoake.UI.Commands.EditorKeyNames.FromKey(Avalonia.Input.Key.Enter);
        Assert.Equal("Enter",name);Assert.Equal(name,Yoake.UI.Commands.EditorKeyNames.FromKey(Avalonia.Input.Key.Return));
        var contexts=new[]{Yoake.Core.Hotkeys.HotkeyContext.Default,Yoake.Core.Hotkeys.HotkeyContext.SubtitleEdit};
        Assert.Equal(CommandIds.EditCommitNext,_model.Hotkeys.Resolve(new(name),contexts)!.CommandId);
        Assert.Equal(CommandIds.EditCommit,_model.Hotkeys.Resolve(new(name,Yoake.Core.Hotkeys.KeyModifiers.Control),contexts)!.CommandId);
        Assert.Equal(CommandIds.TextHardNewline,_model.Hotkeys.Resolve(new(name,Yoake.Core.Hotkeys.KeyModifiers.Shift),contexts)!.CommandId);
    }
    public void Dispose(){_model.Dispose();Directory.Delete(_root,true);}
    private sealed class UnavailableAutomationProvider : Yoake.Core.Automation.IAutomationRuntimeProvider
    {
        public ValueTask<Yoake.Core.Automation.IAutomationScript> LoadAsync(string path, Yoake.Core.Automation.AutomationPathResolver paths, CancellationToken token)
            => ValueTask.FromException<Yoake.Core.Automation.IAutomationScript>(new InvalidOperationException("No interpreter in this persistence test."));
    }
    private sealed class TestDialogs : IEditorDialogs
    {
        public UnsavedChoice Choice=UnsavedChoice.Cancel;
        public Task<string?> OpenSubtitleAsync()=>Task.FromResult<string?>(null);
        public Task<string?> OpenMediaAsync()=>Task.FromResult<string?>(null);
        public Task<string?> SaveSubtitleAsync(string suggestedName)=>Task.FromResult<string?>(null);
        public Task<UnsavedChoice> ConfirmUnsavedAsync(string title)=>Task.FromResult(Choice);
        public Task<bool> ConfirmRevertAsync()=>Task.FromResult(false);
        public Task<string?> ReadClipboardAsync()=>Task.FromResult<string?>(null);
        public Task WriteClipboardAsync(string text)=>Task.CompletedTask;
        public Task ShowStylesAsync(SubtitleEditor editor)=>Task.CompletedTask;
        public Task ShowScriptInfoAsync(SubtitleEditor editor)=>Task.CompletedTask;
        public Task ShowFindAsync(MainWindowViewModel model)=>Task.CompletedTask;
    }
}
