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
    public bool IsClipFamily=>IsClipTool||IsVectorClipTool;
    public string VisualHint=>ActiveVisualTool switch
    {
        "Crosshair"=>"Double-click: shift visible selection ﾂｷ coordinates in script pixels",
        "Position"=>"Square: start ﾂｷ circle: end ﾂｷ triangle: origin ﾂｷ Shift: one axis ﾂｷ endpoint time: current frame",
        "RotateZ"=>"Drag ring: rotate ﾂｷ Ctrl: 30ﾂｰ snap ﾂｷ triangle: origin",
        "RotateXY"=>"Drag: X/Y rotation ﾂｷ Shift: one axis ﾂｷ Ctrl: 30ﾂｰ snap ﾂｷ triangle: origin",
        "Scale"=>"Drag: scale ﾂｷ Shift: one axis ﾂｷ Alt: aspect ﾂｷ Ctrl: 25% snap",
        "Clip"=>"Drag: new rectangle ﾂｷ corners: resize ﾂｷ Shift+drag / inside: move clip",
        "VectorClip"=>VectorMode+" ﾂｷ Ctrl: toggle points ﾂｷ empty drag: box select ﾂｷ Esc: cancel",
        "Distort"=>"Drag corners: bilinear distortion ﾂｷ center: translate ﾂｷ eight normalized values",
        _=>""
    };
    public IReadOnlyList<VisualLine> VisibleVisualLines()
    {
        if(ActiveEditor is null)return [];
        var time=(FrameTimes.Count>0?FrameTimes[CurrentFrame]:CurrentTimeSeconds)*1000;var size=ScriptSize;
        return Selection().Where(l=>!l.IsComment&&time>=(l.StartMilliseconds??0)&&time<(l.EndMilliseconds??0)).Select(line=>
        {
            var active=ReferenceEquals(line,SelectedEvent);var text=active?VisualText:line.Text;
            var style=ActiveEditor.Document.Styles.FirstOrDefault(s=>s.Name==(active?EditorDraft.Style:line.Style));
            var fallback=AssVisualTags.DefaultPosition(line,style,size.Width,size.Height,text);
            var relative=time-(line.StartMilliseconds??0);var duration=(line.EndMilliseconds??0)-(line.StartMilliseconds??0);
            var position=AssVisualTags.PositionAtTime(text,(long)relative,duration)??fallback;
            return new VisualLine(line,text,fallback,position,AssVisualTags.Origin(text)??position,AssVisualTags.Transform(text,style),relative,duration,active);
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
        foreach(var property in new[]{nameof(IsCrosshairTool),nameof(IsPositionTool),nameof(IsRotateZTool),nameof(IsRotateXYTool),nameof(IsScaleTool),nameof(IsClipTool),nameof(IsVectorClipTool),nameof(IsDistortTool),nameof(IsClipFamily),nameof(VisualHint)})OnPropertyChanged(property);
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
