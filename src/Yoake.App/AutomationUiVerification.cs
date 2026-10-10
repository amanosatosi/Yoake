using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yoake.Core.Automation;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Real packaged runtime, window, modal controls and document command registry.
// The dispatcher keeps ticking while Lua waits for the actual authoring dialog.
internal sealed class AutomationUiVerification(MainWindow window, MainWindowViewModel model, string report, string mediaFixtures)
{
    private int _stage = -1;
    private Task<bool>? _mediaLoading;
    private byte[]? _beforePixels;
    private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(90);
    private Task<AutomationScriptItem>? _loading;
    private Task<bool>? _running;
    private AutomationScriptItem? _script;
    private AssEvent? _original;
    private string _before = "", _after = "", _command = "", _path = "";
    private string _exportPath = "";
    private string? _undoName, _redoName;
    private bool _dirty;
    private bool _reuseStarted;
    private Task<bool>? _managerTask;
    private AutomationManagerWindow? _manager;
    private Guid _originalDocument;
    private string _templateBefore = "", _templateAfter = "", _templateCommand = "", _templatePath = "";
    private AssEvent? _templateSong;
    private byte[]? _templatePixels;
    private int _count, _responsiveTicks;
    private long _preview;
    private Guid _document;
    private bool _checked;
    private const string EditedText = "日本語 မြန်မာ é 👩‍👩‍👧‍👦";

    public bool Tick()
    {
        Require(DateTime.UtcNow < _deadline, $"Packaged Automation verification timed out at stage {_stage}: {model.SubtitleStatus}; {model.MediaStatus}");
        switch (_stage)
        {
            case -1:
                _mediaLoading ??= model.OpenMediaAsync(Path.Combine(mediaFixtures, "av.avi"));
                if (!_mediaLoading.IsCompleted) return false;
                Require(_mediaLoading.GetAwaiter().GetResult(), "Packaged Automation preview requires actual video: " + model.MediaStatus);
                if (model.VideoFrame is null || model.DisplayedPreviewRevision < model.PreviewRevision) return false;
                _stage++; return false;
            case 0:
                _document = model.Tabs.Single(t => t.IsActive).Id;
                _original = model.Events.First(e => !e.IsComment);
                model.SelectedEvent = _original; model.SetSelectedEvents([_original]);
                _before = model.ActiveEditor!.Document.Serialize(); _count = model.Events.Count;
                _beforePixels = Pixels();
                var directory = Path.Combine(Path.GetDirectoryName(report)!, "automation-ui-日本語"); Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, "authoring.lua");
                File.WriteAllText(_path, """
script_name='Packaged authoring verification'
include('utils.lua')
aegisub.register_macro('Verification/Authoring','Real dialog and mutation',function(subs,selected,active)
  local button,values=aegisub.dialog.display({
    {class='label',label='Subtitle authoring',x=0,y=0,width=2},
    {class='edit',name='text',text='initial',x=0,y=1,width=2},
    {class='textbox',name='notes',text='日本語',x=0,y=2,width=2},
    {class='intedit',name='integer',value=2,min=0,max=10,x=0,y=3},
    {class='floatedit',name='number',value=1.25,min=-10,max=10,step=.25,x=1,y=3},
    {class='dropdown',name='mode',items={'One','Two'},value='Two',x=0,y=4},
    {class='checkbox',name='enabled',label='Enabled',value=true,x=1,y=4},
    {class='color',name='rgb',value='#112233',x=0,y=5},
    {class='coloralpha',name='rgba',value='#11223380',x=1,y=5},
    {class='alpha',name='alpha',text='&H80&',x=0,y=6}
  },{'Apply','Cancel'},{ok='Apply',cancel='Cancel'})
  if not button then aegisub.cancel() end
  assert(button=='Apply' and values.integer==2 and values.number==1.25)
  assert(values.mode=='Two' and values.enabled and values.alpha=='&H80&')
  assert(values.rgb=='#112233' and values.rgba=='#11223380')
  local line=subs[active]
  line.text='{\\pos(100,100)\\bord2\\distort(0,0,1,1)}'..values.text
  line.start_time=0; line.end_time=60000; line.comment=false
  subs[active]=line
  local copy=table.copy(line); copy.effect='automation-ui-generated'; subs.append(copy)
  aegisub.set_undo_point('Packaged Automation authoring')
  return {#subs},#subs
end,function(subs,selected,active)
  return #selected==1 and active>0,'Validated packaged macro'
end,function() return true end)
aegisub.register_macro('Verification/Defaults','Default buttons and clipboard',function()
  local clipboard=require('aegisub.clipboard')
  local previous=clipboard.get()
  assert(clipboard.set('日本語 မြန်မာ clipboard'))
  assert(clipboard.get()=='日本語 မြန်မာ clipboard')
  assert(clipboard.set(previous or ''))
  local button,v=aegisub.dialog.display({
    {class='textbox',name='notes',text='initial',width=2,height=2},
    {class='intedit',name='maximum',value=2147483647,y=2},
    {class='intedit',name='minimum',value=-2147483648,x=1,y=2},
    {class='floatedit',name='fraction',value=-.125,y=3},
    {class='dropdown',name='empty',items={},value='',x=1,y=3},
    {class='checkbox',name='check',label='Check',value=true,y=4}
  })
  assert(button=='' and v.maximum==2147483647 and v.minimum==-2147483648)
  assert(v.fraction==-.125 and v.empty=='' and v.check==false)
  assert(v.notes=='日本語\nမြန်မာ')
end)
aegisub.register_macro('Verification/Cancel','Forced loop cancellation',function(subs,selected,active)
  local line=subs[active]; line.text='temporary cancelled edit'; subs[active]=line
  aegisub.set_undo_point('Temporary cancelled checkpoint')
  aegisub.progress.title('Cancel verification'); aegisub.progress.task('Forced loop'); aegisub.progress.set(17)
  aegisub.log(2,'visible cancellation log\n'); aegisub.log(4,'hidden verbose log\n')
  while true do end
end)
aegisub.register_macro('Verification/Failure','Rollback and traceback',function(subs,selected,active)
  local line=subs[active]; line.text='temporary failed edit'; subs[active]=line
  aegisub.set_undo_point('Temporary failed checkpoint')
  error('packaged intentional failure')
end)
aegisub.register_filter('Verification export','Isolated configured export',1000,function(subs,settings)
  for i=1,#subs do
    local line=subs[i]
    if line.class=='dialogue' then line.text=line.text..settings.suffix; subs[i]=line end
  end
end,function()
  return {{class='edit',name='suffix',text=' export 日本語',width=2}}
end)
""");
                _loading = model.LoadAutomationScriptAsync(_path, _document); _stage++; return false;
            case 1:
                if (!_loading!.IsCompleted) return false;
                _script = _loading.GetAwaiter().GetResult(); Require(_script.Error is null, "Packaged Lua master must load: " + _script.Error);
                Require(_script.Status.StartsWith("4 macros, 1 filters", StringComparison.Ordinal), "Manager metadata must expose registered macros and filters.");
                _command = AutomationCommandCatalog.CommandId(_path, "Verification/Authoring");
                if (!model.Registry.CanExecute(_command, new(_document))) return false;
                Require(model.Registry.GetRequired(_command).IsChecked(new(_document)), "Actual Lua isactive must reach the command state.");
                _checked = true;
                _running = model.Registry.InvokeAsync(_command, new(_document)).AsTask(); _stage++; return false;
            case 2:
                _responsiveTicks++;
                var dialog = Windows(window).OfType<AutomationDialog>().FirstOrDefault(w => w.IsVisible);
                if (dialog is null)
                {
                    Require(!_running!.IsCompleted, "Macro finished before its authoring dialog: " + model.SubtitleStatus); return false;
                }
                var entry = dialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "AutomationControl_text");
                Require(entry.Bounds.Width >= 100 && entry.IsVisible, "Real Unicode edit control must be laid out.");
                Require(dialog.GetVisualDescendants().OfType<Control>().Count(c => c.Name?.StartsWith("AutomationControl_", StringComparison.Ordinal) == true) == 10, "All ten classes must realize actual controls.");
                entry.SetCurrentValue(TextBox.TextProperty, EditedText);
                var accept = dialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AutomationButton_0");
                Require(accept.IsDefault, "Third argument must map Apply to the default action.");
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); _stage++; return false;
            case 3:
                _responsiveTicks++;
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Packaged macro command failed: " + model.SubtitleStatus);
                Require(_checked && _responsiveTicks >= 2, "Actual Lua validation and responsive modal dispatcher must execute.");
                Require(model.Events.Count == _count + 1 && model.SelectedEvent?.Effect == "automation-ui-generated", "Returned file indexes must select the generated grid row.");
                Require(model.SelectedEvents.Count == 1 && ReferenceEquals(model.SelectedEvents[0], model.SelectedEvent), "Returned active/selection must agree.");
                Require(_original!.Text.EndsWith(EditedText, StringComparison.Ordinal) && _original.Text.Contains("\\distort(0,0,1,1)", StringComparison.Ordinal), "Dialog text and unknown Mangetsu syntax must survive Lua writeback.");
                Require(model.Draft?.Text == model.SelectedEvent!.Text && model.PreviewSource()!.Contains(EditedText, StringComparison.Ordinal), "Draft and live preview source must refresh after batch commit.");
                _after = model.ActiveEditor!.Document.Serialize(); _preview = model.PreviewRevision;
                _stage++; return false;
            case 4:
                if (model.DisplayedPreviewRevision < _preview) return false;
                Require(!_beforePixels!.SequenceEqual(Pixels()), "Macro writeback must change actual packaged Mangetsu video pixels.");
                Invoke(CommandIds.EditUndo);
                Require(model.ActiveEditor!.Document.Serialize() == _before, "Macro undo must restore exact ASS source.");
                Require(ReferenceEquals(model.SelectedEvent, _original) && model.SelectedEvents.Count == 1 && ReferenceEquals(model.SelectedEvents[0], _original), "Macro undo must restore original active row and selection.");
                _stage++; return false;
            case 5:
                Invoke(CommandIds.EditRedo);
                Require(model.ActiveEditor!.Document.Serialize() == _after, "Macro redo must restore exact generated source.");
                Require(model.SelectedEvent?.Effect == "automation-ui-generated" && model.SelectedEvents.Count == 1, "Macro redo must restore returned active row and selection.");
                _stage++; return false;
            case 6:
                var rows = window.FindControl<ListBox>("SubtitleRows")!;
                Require(rows.SelectedItems?.Contains(model.SelectedEvent!) == true, "Actual grid must show the returned selection after redo.");
                Invoke(CommandIds.EditUndo); Require(model.ActiveEditor!.Document.Serialize() == _before, "Verification cleanup must restore pre-macro source.");
                _undoName = model.ActiveEditor.Undo.NextUndoName; _redoName = model.ActiveEditor.Undo.NextRedoName; _dirty = model.ActiveEditor.IsDirty;
                _stage++; return false;
            case 7:
                if (!StartMacro("Defaults")) return false;
                _stage++; return false;
            case 8:
                if (!AcceptDefaults()) return false;
                _stage++; return false;
            case 9:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Default dialog or real clipboard round trip failed: " + model.SubtitleStatus);
                RequireUnchanged();
                if (!StartMacro("Cancel")) return false;
                _stage++; return false;
            case 10:
                var progress = Windows(window).OfType<AutomationProgressWindow>().FirstOrDefault(w => w.IsVisible && w.Title == "Cancel verification");
                if (progress is null) { Require(!_running!.IsCompleted, "Cancellation macro must enter its actual progress window."); return false; }
                Require(progress.GetVisualDescendants().OfType<ProgressBar>().Single().Value == 17, "Actual progress must show script percentage.");
                var output = progress.GetVisualDescendants().OfType<TextBox>().Single().Text ?? "";
                if (!output.Contains("visible cancellation log", StringComparison.Ordinal)) return false;
                Require(!output.Contains("hidden verbose log", StringComparison.Ordinal), "Verbose log must stay out of default progress output.");
                Click(progress.GetVisualDescendants().OfType<Button>().Single()); _stage++; return false;
            case 11:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult() && model.SubtitleStatus.Contains("cancelled", StringComparison.OrdinalIgnoreCase), "Forced native loop cancellation must finish through the command boundary.");
                RequireUnchanged(); CloseProgress();
                if (!StartMacro("Failure")) return false;
                _stage++; return false;
            case 12:
                if (!_running!.IsCompleted) return false;
                Require(!_running.GetAwaiter().GetResult(), "Intentional Lua failure must be reported by the command boundary.");
                RequireUnchanged();
                var failureWindow = Windows(window).OfType<AutomationProgressWindow>().Single(w => w.IsVisible);
                var failureText = failureWindow.GetVisualDescendants().OfType<TextBox>().Single().Text ?? "";
                Require(failureText.Contains("packaged intentional failure", StringComparison.Ordinal) && failureText.Contains("stack traceback:", StringComparison.Ordinal) && failureText.Contains(_path, StringComparison.Ordinal), "Actual failure output must preserve full Unicode script path and traceback.");
                CloseProgress();
                _stage++; return false;
            case 13:
                if (!_reuseStarted) { _reuseStarted = StartMacro("Defaults"); if (!_reuseStarted) return false; }
                if (!AcceptDefaults()) return false;
                _stage++; return false;
            case 14:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Interpreter must remain usable after cancellation and failure."); RequireUnchanged();
                _exportPath = Path.Combine(Path.GetDirectoryName(_path)!, "export-日本語.ass");
                _running = model.Registry.InvokeAsync(CommandIds.AutomationExport, new(_document), _exportPath).AsTask(); _stage++; return false;
            case 15:
                var export = Windows(window).OfType<AutomationExportWindow>().FirstOrDefault(w => w.IsVisible);
                if (export is null) { Require(!_running!.IsCompleted, "Export must show its real settings dialog."); return false; }
                var filter = export.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Verification export"));
                filter.IsChecked = true;
                _stage++; return false;
            case 16:
                var exportDialog = Windows(window).OfType<AutomationExportWindow>().Single(w => w.IsVisible);
                var suffix = exportDialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "AutomationControl_suffix");
                Require(suffix.IsVisible && suffix.Bounds.Width >= 100, "Checked filter must reveal its actual embedded settings.");
                suffix.Text = " export 日本語 မြန်မာ";
                Click(exportDialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AutomationExportAccept")); _stage++; return false;
            case 17:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult() && File.Exists(_exportPath), "Configured export must write an ASS output file: " + model.SubtitleStatus);
                RequireUnchanged();
                var exported = AssDocument.Load(_exportPath);
                Require(exported.Events.Count == _count && exported.Events.All(e => e.Text.EndsWith(" export 日本語 မြန်မာ", StringComparison.Ordinal)), "Actual selected filter must process the isolated output copy.");
                _managerTask = model.Registry.InvokeAsync(CommandIds.AutomationManager, new(_document)).AsTask(); _stage++; return false;
            case 18:
                _manager = Windows(window).OfType<AutomationManagerWindow>().FirstOrDefault(w => w.IsVisible);
                if (_manager is null) return false;
                var scripts = _manager.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "AutomationScriptList"); scripts.SelectedItem = _script;
                var details = _manager.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "AutomationScriptDetails").Text ?? "";
                Require(details.Contains(_path, StringComparison.Ordinal) && details.Contains("4 macros, 1 filters", StringComparison.Ordinal), "Actual Manager must show loaded metadata and Unicode master path.");
                File.WriteAllText(_path, "script_name='Reloaded packaged fixture'; aegisub.register_macro('Verification/Reloaded','',function() end)");
                _running = model.Registry.InvokeAsync(CommandIds.AutomationReload, new(_document), _script).AsTask(); _stage++; return false;
            case 19:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult() && _script!.Error is null, "Manager reload must replace the interpreter safely.");
                Require(!model.Registry.TryGet(_command, out _) && model.AutomationMacros.Count(m => m.Name == "Verification/Reloaded") == 1, "Reload must remove old commands and register the replacement once.");
                _running = model.Registry.InvokeAsync(CommandIds.AutomationReload, new(_document), _script).AsTask(); _stage++; return false;
            case 20:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult() && model.AutomationMacros.Count(m => m.Name == "Verification/Reloaded") == 1, "Repeated Manager reload must retain one stable command.");
                _manager!.Close(); _stage++; return false;
            case 21:
                if (!_managerTask!.IsCompleted) return false;
                Require(_managerTask.GetAwaiter().GetResult(), "Manager must close through its stable command."); RequireUnchanged();
                var templateDocument = AssDocument.CreateEmpty(); var templateEditor = new SubtitleEditor(templateDocument);
                var template = templateEditor.Insert(null, after: true); template.Effect = "template syl noblank";
                template.Text = "{\\pos($scenter,$smiddle)\\bord5\\frz12\\distort(0,0,1,1)\\k$sdur}";
                var song = templateEditor.Insert(template, after: true); song.Effect = ""; song.Text = "{\\k20}日{\\kf30}本語"; song.Start = "0:00:00.00"; song.End = "0:01:00.00";
                templateEditor.ToggleComment([template]);
                _templatePath = Path.Combine(Path.GetDirectoryName(_path)!, "templater-日本語.ass");
                File.WriteAllText(_templatePath, templateDocument.Serialize() + "[Future Section]\nMystery: preserve\n");
                _originalDocument = _document;
                Require(model.OpenSubtitle(_templatePath), "Actual application must open the karaoke fixture: " + model.SubtitleStatus);
                _document = model.Tabs.Single(t => t.IsActive).Id;
                _templateSong = model.Events.Single(e => !e.IsComment); model.SelectedEvent = _templateSong; model.SetSelectedEvents([_templateSong]);
                _running = model.Registry.InvokeAsync(CommandIds.AutomationLoad, new(_document), _path).AsTask(); _stage++; return false;
            case 22:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Document-local script Add must persist its project reference.");
                var movedDirectory = Path.Combine(Path.GetDirectoryName(_templatePath)!, "moved"); Directory.CreateDirectory(movedDirectory);
                _templatePath = Path.Combine(movedDirectory, "saved-日本語.ass");
                Require(model.SaveActiveSubtitle(_templatePath), "Actual Save As must succeed for the document-local script fixture.");
                var reference = AssDocument.Load(_templatePath).GetSectionValue("[Aegisub Project Garbage]", "Automation Scripts");
                Require(AutomationScriptReference.Resolve(reference, _templatePath, model.AutomationBaseDirectory) == _path, "Actual Save As must retain the original script location.");
                _templateBefore = model.ActiveEditor!.Document.Serialize();
                _mediaLoading = model.OpenMediaAsync(Path.Combine(mediaFixtures, "av.avi")); _stage++; return false;
            case 23:
                if (!_mediaLoading!.IsCompleted) return false;
                Require(_mediaLoading.GetAwaiter().GetResult(), "Karaoke preview must use this tab's actual independent media session.");
                if (model.VideoFrame is null || model.DisplayedPreviewRevision < model.PreviewRevision) return false;
                var templater = model.AutomationScripts.FirstOrDefault(s => s.DocumentId is null && s.Path.EndsWith("kara-templater.lua", StringComparison.OrdinalIgnoreCase));
                if (templater is null || templater.Status == "Loading") return false;
                Require(templater.Error is null, "Shipped unmodified Karaoke Templater must autoload: " + templater.Error);
                _templateCommand = AutomationCommandCatalog.CommandId(templater.Path, "Apply karaoke template");
                if (!model.Registry.CanExecute(_templateCommand, new(_document))) return false;
                _templatePixels = Pixels(); _running = model.Registry.InvokeAsync(_templateCommand, new(_document)).AsTask(); _stage++; return false;
            case 24:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Unmodified packaged Karaoke Templater must generate actual effects: " + model.SubtitleStatus);
                var effects = model.Events.Where(e => e.Effect == "fx").ToArray();
                Require(effects.Length == 2 && effects.All(e => !e.IsComment && e.Text.Contains("\\pos(", StringComparison.Ordinal) && e.Text.Contains("\\distort(0,0,1,1)", StringComparison.Ordinal)), "Actual templater must generate syllable effects while preserving Mangetsu syntax.");
                Require(_templateSong!.IsComment && model.ActiveEditor!.Document.Styles.Any(s => s.Name == "Default-furigana"), "Real karaskel must preprocess the song and produce furigana style data.");
                _templateAfter = model.ActiveEditor!.Document.Serialize(); _preview = model.PreviewRevision; CloseProgress(); _stage++; return false;
            case 25:
                if (model.DisplayedPreviewRevision < _preview) return false;
                Require(!_templatePixels!.SequenceEqual(Pixels()), "Actual Karaoke Templater effects must change this tab's Mangetsu video pixels.");
                Invoke(CommandIds.EditUndo); Require(model.ActiveEditor!.Document.Serialize() == _templateBefore, "Karaoke Templater undo must restore exact source, project references and unknown sections.");
                Invoke(CommandIds.EditRedo); Require(model.ActiveEditor.Document.Serialize() == _templateAfter, "Karaoke Templater redo must restore exact generated source.");
                Invoke(CommandIds.EditUndo);
                _running = model.CloseDocumentAsync(_document); _stage++; return false;
            case 26:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Saved karaoke fixture must close without an unsaved prompt after undo.");
                Require(model.OpenSubtitle(_templatePath), "Document-local script fixture must reopen from disk.");
                _document = model.Tabs.Single(t => t.IsActive).Id; _stage++; return false;
            case 27:
                var reopened = model.AutomationScripts.FirstOrDefault(s => s.DocumentId == _document && s.Path == _path);
                if (reopened is null || reopened.Status == "Loading") return false;
                Require(reopened.Error is null && model.AutomationMacros.Count(m => m.Name == "Verification/Reloaded") == 1, "Reopened Save As document must discover its original local script and register one replacement command.");
                _running = model.CloseDocumentAsync(_document); _stage++; return false;
            case 28:
                if (!_running!.IsCompleted) return false;
                Require(_running.GetAwaiter().GetResult(), "Reopened document must close cleanly.");
                _document = _originalDocument; Invoke(CommandIds.WorkspaceActivateTab, _document);
                RequireUnchanged();
                File.AppendAllText(report + ".automation.txt", "PASS: packaged Lua runtime, Unicode master path, live validation/isactive, all ten dialog classes, custom/default buttons, responsive dispatcher, real mutation/insertion, lossless source, grid/draft refresh, actual Mangetsu pixel change, selection undo/redo.\n");
                File.AppendAllText(report + ".automation.txt", "PASS: actual Unicode clipboard round trip, default empty-label OK result, signed integer limits, negative float, empty dropdown, multiline text, checkbox edit, forced loop cancellation and checkpoint rollback, log-level filtering, full failure traceback, interpreter reuse, embedded export settings and isolated Unicode ASS output with unchanged live history.\n");
                File.AppendAllText(report + ".automation.txt", "PASS: actual Manager metadata and repeated reload command cleanup, document-local Add and Save As persistence/reopen, shipped unmodified Karaoke Templater with real karaskel/furigana, generated syllables/Mangetsu syntax and changed video pixels, exact source undo/redo, independent tab media and clean disposal.\n");
                _stage++; return true;
            default: return true;
        }
    }

    private bool StartMacro(string name)
    {
        var command = AutomationCommandCatalog.CommandId(_path, "Verification/" + name);
        if (!model.Registry.CanExecute(command, new(_document))) return false;
        _running = model.Registry.InvokeAsync(command, new(_document)).AsTask(); return true;
    }
    private bool AcceptDefaults()
    {
        var dialog = Windows(window).OfType<AutomationDialog>().FirstOrDefault(w => w.IsVisible);
        if (dialog is null) { Require(!_running!.IsCompleted, "Default dialog must be shown: " + model.SubtitleStatus); return false; }
        dialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "AutomationControl_notes").Text = "日本語\nမြန်မာ";
        dialog.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "AutomationControl_check").IsChecked = false;
        var accept = dialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "AutomationButton_0");
        Require(accept.IsDefault && Equals(accept.Content, "OK"), "Default buttons must present the actual OK action."); Click(accept); return true;
    }
    private void RequireUnchanged()
    {
        Require(model.ActiveEditor!.Document.Serialize() == _before && model.ActiveEditor.Undo.NextUndoName == _undoName && model.ActiveEditor.Undo.NextRedoName == _redoName && model.ActiveEditor.IsDirty == _dirty,
            "Cancelled, failed and export workflows must preserve live source, dirty state and undo/redo branch.");
        Require(ReferenceEquals(model.SelectedEvent, _original) && model.SelectedEvents.Count == 1 && ReferenceEquals(model.SelectedEvents[0], _original), "Uncommitted workflows must retain active row and selection.");
    }
    private void CloseProgress()
    {
        foreach (var progress in Windows(window).OfType<AutomationProgressWindow>().Where(w => w.IsVisible).ToArray())
            Click(progress.GetVisualDescendants().OfType<Button>().Single());
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private void Invoke(string id, object? parameter = null)
    {
        var invocation = model.Registry.InvokeAsync(id, new(_document), parameter);
        Require(invocation.IsCompletedSuccessfully && invocation.Result, "Automation verification history command failed: " + id);
    }
    private byte[] Pixels()
    {
        using var buffer = model.VideoFrame!.Lock(); var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length); return pixels;
    }
    private static IEnumerable<Window> Windows(Window root)
    {
        yield return root;
        foreach (var child in root.OwnedWindows.OfType<Window>())
            foreach (var descendant in Windows(child)) yield return descendant;
    }
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
