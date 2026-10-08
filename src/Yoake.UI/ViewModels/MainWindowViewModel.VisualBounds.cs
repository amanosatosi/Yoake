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
    private bool _visualBoundsPending;
    public bool VisualBoundsPending {get=>_visualBoundsPending;private set=>SetField(ref _visualBoundsPending,value);}
    public AssBox? VisualBounds(VisualLine line)=>_visualBounds.TryGetValue(line.Line,out var value)?value.Bounds:null;
    public void RequestVisualBounds()
    {
        if(_disposed||!IsDistortTool||HasGesture||ActiveEditor is not {} editor||VideoFrame is null)return;
        var lines=VisibleVisualLines();var size=ScriptSize;var width=(int)Math.Clamp(size.Width,1,4096);var height=(int)Math.Clamp(size.Height,1,2160);
        var source=PreviewRevision+":"+size+":"+VideoFrame.PixelSize+":"+string.Join(',',lines.Select(l=>l.Line.Number));if(source==_boundsSource)return;_boundsSource=source;
        var header=VisualMeasurement.Header(editor.Document);
        var snapshots=lines.Select(l=>(l.Line,Snapshot:VisualMeasurement.Snapshot(l.Line,l.Text))).ToArray();
        var key=header+string.Join('\n',snapshots.Select(s=>s.Snapshot));if(key==_boundsRequest)return;_boundsRequest=key;
        _boundsCancellation?.Cancel();_boundsCancellation?.Dispose();var cancellation=new CancellationTokenSource();_boundsCancellation=cancellation;
        VisualBoundsPending=true;_=MeasureVisualBoundsAsync(header,snapshots,size,width,height,cancellation.Token);
    }
    private void CancelVisualBounds(){_boundsCancellation?.Cancel();_visualBounds.Clear();VisualBoundsPending=false;_boundsRequest=null;_boundsSource=null;}
    private async Task MeasureVisualBoundsAsync(string header,(AssEvent Line,string Snapshot)[] snapshots,(double Width,double Height) size,int width,int height,CancellationToken token)
    {
        try
        {
            var result=await Task.Run(()=>snapshots.Select(s=>
            {
                token.ThrowIfCancellationRequested();var track=VisualMeasurement.Track(header,s.Snapshot,size.Width/2,size.Height/2);
                return (s.Line,Track:track,Box:_geometryProvider.Measure(track,width,height,token));
            }).ToArray(),token);
            if(token.IsCancellationRequested||_disposed)return;
            _visualBounds.Clear();foreach(var item in result)if(item.Box is {} box)_visualBounds[item.Line]=(item.Track,new(box.Left*size.Width/width-size.Width/2,box.Top*size.Height/height-size.Height/2,box.Width*size.Width/width,box.Height*size.Height/height));
            OnPropertyChanged(nameof(VisualBounds));
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(!token.IsCancellationRequested)Registry.ReportFailure("video/visual/bounds",e);}
        finally{if(!token.IsCancellationRequested&&!_disposed)VisualBoundsPending=false;}
    }
}
