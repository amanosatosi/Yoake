using Avalonia;
using Avalonia.Input;
using Xunit;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.VisualTools;

namespace Yoake.UI.Tests;

public sealed partial class EditorWorkflowTests
{
    [Fact] public async Task PositionToolSelectsAndMovesAnotherVisibleLineWithoutSeeking()
    {
        var rows=await TimingRows();var editor=_model.ActiveEditor!;
        editor.ToggleComment([rows[1]]);editor.SetTiming(rows[1],300,1400);
        editor.SetField(rows[0],"Text",@"{\pos(40,40)}first","Fixture");
        editor.SetField(rows[1],"Text",@"{\pos(160,100)}second","Fixture");
        _model.SelectedEvent=rows[0];_model.CurrentTimeSeconds=.5;
        var context=new VisualToolContext(_model,new Rect(0,0,640,480),_model.VisibleVisualLines(true));
        Assert.Equal(2,context.Lines.Count);var target=context.Lines.Single(l=>ReferenceEquals(l.Line,rows[1]));
        var screen=context.Screen(target.Position);var tool=new PositionTool();var pointer=new VisualPointer(screen,target.Position,KeyModifiers.None);
        Assert.True(tool.Press(context,pointer));Assert.Same(rows[1],_model.SelectedEvent);Assert.Equal(.5,_model.CurrentTimeSeconds,6);
        var moved=new AssPoint(target.Position.X+20,target.Position.Y+10);
        tool.Move(new(_model,context.Video,_model.VisibleVisualLines(true)),new(context.Screen(moved),moved,KeyModifiers.None));
        _model.EndGesture();Assert.Contains(@"\pos(180,110)",rows[1].Text);
        await Command(CommandIds.EditUndo);Assert.Contains(@"\pos(160,100)",rows[1].Text);
    }
    [Fact] public async Task VisualToolsUseTheCompositorsPendingTimingBeforeDuringAndAfterAGesture()
    {
        var rows=await TimingRows();var editor=_model.ActiveEditor!;var source=editor.Document.Serialize();
        await Command("time/lead/both");_model.CurrentTimeSeconds=.25;
        var line=Assert.Single(_model.VisibleVisualLines());Assert.Same(rows[0],line.Line);
        Assert.Equal(50d,line.RelativeTime);Assert.Equal(1550d,line.Duration);
        Assert.Contains("0:00:00.20",_model.PreviewSource());
        Assert.True(_model.BeginGesture("Position pending preview"));
        _model.UpdateVisualGesture(l=>AssVisualTags.SetPosition(l.Text,new(30,40)));
        line=Assert.Single(_model.VisibleVisualLines());Assert.Equal(50d,line.RelativeTime);Assert.Equal(1550d,line.Duration);
        Assert.True(_model.HasPendingAudioTiming);Assert.Equal(300,rows[0].StartMilliseconds);
        _model.EndGesture();Assert.Single(_model.VisibleVisualLines());await Command(CommandIds.EditUndo);
        Assert.Equal(source,editor.Document.Serialize());Assert.True(_model.HasPendingAudioTiming);Assert.False(editor.Undo.CanUndo);
        await Command("audio/timing/revert");Assert.Empty(_model.VisibleVisualLines());
    }
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
