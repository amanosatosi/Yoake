using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Commands;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal static class MultilineFormattingUiVerification
{
    public static async Task VerifyAsync(MainWindow window,MainWindowViewModel model,StartupOptions options)
    {
        async Task Layout()=>await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout,DispatcherPriority.Background);
        async Task Invoke(string id)=>Require(await model.Registry.InvokeAsync(id,new(model.ActiveDocumentId)),"Bulk formatting command: "+id);
        void Press(Control control)
        {
            Require(control.Focus(),"Bulk formatting action accepts keyboard focus.");
            control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=control });
        }
        var path=Path.Combine(options.ProfileDirectory!,"bulk-formatting.ass");
        File.WriteAllText(path,"[Script Info]\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+",Unknown\n"+
            "Comment: 0,0:00:00.00,0:00:00.00,Default,,0,0,0,mangetsu-style,{\\future(style)}metadata,style-extra\n"+
            "Comment: 0,0:00:00.00,0:00:00.00,Default,HARU,0,0,0,mangetsu-colorcoding,{\\future(actor)}metadata,actor-extra\n"+
            "Dialogue: 0,0:00:07.00,0:00:08.00,Default,HARU,0,0,0,{uketsugu},{\\future(inherit)}base,inherit-extra\n"+
            "Dialogue: 0,0:00:01.00,0:00:03.00,Default,A,1,2,3,,{\\future(opaque)}日本語 é မြန်မာ,first-extra\n"+
            "Dialogue: 0,0:00:04.00,0:00:06.00,Default,B,4,5,6,,{\\b1\\future(second)}abc 日本,second-extra\n");
        Require(model.OpenSubtitle(path),"Open bulk formatting fixture.");model.SelectedEvent=model.Events[3];await Layout();
        var grid=window.FindControl<ListBox>("SubtitleRows")!;
        var row=grid.GetVisualDescendants().OfType<ListBoxItem>().First();Require(row.Focus(),"The actual subtitle row accepts keyboard focus.");
        row.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent,Key=Key.A,KeyModifiers=KeyModifiers.Control,Source=row });await Layout();
        Require(model.SelectedEvents.Count==5,"Grid Ctrl+A selects ordinary and protected records.");
        // Focusing a grid row can change the active item; restore its own editor
        // without collapsing the real multi-selection.
        model.SelectedEvent=model.Events[3];await Layout();
        var input=window.FindControl<AssTextBox>("SubtitleText")!;Require(input.Focus(),"Actual editor owns the formatted range.");
        var start=model.Draft!.Text.IndexOf("日本語",StringComparison.Ordinal);
        input.CaretIndex=input.DisplayIndex(start+3);input.SelectionStart=input.DisplayIndex(start);input.SelectionEnd=input.DisplayIndex(start+3);
        var before=model.ActiveEditor!.Document.Serialize();var protectedRows=model.Events.Take(3).Select(e=>e.FieldNames.Select(e.Get).ToArray()).ToArray();
        var nonTextFields=model.Events.Select(e=>e.FieldNames.Where(f=>!f.Equals("Text",StringComparison.OrdinalIgnoreCase)).Select(e.Get).ToArray()).ToArray();
        Require(model.Events.Select(e=>e.Get("Unknown")).SequenceEqual(new[] { "style-extra","actor-extra","inherit-extra","first-extra","second-extra" }),"Fixture must load all five unknown columns.");
        void VerifyNonTextFields()
        {
            Require(model.Events.Count==nonTextFields.Length,"Formatting must retain all records.");
            for(var i=0;i<nonTextFields.Length;i++)
                Require(model.Events[i].FieldNames.Where(f=>!f.Equals("Text",StringComparison.OrdinalIgnoreCase)).Select(model.Events[i].Get).SequenceEqual(nonTextFields[i]),"Formatting changed non-text fields in row "+i);
        }
        var fontTask=model.Registry.InvokeAsync(CommandIds.FormatFont,new(model.ActiveDocumentId));await Layout();
        var font=window.OwnedWindows.Single(w=>w.Name=="SubtitleFontDialog");
        font.FindControl<FontPicker>("SubtitleFontPicker")!.FontName="Subtitle 日本語";font.FindControl<NumericUpDown>("SubtitleFontSize")!.Value=31;
        Press(font.FindControl<Button>("ApplySubtitleFont")!);Require(await fontTask,"Actual font dialog applies across the selected lines.");await Layout();
        Require(model.Events.Skip(3).All(line=>line.Text.Contains(@"\fnSubtitle 日本語",StringComparison.Ordinal)&&line.Text.Contains(@"\fs31",StringComparison.Ordinal)),"Every ordinary selected line receives the font despite different override lengths.");
        for(var i=0;i<3;i++)Require(model.Events[i].FieldNames.Select(model.Events[i].Get).SequenceEqual(protectedRows[i]),"Bulk formatting preserves exact renderer metadata and Effect inheritance.");
        VerifyNonTextFields();
        await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==before,"One undo restores the complete font batch.");
        var colorTask=model.Registry.InvokeAsync(CommandIds.FormatPrimary,new(model.ActiveDocumentId));await Layout();
        var color=window.OwnedWindows.OfType<AssColorDialog>().Single();
        color.GetVisualDescendants().OfType<TextBox>().Single(box=>box.Name=="AssHex").Text="&H44332211";await Layout();
        Require(color.SelectedColor==new AssColor(17,34,51,68),"The actual exact-color input includes ASS alpha.");
        Press(color.GetVisualDescendants().OfType<Button>().Single(button=>button.Command is RegistryCommand { CommandId:"color/accept" }));
        Require(await colorTask,"Actual color dialog applies the batch.");
        Require(model.Events.Skip(3).All(line=>line.Text.Contains(@"\1c&H332211&",StringComparison.Ordinal)&&line.Text.Contains(@"\1a&H44&",StringComparison.Ordinal)),"Every ordinary selected line receives exact RGB and alpha.");
        for(var i=0;i<3;i++)Require(model.Events[i].FieldNames.Select(model.Events[i].Get).SequenceEqual(protectedRows[i]),"Color application preserves metadata order and properties.");
        VerifyNonTextFields();
        var after=model.ActiveEditor.Document.Serialize();await Invoke(CommandIds.EditUndo);Require(model.ActiveEditor.Document.Serialize()==before,"One undo restores the complete color batch.");
        await Invoke(CommandIds.EditRedo);Require(model.ActiveEditor.Document.Serialize()==after,"One redo restores every selected line.");
        await Layout();
        var verification=GridSaveVerification.Capture(window,model,after);
        await Invoke(CommandIds.SubtitleSave);
        verification.VerifySaved(model,path,"Bulk formatting save preserves all unknown columns and metadata.");
        VerifyNonTextFields();
        Console.WriteLine("PASS: real grid multi-selection, native font/color dialogs, mapped Unicode selections, all selected ordinary lines, protected metadata/inheritance, exact alpha and one-step batch undo/redo/save.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
