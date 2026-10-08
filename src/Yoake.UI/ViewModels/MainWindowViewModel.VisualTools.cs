using Yoake.Core.Commands;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

public sealed record VisualLine(AssEvent Line,string Text,AssPoint DefaultPosition,AssPoint Position,AssPoint Origin,AssTransform Transform,double RelativeTime,double Duration,bool Active);

public sealed partial class MainWindowViewModel
{
    private VisualLine[] _visualBaseline=[];
    public bool HasGesture=>_gesture is not null;
    public event EventHandler? GestureEnded;
    public static readonly string[] VisualToolNames=["Crosshair","Position","RotateZ","RotateXY","Scale","Clip","VectorClip","Distort"];
    public static readonly string[] VectorModes=["Select","Line","Bicubic","Convert","Insert","Remove","Freehand","Smooth"];
    private string _vectorMode="Select";
    public string VectorMode {get=>_vectorMode;private set{SetField(ref _vectorMode,value);OnPropertyChanged(nameof(VisualHint));foreach(var mode in VectorModes)OnPropertyChanged("IsVector"+mode);}}
    public bool IsCrosshairTool=>ActiveVisualTool=="Crosshair";
    public bool IsRotateZTool=>ActiveVisualTool=="RotateZ";
    public bool IsRotateXYTool=>ActiveVisualTool=="RotateXY";
    public bool IsScaleTool=>ActiveVisualTool=="Scale";
    public bool IsVectorClipTool=>ActiveVisualTool=="VectorClip";
    public bool IsDistortTool=>ActiveVisualTool=="Distort";
    public bool IsVectorSelect=>VectorMode=="Select";
    public bool IsVectorLine=>VectorMode=="Line";
    public bool IsVectorBicubic=>VectorMode=="Bicubic";
    public bool IsVectorConvert=>VectorMode=="Convert";
    public bool IsVectorInsert=>VectorMode=="Insert";
    public bool IsVectorRemove=>VectorMode=="Remove";
    public bool IsVectorFreehand=>VectorMode=="Freehand";
    public bool IsVectorSmooth=>VectorMode=="Smooth";
    public bool HasVisualOptions=>IsPositionTool||IsClipFamily;
    public bool IsClipFamily=>IsClipTool||IsVectorClipTool;
    public string VisualHint=>ActiveVisualTool switch
    {
        "Crosshair"=>"Double-click: shift visible selection · coordinates in script pixels",
        "Position"=>"Square: start · circle: end · triangle: origin · Shift: one axis · endpoint time: current frame",
        "RotateZ"=>"Drag ring: rotate · Ctrl: 30° snap · triangle: origin",
        "RotateXY"=>"Drag: X/Y rotation · Shift: one axis · Ctrl: 30° snap · triangle: origin",
        "Scale"=>"Drag: scale · Shift: one axis · Alt: aspect · Ctrl: 25% snap",
        "Clip"=>"Drag: new rectangle · corners: resize · Shift+drag / inside: move clip",
        "VectorClip"=>VectorMode+" · Ctrl: toggle points · empty drag: box select · Esc: cancel",
        "Distort"=>"Drag corners: bilinear distortion · center: translate · eight normalized values",
        _=>""
    };
    // Mangetsu's projection distance is 20000/64 layout pixels. The provider
    // configures storage size to the decoded frame; explicit LayoutRes wins.
    public double VisualLayoutScale
    {
        get
        {
            var doc=ActiveEditor?.Document;
            var explicitLayout=int.TryParse(doc?.GetScriptInfo("LayoutResX"),out var x)&&x>0&&int.TryParse(doc?.GetScriptInfo("LayoutResY"),out var y)&&y>0;
            var height=explicitLayout?int.Parse(doc!.GetScriptInfo("LayoutResY")!,System.Globalization.CultureInfo.InvariantCulture):VideoFrame?.PixelSize.Height??(int)ScriptSize.Height;
            return ScriptSize.Height/Math.Max(1,height);
        }
    }
    public IReadOnlyList<VisualLine> VisibleVisualLines()
    {
        if(ActiveEditor is null)return [];
        var time=(FrameTimes.Count>0?FrameTimes[CurrentFrame]:CurrentTimeSeconds)*1000;var size=ScriptSize;
        var visible=Selection().Where(l=>!l.IsComment&&time>=(l.StartMilliseconds??0)&&time<(l.EndMilliseconds??0)).ToArray();
        var primary=visible.FirstOrDefault(l=>ReferenceEquals(l,SelectedEvent))??visible.FirstOrDefault();
        return visible.Select(line=>
        {
            var active=ReferenceEquals(line,SelectedEvent);var text=active?VisualText:line.Text;
            var style=ActiveEditor.Document.Styles.FirstOrDefault(s=>s.Name==(active?EditorDraft.Style:line.Style));
            var fallback=AssVisualTags.DefaultPosition(line,style,size.Width,size.Height,text);
            var relative=time-(line.StartMilliseconds??0);var duration=(line.EndMilliseconds??0)-(line.StartMilliseconds??0);
            var position=AssVisualTags.PositionAtTime(text,(long)relative,duration)??fallback;
            return new VisualLine(line,text,fallback,position,AssVisualTags.Origin(text)??position,AssVisualTags.Transform(text,style),relative,duration,ReferenceEquals(line,primary));
        }).ToArray();
    }
    public void UpdateVisualGesture(Func<VisualLine,string> edit)=>InvokeGesture("video/visual/update",edit);
    private void UpdateVisualGestureCore(Func<VisualLine,string> edit)
    {
        if(_gesture is null||_gestureEditor is null)return;
        // Compute the complete batch before mutating. Any parse failure rolls
        // back the transaction, including edits to earlier selected lines.
        var changes=_visualBaseline.Select(line=>(line.Line,Text:edit(line))).ToArray();
        foreach(var change in changes)_gestureEditor.SetField(change.Line,"Text",change.Text,"Visual typesetting");
        GestureChanged();
    }
    private void SelectVisualTool(string tool)
    {
        CancelGesture();ActiveVisualTool=tool;
        foreach(var property in new[]{nameof(IsCrosshairTool),nameof(IsPositionTool),nameof(IsRotateZTool),nameof(IsRotateXYTool),nameof(IsScaleTool),nameof(IsClipTool),nameof(IsVectorClipTool),nameof(IsDistortTool),nameof(IsClipFamily),nameof(HasVisualOptions),nameof(VisualHint)})OnPropertyChanged(property);
    }
    private void ToggleVisualMove()
    {
        if(!BeginGesture("Toggle position / movement"))return;
        try
        {
            UpdateVisualGesture(line=>
            {
                var frame=FrameTimes.Count>0?CurrentFrame:0;var a=FrameTimes.Count>0?FrameTimes[frame]*1000-(line.Line.StartMilliseconds??0):line.RelativeTime;
                var b=FrameTimes.Count>frame+1?FrameTimes[frame+1]*1000-(line.Line.StartMilliseconds??0):a+40;
                return AssVisualTags.ToggleMove(line.Text,line.DefaultPosition,Math.Max(0,a),Math.Max(0,b));
            });EndGesture();
        }
        catch{CancelGesture();throw;}
    }
    private void InvertVisualClip()
    {
        if(!BeginGesture("Invert subtitle clips"))return;
        try{UpdateVisualGesture(line=>AssVisualTags.InvertClip(line.Text));EndGesture();}catch{CancelGesture();throw;}
    }
}
