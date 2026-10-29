using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VisualLine(AssEvent Line,string Text,AssPoint DefaultPosition,AssPoint Position,AssPoint Origin,AssTransform Transform,double RelativeTime,double Duration,bool Active);

public sealed partial class MainWindowViewModel
{
    private VisualLine[] _visualBaseline=[];
    public bool HasGesture=>_gesture is not null||_audioTimingGesture is not null||_karaokeGesture is not null;
    public event EventHandler? GestureEnded;
    public static readonly string[] VisualToolNames=["Crosshair","Position","RotateZ","RotateXY","Scale","Clip","VectorClip","Distort"];
    public static readonly string[] VectorModes=["Select","Line","Bicubic","Convert","Insert","Remove","Freehand","Smooth"];
    private string _vectorMode="Select";
    public string VectorMode {get=>VisualState?.VectorMode??_vectorMode;private set{if(VisualState is {} state)state.VectorMode=value;SetField(ref _vectorMode,value);OnPropertyChanged();OnPropertyChanged(nameof(VisualHint));foreach(var mode in VectorModes)OnPropertyChanged("IsVector"+mode);}}
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
    public IReadOnlyList<VisualLine> VisibleVisualLines(bool includeUnselected = false)
    {
        if(ActiveEditor is null)return [];
        var time=(FrameTimes.Count>0?FrameTimes[CurrentFrame]:CurrentTimeSeconds)*1000;var size=ScriptSize;
        // Use the same active-line timing values as the subtitle compositor,
        // including pending markers during a visual gesture. The audio primary
        // range intentionally falls back to stored timing while such a gesture
        // owns input, so it cannot be used as a second preview timing model here.
        var preview=AudioPreviewValues();
        (long Start,long End) Range(AssEvent line)
        {
            if(ReferenceEquals(line,SelectedEvent)&&preview is not null&&
                AssTime.TryParse(preview.GetValueOrDefault("Start"),out var start)&&
                AssTime.TryParse(preview.GetValueOrDefault("End"),out var end)&&end>=start)return(start,end);
            return(line.StartMilliseconds??0,line.EndMilliseconds??0);
        }
        var visible=(includeUnselected?Events:Selection()).Select(line=>(Line:line,Timing:Range(line)))
            .Where(entry=>!entry.Line.IsComment&&time>=entry.Timing.Start&&time<entry.Timing.End).ToArray();
        var primary=visible.FirstOrDefault(entry=>ReferenceEquals(entry.Line,SelectedEvent)).Line??visible.FirstOrDefault().Line;
        return visible.Select(entry=>
        {
            var line=entry.Line;var active=ReferenceEquals(line,SelectedEvent);var text=active?VisualText:line.Text;
            var style=ActiveEditor.Document.Styles.FirstOrDefault(s=>s.Name==(active?EditorDraft.Style:line.Style));
            var fallback=AssVisualTags.DefaultPosition(line,style,size.Width,size.Height,text);
            var relative=time-entry.Timing.Start;var duration=entry.Timing.End-entry.Timing.Start;
            var position=AssVisualTags.PositionAtTime(text,(long)relative,duration)??fallback;
            return new VisualLine(line,text,fallback,position,AssVisualTags.Origin(text)??position,AssVisualTags.Transform(text,style),relative,duration,ReferenceEquals(line,primary));
        }).ToArray();
    }
    public bool SelectVisualLine(AssEvent line)
    {
        var suppress=_suppressSelectionSeek;_suppressSelectionSeek=true;
        try { SelectedEvent=line; return ReferenceEquals(SelectedEvent,line); }
        finally { _suppressSelectionSeek=suppress; }
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
        NotifyVisualToolState();
    }
    private void NotifyVisualToolState()
    {
        OnPropertyChanged(nameof(ActiveVisualTool));OnPropertyChanged(nameof(VectorMode));
        foreach(var mode in VectorModes)OnPropertyChanged("IsVector"+mode);
        foreach(var property in new[]{nameof(IsCrosshairTool),nameof(IsPositionTool),nameof(IsRotateZTool),nameof(IsRotateXYTool),nameof(IsScaleTool),nameof(IsClipTool),nameof(IsVectorClipTool),nameof(IsDistortTool),nameof(IsClipFamily),nameof(HasVisualOptions),nameof(VisualHint)})OnPropertyChanged(property);
    }
    private DocumentState? VisualState=>_activeId is {} id&&_documents.TryGetValue(id,out var state)?state:null;
    private void RegisterVisualToolCommands()
    {
        bool Owns(CommandContext context)=>_activeId is not null&&(context.DocumentId is null||context.DocumentId==_activeId);
        void Register(string id,string label,Action select,Func<bool> check)
        {
            _registry.Register(new(new(id,label,label,"Video visual tools"),(_,token)=>
            {token.ThrowIfCancellationRequested();select();return ValueTask.CompletedTask;},
                c=>Owns(c)&&_media?.HasVideo==true&&!HasEditOperation,isChecked:c=>Owns(c)&&check()));
            Actions[id]=new RegistryCommand(_registry,id,CurrentContext,hotkeys:()=>HotkeyProfile);
        }
        foreach(var name in VisualToolNames)
        {
            var tool=name;var id="video/tool/"+(tool=="Position"?"position":tool.ToLowerInvariant());
            Register(id,tool+" tool",()=>SelectVisualTool(tool),()=>ActiveVisualTool==tool);
        }
        foreach(var name in VectorModes)
        {
            var mode=name;
            Register("video/vector/"+mode.ToLowerInvariant(),"Vector clip: "+mode,
                ()=>{CancelGesture();SelectVisualTool("VectorClip");VectorMode=mode;},()=>IsVectorClipTool&&VectorMode==mode);
        }
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
