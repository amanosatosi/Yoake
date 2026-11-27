using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
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
        var left=edit.TranslatePoint(default,editors)!.Value;var right=original.TranslatePoint(default,editors)!.Value;
        Require(Math.Abs(left.Y-right.Y)<=1&&left.X+edit.Bounds.Width<=right.X+1,"Translation must be left of the original, with both horizontally aligned.");
        var minimum=UiFontSize(model)*1.6;var compact=Math.Max(24,Math.Ceiling(minimum));
        foreach(var name in new[]{"OriginalRevert","OriginalClear","OriginalClearText","OriginalInsert"})
        {
            var button=window.FindControl<Button>(name)!;
            var point=button.TranslatePoint(default,editors)!.Value;
            Require(button.IsEffectivelyVisible&&(name=="OriginalClearText"?button.Content is Grid:button.Content is PathIcon)&&button.Bounds.Height>=24&&button.Bounds.Height<=compact+2&&button.Bounds.Width<=32&&point.X>=left.X+edit.Bounds.Width-1&&point.X+button.Bounds.Width<=right.X+1,"Original action icons must fit between the inputs: "+name+" "+button.Bounds);
        }
        var insert=(PathIcon)window.FindControl<Button>("OriginalInsert")!.Content!;
        var revert=(PathIcon)window.FindControl<Button>("OriginalRevert")!.Content!;
        var clear=(PathIcon)window.FindControl<Button>("OriginalClear")!.Content!;
        var glyph=window.FindControl<PathIcon>("OriginalClearTextGlyph")!;
        var cross=window.FindControl<PathIcon>("OriginalClearTextCross")!;
        Require(Equals(insert.Foreground,revert.Foreground)&&Equals(insert.Foreground,glyph.Foreground)&&
            Equals(clear.Foreground,cross.Foreground)&&!Equals(glyph.Foreground,cross.Foreground),
            "Original action icons use the primary palette; delete uses warning, and Clear Text combines both.");
        var colors=window.FindControl<WrapPanel>("FormattingToolbar")!.GetVisualDescendants().OfType<AssColorButton>().ToArray();
        Require(colors.Length==4,"Four subtitle color channels remain available.");
        foreach(var color in colors)
        {
            var button=color.GetVisualDescendants().OfType<Button>().Single();
            Require(color.IsEffectivelyVisible&&color.Bounds.Width>=30&&color.Bounds.Width<=48&&color.Bounds.Height>=24&&color.Bounds.Height<=compact+2&&
                button.Bounds.Height<=compact+2&&button.Command is not null,"Color buttons keep desktop toolbar density: "+color.Bounds+"; inner button "+button.Bounds);
            var icons=color.GetVisualDescendants().OfType<PathIcon>().ToArray();
            Require(icons.Length==2&&icons.All(icon=>icon.IsEffectivelyVisible&&icon.Bounds.Width==24&&icon.Bounds.Height==24&&
                icon.TranslatePoint(default,color) is {} p&&p.Y>=-0.5&&p.Y+icon.Bounds.Height<=color.Bounds.Height+0.5),
                "Original Aegisub color artwork must render at its legible native size inside the compact button.");
        }
        await VerifyStableFocusAsync(window,edit,original);
        Require(model.ActiveEditor.Document.Serialize()==source&&model.ActiveEditor.Undo.NextUndoName==undo,"Editor presentation must not change ASS or undo history.");
        using(var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96)))
        {bitmap.Render(window);bitmap.Save(Path.Combine(options.ProfileDirectory!,"editor-original-and-colors.png"));}
        Require(await model.Registry.InvokeAsync("edit/show_original",new(model.ActiveDocumentId)),"Hide Original to verify single-editor layout.");
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        Require(!original.IsEffectivelyVisible&&!window.FindControl<StackPanel>("OriginalActions")!.IsEffectivelyVisible&&Math.Abs(edit.Bounds.Width-editors.Bounds.Width)<=1,"Hiding Original returns the entire width to the main editor.");
        if(originalShown)Require(await model.Registry.InvokeAsync("edit/show_original",new(model.ActiveDocumentId)),"Restore the Original visibility preference.");
    }
    private sealed record EditorGeometry(Rect Outer,Rect Border,Rect Presenter,Thickness BorderThickness,Thickness Padding,IBrush? Background,IBrush? BorderBrush);
    private static EditorGeometry CaptureGeometry(TextBox box,Control root)
    {
        var border=box.GetVisualDescendants().OfType<Border>().Single(b=>b.Name=="PART_BorderElement");
        var presenter=box.GetVisualDescendants().OfType<TextPresenter>().Single(p=>p.Name=="PART_TextPresenter");
        Rect Position(Control c)=>new(c.TranslatePoint(default,root)!.Value,c.Bounds.Size);
        return new(Position(box),Position(border),Position(presenter),border.BorderThickness,box.Padding,border.Background,border.BorderBrush);
    }
    private static async Task VerifyStableFocusAsync(MainWindow window,TextBox edit,TextBox original)
    {
        async Task Focus(TextBox box)
        {
            window.Activate();
            Require(box.Focus(),"Editor must accept keyboard focus: "+box.Name);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
            Require(box.IsKeyboardFocusWithin,"Focus must reach the actual editor: "+box.Name);
        }
        await Focus(original);
        var editNormal=CaptureGeometry(edit,window);var originalFocused=CaptureGeometry(original,window);
        await Focus(edit);
        var editFocused=CaptureGeometry(edit,window);var originalNormal=CaptureGeometry(original,window);
        Check(editNormal,editFocused,"Translation");
        Check(originalNormal,originalFocused,"Original");
        await Focus(original);
        Check(editNormal,CaptureGeometry(edit,window),"Translation after losing focus",false);
        await Focus(edit);
        Check(originalNormal,CaptureGeometry(original,window),"Original after losing focus",false);
        static void Check(EditorGeometry normal,EditorGeometry actual,string label,bool focused=true)
        {
            Require(normal.Outer==actual.Outer&&normal.Border==actual.Border&&normal.Presenter==actual.Presenter&&
                normal.BorderThickness==new Thickness(1)&&actual.BorderThickness==normal.BorderThickness&&normal.Padding==actual.Padding,
                label+" focus changed editor geometry: "+normal+" / "+actual);
            Require(Equals(normal.Background,actual.Background),label+" focus must not change the background.");
            Require(Equals(normal.BorderBrush,actual.BorderBrush)!=focused,label+" focus must change only the border color and restore it on blur.");
        }
    }
    private static double UiFontSize(MainWindowViewModel model)=>model.UiFontSize??12;
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
