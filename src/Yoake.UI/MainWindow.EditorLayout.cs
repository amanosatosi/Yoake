using Avalonia.Controls;

namespace Yoake.UI;

public partial class MainWindow
{
    private const double OriginalActionStripWidth=38;
    private const double OriginalActionStripHeight=4*24+3*3;

    private void UpdateOriginalTextLayout()
    {
        var visible=_model?.ShowOriginal==true;
        var columns=SubtitleTextEditors.ColumnDefinitions;
        columns[0].Width=new GridLength(1,GridUnitType.Star);
        columns[1].Width=new GridLength(visible?OriginalActionStripWidth:0);
        columns[2].Width=visible?new GridLength(1,GridUnitType.Star):new GridLength(0);
        UpdateEditorMinimumHeight();
    }
    private void UpdateEditorMinimumHeight()
    {
        if(_model is null||EventEditorRegion.Bounds.Width<=0)return;
        var chrome=EventEditorRegion.RowDefinitions.Where((row,index)=>index!=1).Sum(row=>row.ActualHeight)+EventEditorRegion.RowSpacing*2+
            EventEditorRegion.Margin.Top+EventEditorRegion.Margin.Bottom;
        // The original and edit fields share their height rather than stacking
        // two independent minimum heights and a separate action row.
        var input=SubtitleText.MinHeight;
        if(_model.ShowOriginal)input=Math.Max(input,Math.Max(OriginalText.MinHeight,OriginalActionStripHeight));
        var minimum=Math.Max(280,Math.Ceiling(chrome+input));
        var editor=TemporalTextColumn.RowDefinitions[2];
        if(Math.Abs(editor.MinHeight-minimum)>.5)editor.MinHeight=minimum;
        if(UpperWorkspace.Parent is Grid workspace)
        {
            var audio=_model.ShowAudio?TemporalTextColumn.RowDefinitions[0].MinHeight+4:0;
            var upperMinimum=Math.Max(_compactSubtitleLayout?280:444,minimum+audio);
            if(Math.Abs(workspace.RowDefinitions[2].MinHeight-upperMinimum)>.5)workspace.RowDefinitions[2].MinHeight=upperMinimum;
        }
        ClampAudioBounds();
    }
}
