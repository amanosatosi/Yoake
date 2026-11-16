using Avalonia.Controls;

namespace Yoake.UI;

public partial class MainWindow
{
    private void UpdateOriginalTextLayout()
    {
        var visible=_model?.ShowOriginal==true;
        var rows=SubtitleTextEditors.RowDefinitions;
        rows[0].Height=visible?new GridLength(1,GridUnitType.Star):new GridLength(0);
        rows[0].MinHeight=visible?OriginalText.MinHeight:0;
        rows[1].Height=new GridLength(1,GridUnitType.Star);rows[1].MinHeight=SubtitleText.MinHeight;
        rows[2].Height=visible?GridLength.Auto:new GridLength(0);
        SubtitleTextEditors.RowSpacing=visible?3:0;
        UpdateEditorMinimumHeight();
    }
    private void UpdateEditorMinimumHeight()
    {
        if(_model is null||EventEditorRegion.Bounds.Width<=0)return;
        var chrome=EventEditorRegion.RowDefinitions.Take(3).Sum(row=>row.ActualHeight)+EventEditorRegion.RowSpacing*3+
            EventEditorRegion.Margin.Top+EventEditorRegion.Margin.Bottom;
        var input=SubtitleText.MinHeight+(_model.ShowOriginal?OriginalText.MinHeight+SubtitleTextEditors.RowDefinitions[2].ActualHeight+6:0);
        var minimum=Math.Max(280,Math.Ceiling(chrome+input));
        var editor=TemporalTextColumn.RowDefinitions[2];
        if(Math.Abs(editor.MinHeight-minimum)>.5)editor.MinHeight=minimum;
        if(UpperWorkspace.Parent is Grid workspace)
        {
            var audio=_model.ShowAudio?TemporalTextColumn.RowDefinitions[0].MinHeight+4:0;
            var upperMinimum=Math.Max(_compactSubtitleLayout?280:444,minimum+audio);
            if(Math.Abs(workspace.RowDefinitions[2].MinHeight-upperMinimum)>.5)workspace.RowDefinitions[2].MinHeight=upperMinimum;
        }
        if(_model.ShowOriginal&&SubtitleTextEditors.Bounds.Height>0)
        {
            var scale=RenderScaling;
            var available=SubtitleTextEditors.Bounds.Height-SubtitleTextEditors.RowDefinitions[2].ActualHeight-6;
            var half=Math.Max(SubtitleText.MinHeight,Math.Floor(available*scale/2)/scale);
            foreach(var row in SubtitleTextEditors.RowDefinitions.Take(2))
                if(!row.Height.IsAbsolute||Math.Abs(row.Height.Value-half)>.01)row.Height=new GridLength(half);
        }
        ClampAudioBounds();
    }
}
