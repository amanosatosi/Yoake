using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Services;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    private string OpenMultilineFormatting()
    {
        var path=Path.Combine(_root,"multiline-format.ass");
        File.WriteAllText(path,"[Script Info]\n[Events]\nFormat: "+string.Join(',',AssDocument.EventFormat)+",Unknown\n"+
            "Comment: 2,0:00:00.00,0:00:00.00,Default,,0,0,0,mangetsu-style,{\\future(keep)}opaque,style-extra\n"+
            "Comment: 3,0:00:00.00,0:00:00.00,Default,HARU,0,0,0,mangetsu-colorcoding,{\\future(color)}actor,actor-extra\n"+
            "Dialogue: 4,0:00:07.00,0:00:08.00,Default,HARU,1,2,3,{uketsugu},{\\future(inherit)}base,inherit-extra\n"+
            "Dialogue: 5,0:00:01.00,0:00:03.00,Default,HARU,4,5,6,,{\\future(a)}日本語 é မြန်မာ,first-extra\n"+
            "Dialogue: 6,0:00:03.00,0:00:05.00,Default,B,7,8,9,,{\\b1\\future(b)}abc 日本,second-extra\n"+
            "Dialogue: 7,0:00:05.00,0:00:06.00,Default,C,0,0,0,,x,short-extra\n"+
            "Dialogue: 8,0:00:06.00,0:00:07.00,Default,D,0,0,0,,ordinary {uketsugu} text,text-extra\n");
        Assert.True(_model.OpenSubtitle(path));_model.SelectedEvent=_model.Events[3];_model.SetSelectedEvents(_model.Events);
        var start=_model.Draft!.Text.IndexOf("日本語",StringComparison.Ordinal);
        _model.TextSelectionStart=start;_model.TextSelectionEnd=_model.TextCursor=start+3;
        return _model.ActiveEditor!.Document.Serialize();
    }
    [Theory]
    [InlineData(CommandIds.FormatBold)]
    [InlineData(CommandIds.FormatItalic)]
    [InlineData(CommandIds.FormatUnderline)]
    [InlineData(CommandIds.FormatStrikeout)]
    [InlineData(CommandIds.FormatReset)]
    [InlineData(CommandIds.FormatFont)]
    [InlineData(CommandIds.FormatPrimary)]
    [InlineData(CommandIds.FormatSecondary)]
    [InlineData(CommandIds.FormatOutline)]
    [InlineData(CommandIds.FormatShadow)]
    public async Task FormattingAppliesToAllSelectedOrdinaryLinesInOneUndo(string command)
    {
        var before=OpenMultilineFormatting();var rows=_model.Events.ToArray();var originals=rows.Select(row=>row.Text).ToArray();
        var fields=rows.Select(row=>row.FieldNames.Select(row.Get).ToArray()).ToArray();
        _dialogs.FontDialog=_=>Task.FromResult<FontChoice?>(new("Subtitle 日本語","31"));
        _dialogs.ColorDialog=_=>Task.FromResult<AssColor?>(new(17,34,51,68));
        await Command(command);
        for(var i=0;i<3;i++)Assert.Equal(fields[i],rows[i].FieldNames.Select(rows[i].Get));
        for(var i=3;i<rows.Length;i++)
        {
            Assert.NotEqual(originals[i],rows[i].Text);
            for(var field=0;field<fields[i].Length;field++)if(rows[i].FieldNames[field]!="Text")Assert.Equal(fields[i][field],rows[i].Get(rows[i].FieldNames[field]));
        }
        Assert.Contains(@"\future(a)",rows[3].Text);Assert.Contains(@"\future(b)",rows[4].Text);Assert.Equal(AssTextOperations.PlainTextForSorting(originals[6]),AssTextOperations.PlainTextForSorting(rows[6].Text));
        Assert.Equal("日本語",_model.Draft!.Text[_model.TextSelectionStart.._model.TextSelectionEnd]);
        var after=_model.ActiveEditor!.Document.Serialize();await Command(CommandIds.EditUndo);
        Assert.Equal(before,_model.ActiveEditor.Document.Serialize());await Command(CommandIds.EditRedo);Assert.Equal(after,_model.ActiveEditor.Document.Serialize());
        Assert.Equal(rows,_model.SelectedEvents);
    }
    [Fact] public async Task DialogSelectionChangeCannotRedirectBulkFormatting()
    {
        var before=OpenMultilineFormatting();var undo=_model.ActiveEditor!.Undo.NextUndoName;
        var answer=new TaskCompletionSource<FontChoice?>();_dialogs.FontDialog=_=>answer.Task;
        var task=_model.Registry.InvokeAsync(CommandIds.FormatFont,new(_model.ActiveDocumentId));
        _model.SetSelectedEvents([_model.SelectedEvent!]);answer.SetResult(new("Other font","40"));
        Assert.True(await task);Assert.Equal(before,_model.ActiveEditor.Document.Serialize());Assert.Equal(undo,_model.ActiveEditor.Undo.NextUndoName);
    }
}
