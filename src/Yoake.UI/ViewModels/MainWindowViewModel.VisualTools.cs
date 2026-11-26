using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI.ViewModels;

public sealed record VisualLine(AssEvent Line,string Text,AssPoint DefaultPosition,AssPoint Position,AssPoint Origin,AssTransform Transform,double RelativeTime,double Duration,bool Active,int Alignment=2,AssStyle? Style=null);

public sealed partial class MainWindowViewModel
{
    private VisualLine[] _visualBaseline=[];
    public bool HasGesture=>_gesture is not null||_audioTimingGesture is not null||_karaokeGesture is not null;
    public event EventHandler? GestureEnded;
    public static readonly string[] VisualToolNames=["Crosshair","Position","RotateZ","RotateXY","Scale","Clip","VectorClip","Distort","Drawing","Shear","Shift","Tags"];
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
    public bool IsDrawingTool=>ActiveVisualTool=="Drawing";
    public bool IsDrawingGesture=>IsDrawingTool&&_gesture is not null;
    public bool IsTagsTool=>ActiveVisualTool=="Tags";
    public IReadOnlyList<string> PositionModes {get;}=["Endpoints","Two frames","Horizontal","Vertical","Origin","Rectangle"];
    public string PositionMode {get=>VisualState?.PositionMode??"Endpoints";set=>SetVisualOption(value,PositionModes,nameof(PositionMode),(state,v)=>state.PositionMode=v);}
    public IReadOnlyList<string> ScaleModes {get;}=["Drag","Width","Height","Proportional","Fit rectangle"];
    public IReadOnlyList<string> RotateZModes {get;}=["Ring","Guide line"];
    public IReadOnlyList<string> ShiftModes {get;}=["All","Position","Move start","Move end","Origin","Clip"];
    public bool IsShiftTool=>ActiveVisualTool=="Shift";
    public string ScaleMode {get=>VisualState?.ScaleMode??"Drag";set=>SetVisualOption(value,ScaleModes,nameof(ScaleMode),(state,v)=>state.ScaleMode=v);}
    public string RotateZMode {get=>VisualState?.RotateZMode??"Ring";set=>SetVisualOption(value,RotateZModes,nameof(RotateZMode),(state,v)=>state.RotateZMode=v);}
    public string ShiftMode {get=>VisualState?.ShiftMode??"All";set=>SetVisualOption(value,ShiftModes,nameof(ShiftMode),(state,v)=>state.ShiftMode=v);}
    public IReadOnlyList<string> DrawingModes {get;}=["Select","Move","Resize","Rotate","Contour","Line","Bicubic","Convert","Insert","Remove","Freehand","Smooth","Spline"];
    public IReadOnlyList<string> DrawingPresets {get;}=["No preset","Rectangle","Ellipse","Rounded box","Triangle","Diamond","Star"];
    public IReadOnlyList<string> VisualNumericTags=>AssVisualTags.NumericVisualTags;
    public IReadOnlyList<string> VisualTagOperations {get;}=["Adjust","Set","By line","Reverse lines","By text","Reverse text"];
    public string DrawingMode {get=>VisualState?.DrawingMode??"Select";set=>SetVisualOption(value,DrawingModes,nameof(DrawingMode),(state,v)=>state.DrawingMode=v);}
    public string DrawingPreset {get=>VisualState?.DrawingPreset??"No preset";set=>SetVisualOption(value,DrawingPresets,nameof(DrawingPreset),(state,v)=>state.DrawingPreset=v);}
    public string VisualNumericTag {get=>VisualState?.NumericVisualTag??"bord";set=>SetVisualOption(value,VisualNumericTags,nameof(VisualNumericTag),(state,v)=>state.NumericVisualTag=v);}
    public string VisualTagOperation {get=>VisualState?.VisualTagOperation??"Adjust";set=>SetVisualOption(value,VisualTagOperations,nameof(VisualTagOperation),(state,v)=>state.VisualTagOperation=v);}
    private void SetVisualOption(string value,IReadOnlyList<string> options,string property,Action<DocumentState,string> assign)
    {
        if(VisualState is not {} state||!options.Contains(value))return;
        CancelGesture();assign(state,value);OnPropertyChanged(property);OnPropertyChanged(nameof(VisualHint));
    }
    public bool IsClipFamily=>IsClipTool||IsVectorClipTool;
    public string VisualHint=>ActiveVisualTool switch
    {
        "Crosshair"=>"Double-click: shift visible selection · coordinates in script pixels",
        "Position" when PositionMode=="Two frames"=>"Click a video feature, seek to a different frame, then click the same feature",
        "Position" when PositionMode=="Origin"=>"Drag to set or translate the rotation origin",
        "Position" when PositionMode=="Rectangle"=>"Draw a reference rectangle and place the line at its alignment point",
        "Position"=>"Square: start · circle: end · triangle: origin · Shift: one axis · endpoint time: current frame",
        "RotateZ" when RotateZMode=="Guide line"=>"Drag along a visual guide to set the Z angle",
        "RotateZ"=>"Drag ring: rotate · Ctrl: 30° snap · triangle: origin",
        "RotateXY"=>"Drag: X/Y rotation · Shift: one axis · Ctrl: 30° snap · triangle: origin",
        "Scale" when ScaleMode=="Fit rectangle"=>"Draw a rectangle to fit the unrotated subtitle bounds",
        "Scale"=>"Drag: scale · Shift: one axis · Alt: aspect · Ctrl: 25% snap",
        "Clip"=>"Drag: new rectangle · corners: resize · Shift+drag / inside: move clip",
        "VectorClip"=>VectorMode+" · Ctrl: toggle points · empty drag: box select · Esc: cancel",
        "Drawing"=>DrawingPreset=="No preset"?DrawingMode+" · select an existing drawing or draw on a blank event":"Drag a shape onto the video · Shift: constrain proportions",
        "Shear"=>"Drag horizontally for fax, vertically for fay · Shift: one axis",
        "Shift"=>"Shift "+ShiftMode.ToLowerInvariant()+" by dragging · Shift: one axis",
        "Tags"=>"Drag horizontally to edit "+VisualNumericTag+" · Shift: fine adjustment",
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
            return new VisualLine(line,text,fallback,position,AssVisualTags.Origin(text)??position,AssVisualTags.Transform(text,style),relative,duration,ReferenceEquals(line,primary),
                AssVisualTags.Alignment(text,int.TryParse(style?.Get("Alignment"),out var alignment)&&alignment is >=1 and <=9?alignment:2),style);
        }).ToArray();
    }
    public bool SelectVisualLine(AssEvent line)
    {
        var suppress=_suppressSelectionSeek;_suppressSelectionSeek=true;
        try { SelectedEvent=line; return ReferenceEquals(SelectedEvent,line); }
        finally { _suppressSelectionSeek=suppress; }
    }
    internal void ReportUnsupportedDrawing()=>SubtitleStatus="Select an isolated drawing or a blank event. Animated geometry and advanced layout tags need the text editor.";
    public void UpdateVisualGesture(Func<VisualLine,string> edit)=>InvokeGesture("video/visual/update",edit);
    private void UpdateVisualGestureCore(Func<VisualLine,string> edit)
    {
        if(_gesture is null||_gestureEditor is null)return;
        // Compute the complete batch before mutating. Any parse failure rolls
        // back the transaction, including edits to earlier selected lines.
        var changes=_visualBaseline.Select(line=>(line.Line,Text:edit(line)))
            .Where(change=>change.Line.Text!=change.Text).ToArray();
        if(changes.Length==0)return;
        foreach(var change in changes)_gestureEditor.SetField(change.Line,"Text",change.Text,"Visual typesetting");
        if(IsDrawingGesture)OnPropertyChanged(nameof(EventEditorText));
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
        foreach(var property in new[]{nameof(IsCrosshairTool),nameof(IsPositionTool),nameof(IsRotateZTool),nameof(IsRotateXYTool),nameof(IsScaleTool),nameof(IsClipTool),nameof(IsVectorClipTool),nameof(IsDistortTool),nameof(IsClipFamily),nameof(IsDrawingTool),nameof(IsTagsTool),nameof(IsShiftTool),nameof(PositionMode),nameof(ScaleMode),nameof(RotateZMode),nameof(ShiftMode),nameof(DrawingMode),nameof(DrawingPreset),nameof(VisualNumericTag),nameof(VisualTagOperation),nameof(VisualHint)})OnPropertyChanged(property);
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
