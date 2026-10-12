using Avalonia;
using Avalonia.Input;
using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.VisualTools;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RectangleFromAbsentClipPreviewsFromPressAndCommitsOneLosslessUndo(bool inverse)
    {
        await Command(CommandIds.GridInsertAfter);var line=_model.SelectedEvent!;
        _model.ActiveEditor!.SetField(line,"Text",@"{\future(keep)\distort(1,2,3,4,5,6,7,8)}日本語 é","Fixture");
        var source=_model.ActiveEditor.Document.Serialize();var history=_model.ActiveEditor.Undo.NextUndoName;
        _model.InverseClip=inverse;
        var context=new VisualToolContext(_model,new Rect(10,20,768,576),_model.VisibleVisualLines());
        var tool=new RectangleClipTool();var a=new AssPoint(20,30);var b=new AssPoint(100,90);
        var press=new VisualPointer(context.Screen(a),a,KeyModifiers.None);var move=new VisualPointer(context.Screen(b),b,KeyModifiers.None);
        Assert.True(tool.Press(context,press));Assert.True(_model.HasGesture);
        var prospective=AssVisualTags.Clip(line.Text)!;Assert.True(prospective.Rectangular);Assert.Equal(inverse,prospective.Inverse);Assert.Equal(new[]{a,a},prospective.Points);
        tool.Move(context,move);Assert.Contains(inverse?@"\iclip(20,30,100,90)":@"\clip(20,30,100,90)",_model.PreviewSource());
        Assert.Equal(history,_model.ActiveEditor.Undo.NextUndoName);
        tool.Release(context,move);_model.EndGesture();Assert.False(_model.HasGesture);var committed=_model.ActiveEditor.Document.Serialize();
        Assert.Contains(@"\future(keep)\distort(1,2,3,4,5,6,7,8)",line.Text);
        await Command(CommandIds.EditUndo);Assert.Equal(source,_model.ActiveEditor.Document.Serialize());Assert.Equal(history,_model.ActiveEditor.Undo.NextUndoName);
        await Command(CommandIds.EditRedo);Assert.Equal(committed,_model.ActiveEditor.Document.Serialize());
        Assert.True(tool.Press(new(_model,context.Video,_model.VisibleVisualLines()),press));tool.Move(context,move);_model.CancelGesture();
        Assert.Equal(committed,_model.ActiveEditor.Document.Serialize());Assert.False(_model.HasGesture);
    }
}
