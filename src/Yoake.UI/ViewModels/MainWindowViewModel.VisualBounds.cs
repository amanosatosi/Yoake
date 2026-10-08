using Yoake.Core.Subtitles;
using Yoake.Native;

namespace Yoake.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly SubtitleGeometryProvider _geometryProvider=new();
    private readonly Dictionary<AssEvent,(string Key,AssBox Bounds)> _visualBounds=[];
    private CancellationTokenSource? _boundsCancellation;
    private string? _boundsRequest;
    private string? _boundsSource;
    public AssBox? VisualBounds(VisualLine line)=>_visualBounds.TryGetValue(line.Line,out var value)?value.Bounds:null;
    public void RequestVisualBounds()
    {
        if(_disposed||!IsDistortTool||HasGesture||ActiveEditor is not {} editor||VideoFrame is null)return;
        var lines=VisibleVisualLines();var size=ScriptSize;var width=(int)Math.Clamp(size.Width,1,4096);var height=(int)Math.Clamp(size.Height,1,2160);
        var source=editor.Document.Revision+":"+string.Join(',',lines.Select(l=>l.Line.Number));if(source==_boundsSource)return;_boundsSource=source;
        var snapshots=lines.Select(l=>(l.Line,Track:VisualMeasurement.Track(editor.Document,l.Line,l.Text,size.Width/2,size.Height/2))).ToArray();
        var key=string.Join('\n',snapshots.Select(s=>s.Track));if(key==_boundsRequest)return;_boundsRequest=key;
        _boundsCancellation?.Cancel();_boundsCancellation?.Dispose();var cancellation=new CancellationTokenSource();_boundsCancellation=cancellation;
        _=MeasureVisualBoundsAsync(snapshots,size,width,height,cancellation.Token);
    }
    private void CancelVisualBounds(){_boundsCancellation?.Cancel();_boundsRequest=null;_boundsSource=null;_visualBounds.Clear();}
    private async Task MeasureVisualBoundsAsync((AssEvent Line,string Track)[] snapshots,(double Width,double Height) size,int width,int height,CancellationToken token)
    {
        try
        {
            var result=await Task.Run(()=>snapshots.Select(s=>(s.Line,s.Track,Box:_geometryProvider.Measure(s.Track,width,height,token))).ToArray(),token);
            if(token.IsCancellationRequested||_disposed)return;
            _visualBounds.Clear();foreach(var item in result)if(item.Box is {} box)_visualBounds[item.Line]=(item.Track,new(box.Left*size.Width/width-size.Width/2,box.Top*size.Height/height-size.Height/2,box.Width*size.Width/width,box.Height*size.Height/height));
            OnPropertyChanged(nameof(VisualBounds));
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(!token.IsCancellationRequested)Registry.ReportFailure("video/visual/bounds",e);}
    }
}
