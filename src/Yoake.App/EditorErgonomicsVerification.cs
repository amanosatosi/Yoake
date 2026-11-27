using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal static class EditorErgonomicsVerification
{
    public static async Task VerifyAsync(MainWindow window,MainWindowViewModel model,StartupOptions options)
    {
        var originalShown=model.ShowOriginal;var source=model.ActiveEditor!.Document.Serialize();var undo=model.ActiveEditor.Undo.NextUndoName;
        if(!originalShown)Require(await model.Registry.InvokeAsync("edit/show_original",new(model.ActiveDocumentId)),"Show Original through its semantic action.");
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        var original=window.FindControl<TextBox>("OriginalText")!;var edit=window.FindControl<TextBox>("SubtitleText")!;
        Require(original.IsVisible&&original.IsReadOnly&&original.Bounds.Height>=64&&
            Math.Abs(original.Bounds.Height-edit.Bounds.Height)<=1&&Math.Abs(original.Bounds.Width-edit.Bounds.Width)<=1,
            $"Original and edit inputs must have equal real dimensions: {original.Bounds} / {edit.Bounds}.");
        var editors=window.FindControl<Grid>("SubtitleTextEditors")!;
        var left=original.TranslatePoint(default,editors)!.Value;var right=edit.TranslatePoint(default,editors)!.Value;
        Require(Math.Abs(left.Y-right.Y)<=1&&left.X+original.Bounds.Width<=right.X,"The original and translation must be horizontally aligned, not stacked.");
        var minimum=UiFontSize(model)*1.6;var compact=Math.Max(24,Math.Ceiling(minimum));
        foreach(var name in new[]{"OriginalRevert","OriginalClear","OriginalClearText","OriginalInsert"})
        {
            var button=window.FindControl<Button>(name)!;
            var point=button.TranslatePoint(default,editors)!.Value;
            Require(button.IsEffectivelyVisible&&button.Content is PathIcon&&button.Bounds.Height>=24&&button.Bounds.Height<=compact+2&&button.Bounds.Width<=32&&point.X>=left.X+original.Bounds.Width-1&&point.X+button.Bounds.Width<=right.X+1,"Original action icons must fit between the inputs: "+name+" "+button.Bounds);
        }
        var colors=window.FindControl<WrapPanel>("FormattingToolbar")!.GetVisualDescendants().OfType<AssColorButton>().ToArray();
        Require(colors.Length==4,"Four subtitle color channels remain available.");
        foreach(var color in colors)
        {
            Require(color.Bounds.Width<=48&&color.Bounds.Height<=compact+2,"Color buttons keep desktop toolbar density: "+color.Bounds);
            Require(color.GetVisualDescendants().OfType<PathIcon>().All(icon=>icon.Bounds.Width==24&&icon.Bounds.Height==24),"Original Aegisub color artwork must render at its legible native size.");
        }
        Require(model.ActiveEditor.Document.Serialize()==source&&model.ActiveEditor.Undo.NextUndoName==undo,"Editor presentation must not change ASS or undo history.");
        using(var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96)))
        {bitmap.Render(window);bitmap.Save(Path.Combine(options.ProfileDirectory!,"editor-original-and-colors.png"));}
        Require(await model.Registry.InvokeAsync("edit/show_original",new(model.ActiveDocumentId)),"Hide Original to verify single-editor layout.");
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        Require(!original.IsEffectivelyVisible&&!window.FindControl<StackPanel>("OriginalActions")!.IsEffectivelyVisible&&Math.Abs(edit.Bounds.Width-editors.Bounds.Width)<=1,"Hiding Original returns the entire width to the main editor.");
        if(originalShown)Require(await model.Registry.InvokeAsync("edit/show_original",new(model.ActiveDocumentId)),"Restore the Original visibility preference.");
    }
    private static double UiFontSize(MainWindowViewModel model)=>model.UiFontSize??12;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
