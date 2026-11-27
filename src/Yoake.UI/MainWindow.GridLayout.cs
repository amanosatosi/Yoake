using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Yoake.Core.Subtitles;

namespace Yoake.UI;

public partial class MainWindow
{
    private const int GridMetadataColumnCount=10;
    private double[] _gridWidthOverrides=new double[GridMetadataColumnCount];
    private bool _gridLayoutQueued;

    private void QueueGridColumnLayout()
    {
        if(_gridLayoutQueued)return;
        _gridLayoutQueued=true;
        Dispatcher.UIThread.Post(()=>
        {
            _gridLayoutQueued=false;
            if(_model is null||_resizeColumn>=0)return;
            var size=Application.Current is {} app&&app.Resources.TryGetResource("YoakeUiFontSizeSmall",null,out var value)&&value is double fontSize?fontSize:11;
            var probe=new TextBlock{FontFamily=FontFamily,FontSize=size};
            double Measure(string text)
            {
                probe.Text=text;
                probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                return probe.DesiredSize.Width;
            }
            var widths=SubtitleGridColumns.Calculate(_model.Events,Measure,_uiColumnScale,_gridWidthOverrides);
            for(var i=0;i<GridMetadataColumnCount;i++)
            {
                var width=new GridLength(widths[i]);
                if(ColumnHeader.ColumnDefinitions[i].Width!=width)ColumnHeader.ColumnDefinitions[i].Width=width;
            }
            foreach(var child in ColumnHeader.Children)
                if(Grid.GetColumn(child)<GridMetadataColumnCount)
                    child.IsVisible=widths[Grid.GetColumn(child)]>0;
            ApplyRowWidths();
        },DispatcherPriority.Background);
    }
}
