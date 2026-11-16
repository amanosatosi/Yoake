using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Media;
using Yoake.Core.Subtitles;
using Yoake.Native;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal static class InlineInsertionUiVerification
{
    public static async Task VerifyAsync(MainWindow window,MainWindowViewModel model,StartupOptions options)
    {
        async Task Layout()=>await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        async Task Invoke(string id)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId)),"Inline command: "+id);
        var path=Path.Combine(options.ProfileDirectory!,"inline-authoring.ass");
        var fixture=AssDocument.CreateEmpty();new SubtitleEditor(fixture).SetScriptInfo(new Dictionary<string,string> { ["PlayResX"]="640",["PlayResY"]="360" });
        File.WriteAllText(path,fixture.Serialize()+"Dialogue: 0,0:00:01.00,0:00:04.00,Default,HARU,0,0,0,,{\\k30\\future(preserve)}日本語 မြန်မာ é\n");
        Require(model.OpenSubtitle(path),"Open independent inline-authoring document.");await Layout();
        var input=window.FindControl<AssTextBox>("SubtitleText")!;
        var before=model.ActiveEditor!.Document.Serialize();var text=model.SelectedEvent!.Text;
        void SelectBase()
        {
            var start=model.Draft!.Text.IndexOf("日本語",StringComparison.Ordinal);
            input.Focus();input.CaretIndex=input.DisplayIndex(start+3);
            input.SelectionStart=input.DisplayIndex(start);input.SelectionEnd=input.DisplayIndex(start+3);
            Require(model.TextSelectionStart==start&&model.TextSelectionEnd==start+3,"Actual ASS text selection reaches the command model.");
        }
        SelectBase();
        var brackets=window.FindControl<Button>("JapaneseBracketsButton")!;
        Require(brackets.Flyout is MenuFlyout { Items.Count:14 },"All fourteen Toshi bracket pairs are available in the real toolbar popup.");
        var pair=((MenuFlyout)brackets.Flyout!).Items.OfType<MenuItem>().Single(item=>item.Name=="JapaneseBracket_double_corner");
        Require(brackets.Focus(),"The bracket popup accepts keyboard focus.");
        brackets.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=brackets });await Layout();
        Require(pair.Bounds.Height>0&&pair.Focus()&&pair.Command!.CanExecute(null),"The real bracket menu item is realized, focusable and available.");
        pair.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=pair });await Layout();
        Require(model.SelectedEvent.Text==text.Replace("日本語","『日本語』",StringComparison.Ordinal),"Bracket menu preserves the original timing and unknown tags.");
        await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==before,"Bracket insertion undoes losslessly.");SelectBase();
        using(var cancel=new CancellationTokenSource())
        {
            var pending=model.Registry.InvokeAsync("text/insert/furigana",new(model.ActiveDocumentId),cancellationToken:cancel.Token);
            await Layout();var dialog=window.OwnedWindows.OfType<FuriganaWindow>().Single();
            var reading=dialog.FindControl<TextBox>("FuriganaReading")!;
            Require(reading.Bounds.Width>0&&reading.Focus(),"The realized reading accepts keyboard focus.");
            var presenter=reading.GetVisualDescendants().OfType<TextPresenter>().Single();presenter.PreeditText="に";
            reading.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=reading });
            Require(dialog.IsVisible&&!pending.IsCompleted,"IME Enter belongs to composition and cannot insert or close the dialog.");presenter.PreeditText=null;
            reading.Text="にほんご";dialog.FindControl<TextBox>("FuriganaOpposite")!.Text="Japanese";
            await Layout();
            Require(dialog.FindControl<TextBox>("FuriganaPreview")!.Text!.Contains("<日本語|にほんご|Japanese>",StringComparison.Ordinal),"The native dialog previews actual Mangetsu double-sided reading syntax.");
            cancel.Cancel();Require(!await pending&&model.ActiveEditor.Document.Serialize()==before,"Cancel closes the dialog and leaves source and history untouched.");
        }
        SelectBase();
        var applyTask=model.Registry.InvokeAsync("text/insert/furigana",new(model.ActiveDocumentId));await Layout();
        var applyDialog=window.OwnedWindows.OfType<FuriganaWindow>().Single();
        applyDialog.FindControl<TextBox>("FuriganaReading")!.Text="にほんご";applyDialog.FindControl<TextBox>("FuriganaOpposite")!.Text="Japanese";
        await Layout();
        var apply=applyDialog.FindControl<Button>("ApplyFurigana")!;Require(apply.Focus()&&apply.IsEnabled,"Insert accepts keyboard focus.");
        apply.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=apply });
        Require(await applyTask,"Native furigana Insert completes.");
        var after=model.ActiveEditor.Document.Serialize();Require(model.SelectedEvent.Text==text.Replace("日本語","<日本語|にほんご|Japanese>",StringComparison.Ordinal),"Insert changes only the selected base text.");
        await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==before,"Furigana undoes in one operation.");
        await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==after,"Furigana redo restores exact source.");
        if(OperatingSystem.IsWindows())await Task.Run(()=>
        {
            using var renderer=new MangetsuSubtitleRenderer(before,640,360);
            var plain=new DecodedVideoFrame(640,360,2560,new byte[640*360*4]);renderer.Composite(plain,2);
            renderer.UpdateTrack(after);
            var ruby=new DecodedVideoFrame(640,360,2560,new byte[640*360*4]);renderer.Composite(ruby,2);
            Require(plain.Pixels.Any(value=>value!=0)&&ruby.Pixels.Any(value=>value!=0)&&!plain.Pixels.SequenceEqual(ruby.Pixels),"Actual Mangetsu must render the authored double-sided furigana differently from plain base text.");
        });
        await Invoke(CommandIds.SubtitleSave);Require(File.ReadAllText(path)==after,"Save preserves the exact generated ASS and Unicode.");
        var owner=model.ActiveDocumentId!.Value;Require(await model.CloseDocumentAsync(owner)&&model.OpenSubtitle(path)&&model.ActiveEditor!.Document.Serialize()==after,"Close/reopen preserves the authored reading and every other ASS property.");
        Console.WriteLine("PASS: native Japanese bracket popup/selection/undo and furigana focus, IME Enter, literal preview, Cancel, Insert, one-step undo/redo and exact ASS save/reopen; Windows also verifies actual Mangetsu ruby pixels.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
