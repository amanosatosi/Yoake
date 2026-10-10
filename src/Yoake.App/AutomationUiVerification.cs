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
""");
                _loading = model.LoadAutomationScriptAsync(_path, _document); _stage++; return false;
            case 1:
                if (!_loading!.IsCompleted) return false;
                _script = _loading.GetAwaiter().GetResult(); Require(_script.Error is null, "Packaged Lua master must load: " + _script.Error);
                Require(_script.Status.StartsWith("1 macros", StringComparison.Ordinal), "Manager metadata must expose registered macros.");
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
                File.AppendAllText(report + ".automation.txt", "PASS: packaged Lua runtime, Unicode master path, live validation/isactive, all ten dialog classes, custom/default buttons, responsive dispatcher, real mutation/insertion, lossless source, grid/draft refresh, actual Mangetsu pixel change, selection undo/redo.\n");
                _stage++; return true;
            default: return true;
        }
    }

    private void Invoke(string id)
    {
        var invocation = model.Registry.InvokeAsync(id, new(_document));
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
