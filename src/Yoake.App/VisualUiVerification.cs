using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

// Real routed pointer input into the shipping overlay, with the packaged
// Mangetsu renderer. The dispatcher waits between edits and pixel assertions.
internal sealed class VisualUiVerification(MainWindow window,MainWindowViewModel model,
    Action<Control,string,double> capture,Func<byte[]> pixels)
{
    private readonly Pointer _pointer=new(410,PointerType.Mouse,true);
    private int _stage;
    private long _revision;
    private byte[]? _baselinePixels;
    private string _baseline="";
    private AssEvent? _line;
    private AssPoint _position;
    private VisualOverlayControl Overlay=>window.FindControl<VisualOverlayControl>("VisualOverlay")!;
    private (double Width,double Height) Size=>model.ScriptSize;
    private bool Ready=>model.DisplayedPreviewRevision>=_revision;
    private void WaitForPreview()=>_revision=model.PreviewRevision;
    private const string Plain=@"{\an5\fs80\bord0\shad0\1c&HFFFFFF&\future(keep)}VISUAL";

    public bool Tick()
    {
        switch(_stage++)
        {
            case 0:
                _line=model.SelectedEvent!;
                model.ActiveEditor!.SetTiming(_line,0,1000);
                model.CurrentTimeSeconds=0.4;
                Fixture(Plain);Tool("Position");WaitForPreview();return false;
            case 1:
                if(!Ready)return Retry();
                Shot("position-standby",1);
                capture(window,"editor-default-media-1440x900",1);
                Require(Video().Width>=600&&Video().Height>=300,"The fitted decoded video itself must retain usable typesetting dimensions.");
                _position=model.VisibleVisualLines().Single().Position;
                Require(AssVisualTags.Position(_line!.Text) is null,"Standby position must not insert a tag.");
                _baseline=_line.Text;_baselinePixels=pixels();
                Press(_position);Move(Add(_position,Size.Width*.15,0));WaitForPreview();return false;
            case 2:
                if(!Ready)return Retry();
                Require(model.HasGesture&&_line!.Text!=_baseline,"Pointer motion must update the active transaction.");
                Require(!_baselinePixels!.AsSpan().SequenceEqual(pixels()),"A visual drag must change actual Mangetsu pixels before release.");
                capture(window,"visual-position-live-drag",1);Escape();
                Require(!model.HasGesture&&_line!.Text==_baseline,"Escape must restore exact source and release the gesture.");
                WaitForPreview();return false;
            case 3:
                if(!Ready)return Retry();
                Require(_baselinePixels!.AsSpan().SequenceEqual(pixels()),"Escape must restore the original composited pixels.");
                Press(_position);Move(Add(_position,Size.Width*.1,0));Move(Add(_position,Size.Width*.15,0));Release(Add(_position,Size.Width*.15,0));
                Require(!model.HasGesture&&_line!.Text!=_baseline,"Release must commit a position drag.");
                Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,"One undo must restore the entire continuous drag.");
                Press(_position);Move(Add(_position,Size.Width*.12,0));_pointer.Capture(null);
                Require(!model.HasGesture&&_line.Text==_baseline,"Pointer capture loss must roll back the real overlay drag.");
                Press(_position);Move(Add(_position,Size.Width*.12,0));Tool("RotateZ");
                Require(!model.HasGesture&&_line.Text==_baseline,"Switching tools must roll back an in-flight pointer drag.");
                Fixture(At(_position)+@"\move("+N(Size.Width*.25)+","+N(Size.Height*.4)+","+N(Size.Width*.65)+","+N(Size.Height*.4)+@",100,800)\org("+N(Size.Width*.5)+","+N(Size.Height*.6)+@")}"+"MOVE");
                // Position and move are mutually exclusive in this fixture.
                Fixture(_line.Text.Replace(@"\pos("+N(_position.X)+","+N(_position.Y)+")","",StringComparison.Ordinal));
                Tool("Position");return false;
            case 4:
                Shot("position-move-origin",4);
                var move=AssVisualTags.Move(_line!.Text)!.Value;_baseline=_line.Text;
                Press(move.End);Move(Add(move.End,Size.Width*.04,0));Release(Add(move.End,Size.Width*.04,0));
                var edited=AssVisualTags.Move(_line!.Text)!.Value;
                Require(edited.Start==move.Start&&Math.Abs(edited.EndTime!.Value-400)<1,"Move end dragging must associate timing with the current FFMS2 frame.");
                Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,"Move endpoint undo must preserve all original timing/source.");
                Fixture(At(_position)+@"\org("+N(_position.X-Size.Width*.08)+","+N(_position.Y+Size.Height*.08)+@")}ROTATE");Tool("RotateZ");return false;
            case 5:
                ShotBoth("rotate-z",2);var origin=AssVisualTags.Origin(_line!.Text)!.Value;_baseline=_line.Text;
                var right=Add(origin,ScriptDip(65),0);var up=Add(origin,ScriptDip(46),-ScriptDip(46));
                Press(right);Move(up,KeyModifiers.Control);
                Require(Math.Abs(AssVisualTags.Scalar(_line.Text,"frz",0)-60)<.01,"Ctrl rotation must snap the ring drag to 30-degree increments.");
                Escape();Require(_line!.Text==_baseline,"Rotate Z cancel must restore exact source.");
                Press(origin);Move(Add(origin,Size.Width*.04,0));Release(Add(origin,Size.Width*.04,0));
                Require(AssVisualTags.Origin(_line.Text)!=origin,"Origin handle must be directly draggable.");Invoke(CommandIds.EditUndo);
                Tool("RotateXY");return false;
            case 6:
                ShotBoth("rotate-xy",1);_baseline=_line!.Text;
                var anchor=Add(_position,Size.Width*.15,0);Press(anchor);Move(Add(anchor,ScriptDip(20),ScriptDip(3)),KeyModifiers.Shift|KeyModifiers.Control);
                var transform=AssVisualTags.Transform(_line.Text,null);
                Require(transform.X==0&&transform.Y==30,"XY Shift drag must constrain the dominant axis and Ctrl must snap it.");Escape();
                Require(_line!.Text==_baseline,"XY cancellation must preserve source.");Tool("Scale");return false;
            case 7:
                ShotBoth("scale",4);_baseline=_line!.Text;
                Press(_position);Move(Add(_position,ScriptDip(20),-ScriptDip(10)),KeyModifiers.Alt|KeyModifiers.Control);
                var scaled=AssVisualTags.Transform(_line.Text,null);
                Require(scaled.ScaleX==125&&scaled.ScaleY==125,"Alt+Ctrl scaling must preserve aspect and snap to 25%.");Escape();
                Require(_line!.Text==_baseline,"Scale cancellation must preserve source.");
                Fixture(At(_position)+@"\iclip("+N(Size.Width*.25)+","+N(Size.Height*.25)+","+N(Size.Width*.75)+","+N(Size.Height*.75)+@")}CLIP");Tool("Clip");return false;
            case 8:
                ShotBoth("rectangle-inverse",4);_baseline=_line!.Text;var clip=AssVisualTags.Clip(_line.Text)!;
                Hover(clip.Points[0]);Require(Overlay.Cursor?.ToString()=="TopLeftCorner","Rectangle corner hover must show a resize cursor.");
                Press(clip.Points[0]);Move(Add(clip.Points[0],Size.Width*.05,Size.Height*.05));Release(Add(clip.Points[0],Size.Width*.05,Size.Height*.05));
                var resized=AssVisualTags.Clip(_line.Text)!;
                Require(resized.Inverse&&resized.Points[0]!=clip.Points[0]&&resized.Points[1]==clip.Points[1],"Corner resize must retain inverse and the opposite corner.");
                Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,"Rectangle undo must restore source.");
                Fixture(_baseline.Replace("}",@"\clips150}",StringComparison.Ordinal));var scaledSource=_line.Text;
                var scaledClip=AssVisualTags.Clip(scaledSource)!;var scaledMap=AssVisualTags.ClipTransform(scaledSource,400,1000);
                var mappedCorner=scaledMap.Map(scaledClip.Points[0]);var mappedOpposite=scaledMap.Map(scaledClip.Points[1]);var targetCorner=Add(mappedCorner,Size.Width*.05,Size.Height*.05);
                Press(mappedCorner);Move(targetCorner);Release(targetCorner);
                var resizedScaled=AssVisualTags.Clip(_line.Text)!;var resizedMap=AssVisualTags.ClipTransform(_line.Text,400,1000);
                Require(VisualGeometry.Distance(resizedMap.Map(resizedScaled.Points[0]),targetCorner)<.002&&VisualGeometry.Distance(resizedMap.Map(resizedScaled.Points[1]),mappedOpposite)<.002,"Scaled clip resize must follow the pointer while keeping the opposite displayed corner fixed.");
                Invoke(CommandIds.EditUndo);Require(_line.Text==scaledSource,"Scaled corner resize must undo exactly.");Fixture(_baseline);
                Fixture(_line!.Text.Replace("}",@"\clippos(5,-7)\t(0,1000,\clippos(20,10))}",StringComparison.Ordinal));
                var shiftedSource=_line.Text;var beforeMask=AssVisualTags.Clip(shiftedSource)!;
                var offset=AssVisualTags.ClipOffset(shiftedSource,400,1000);var center=Add(_position,offset.X,offset.Y);
                Press(center);Move(Add(center,Size.Width*.05,0));Release(Add(center,Size.Width*.05,0));
                Require(AssVisualTags.Clip(_line.Text)!.Points.SequenceEqual(beforeMask.Points),"Moving a clip with clippos must not bake the offset into its geometry.");
                Require(Math.Abs(AssVisualTags.ClipOffset(_line.Text,400,1000).X-offset.X-Size.Width*.05)<.001,"Animated clip offset must translate by the full drag delta.");
                Invoke(CommandIds.EditUndo);Require(_line.Text==shiftedSource,"Clip offset drag must be one lossless undo.");
                Fixture(At(_position)+@"\iclip(3,"+Drawing()+@")\clippos(5,-7)\t(0,1000,\clippos(~+10,~-4))}VECTOR");
                _baseline=_line.Text;Press(_position);Move(Add(_position,Size.Width*.08,0));Release(Add(_position,Size.Width*.08,0));
                Require(_line!.Text==_baseline&&!model.HasGesture,"Rectangular tool miss must not destroy a vector clip.");Tool("VectorClip");return false;
            case 9:
                ShotBoth("vector-select",5);
                foreach(var mode in MainWindowViewModel.VectorModes)
                    Require(window.GetVisualDescendants().OfType<ToggleButton>().Any(b=>b.Name=="Vector"+mode&&b.IsVisible&&b.Bounds.Width>20),"Vector subtool must realize: "+mode);
                _baseline=_line!.Text;var vector=AssVisualTags.Clip(_line.Text)!;var path=AssVectorPath.Parse(vector.Drawing,vector.Scale)!;
                var map=AssVisualTags.ClipTransform(_line.Text,400,1000);var first=map.Map(path.Handles().First().Point);
                Press(first);Move(Add(first,Size.Width*.03,0));Release(Add(first,Size.Width*.03,0));
                Require(_line.Text!=_baseline&&AssVisualTags.Clip(_line.Text) is {Inverse:true,Scale:3},"Vector point drag must preserve inverse and drawing scale.");
                Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,"One vector drag must undo as one item.");
                var secondPoint=map.Map(path.Handles().Skip(1).First().Point);
                var boxStart=Add(first,-ScriptDip(12),-ScriptDip(12));var boxEnd=Add(secondPoint,ScriptDip(12),ScriptDip(12));
                Press(boxStart);Move(boxEnd);Release(boxEnd);
                Require(!model.HasGesture&&_line.Text==_baseline,"Box selection must not mutate subtitle source.");
                Press(first);Move(Add(first,Size.Width*.03,0));Release(Add(first,Size.Width*.03,0));
                var grouped=AssVectorPath.Parse(AssVisualTags.Clip(_line.Text)!.Drawing,3)!.Handles().ToArray();var originalHandles=path.Handles().ToArray();
                Require(grouped.Take(2).All(h=>Math.Abs(h.Point.X-originalHandles[h.Index].Point.X-Size.Width*.03)<.25)&&grouped[2].Point==originalHandles[2].Point,"Box-selected endpoints must drag together without moving other controls.");
                Invoke(CommandIds.EditUndo);Press(first,KeyModifiers.Control);Release(first);
                Press(secondPoint);Move(Add(secondPoint,Size.Width*.03,0));Release(Add(secondPoint,Size.Width*.03,0));
                var toggled=AssVectorPath.Parse(AssVisualTags.Clip(_line.Text)!.Drawing,3)!.Handles().ToArray();
                Require(toggled[0].Point==originalHandles[0].Point&&toggled[1].Point!=originalHandles[1].Point,"Ctrl must toggle a point out of the drag selection.");Invoke(CommandIds.EditUndo);
                VectorMode("Convert");var curve=path.Curves().First(c=>!c.Cubic);var point=map.Map(curve.At(.5));Click(point);
                Require(AssVectorPath.Parse(AssVisualTags.Clip(_line.Text)!.Drawing,3)!.Curves().Count(c=>c.Cubic)==path.Curves().Count(c=>c.Cubic)+1,"Convert subtool must produce a cubic drawing.");Invoke(CommandIds.EditUndo);
                VectorMode("Insert");Click(point);
                Require(AssVectorPath.Parse(AssVisualTags.Clip(_line.Text)!.Drawing,3)!.PointCount>path.PointCount,"Insert must split the nearest segment.");Invoke(CommandIds.EditUndo);
                VectorMode("Remove");Click(first);
                Require(AssVectorPath.Parse(AssVisualTags.Clip(_line.Text)!.Drawing,3)!.PointCount<path.PointCount,"Remove must delete a hit point.");Invoke(CommandIds.EditUndo);
                foreach(var mode in new[]{"Line","Bicubic"})
                {
                    VectorMode(mode);var prospective=Add(_position,Size.Width*.2,Size.Height*.1);Hover(prospective);
                    capture(window,"visual-vector-"+mode.ToLowerInvariant()+"-prospective",1);
                    var preview=Overlay.VectorPreviewDrawing;Require(preview is not null,"Prospective point must render a complete closed path.");
                    var expected=path.Copy();expected.Append(map.Unmap(prospective),mode=="Bicubic");
                    Require(preview==expected.Serialize(3),"Preview must contain the exact prospective segment and its new closing edge.");
                    var topology=expected.Curves(true).ToArray();Require(topology[^1].Start==expected.Commands[^1].Points[^1]&&topology[^1].End==expected.Commands[0].Points[0],"Prospective closing edge must run from cursor to contour start.");
                    Click(prospective);Require(AssVisualTags.Clip(_line.Text)!.Drawing==preview,"Committed drawing topology must match the hover preview exactly.");Invoke(CommandIds.EditUndo);
                }
                return false;
            case 10:
                foreach(var mode in new[]{"Freehand","Smooth"})
                {
                    _baseline=_line!.Text;VectorMode(mode);var start=Add(_position,-Size.Width*.2,-Size.Height*.15);Press(start);
                    for(var i=1;i<=8;i++)Move(Add(start,Size.Width*.04*i,Size.Height*.08*Math.Sin(i)));
                    Release(Add(start,Size.Width*.35,0));var drawing=AssVisualTags.Clip(_line.Text)!;
                    Require(drawing.Inverse&&drawing.Scale==3&&AssVectorPath.Parse(drawing.Drawing,3) is {PointCount:>2},mode+" must produce a valid scaled inverse clip.");
                    capture(window,"visual-vector-"+mode.ToLowerInvariant(),1);Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,mode+" must be one undo action.");
                }
                Fixture(At(_position)+@"\distort(1,0,1,1,0,1)}DISTORT");Tool("Distort");return false;
            case 11:
                if(model.VisibleVisualLines().FirstOrDefault() is not {} line||model.VisualBounds(line) is not {} bounds)return Retry();
                if(model.DisplayedPreviewRevision<model.PreviewRevision)return Retry();
                ShotBoth("distort",5);_baseline=_line!.Text;_baselinePixels=pixels();
                var corner=Add(_position,bounds.Left,bounds.Top);Press(corner);Move(Add(corner,Size.Width*.04,-Size.Height*.04));WaitForPreview();return false;
            case 12:
                if(!Ready)return Retry();
                Require(!_baselinePixels!.AsSpan().SequenceEqual(pixels()),"Distort corner dragging must change actual Mangetsu pixels before commit.");
                Require(model.HasGesture&&AssVisualTags.Distort(_line!.Text) is {Count:4},"Distort corner drag must edit four normalized points.");
                Require(AssVisualTags.Scan(_line!.Text).Last(t=>t.Name=="distort").Arguments.Split(',').Length==8,"Intentional distort edit must upgrade six to eight slots.");
                Require(!_line.Text.Contains(@"\perspective",StringComparison.Ordinal),"Visual tool must never emit perspective.");
                capture(window,"visual-distort-live-drag",1);Escape();Require(_line!.Text==_baseline,"Distort cancel must restore original legacy syntax.");
                // Visibility and multi-line checks use the same overlay host.
                Fixture(At(_position)+"}MULTI");var second=model.Events.First(e=>!ReferenceEquals(e,_line)&&!e.IsComment);
                model.ActiveEditor!.SetTiming(second,0,1000);model.ActiveEditor.SetField(second,"Text",At(Add(_position,Size.Width*.1,Size.Height*.15))+"}SECOND","Visual fixture");
                model.SetSelectedEvents([_line,second]);Tool("Position");return false;
            case 13:
                Shot("position-multiple",2);var selected=model.VisibleVisualLines().ToArray();Require(selected.Length==2,"Both selected visible lines must be available.");
                var originals=selected.Select(l=>l.Text).ToArray();Press(selected.Single(l=>l.Active).Position);Move(Add(_position,Size.Width*.05,0));Release(Add(_position,Size.Width*.05,0));
                var shifted=model.VisibleVisualLines().ToArray();
                for(var i=0;i<2;i++)Require(Math.Abs(shifted[i].Position.X-selected[i].Position.X-Size.Width*.05)<.01,"Multi-line drag must preserve relative offsets.");
                Invoke(CommandIds.EditUndo);Require(selected.Select(l=>l.Line.Text).SequenceEqual(originals),"One undo must restore all selected lines.");
                model.ActiveEditor!.SetTiming(_line!,0,200);model.ActiveEditor.SetTiming(selected.Single(l=>!l.Active).Line,0,200);
                return false;
            case 14:
                capture(Overlay,"visual-outside-line-time",1);Require(Overlay.RenderedHandles==0,"Seeking outside selected line times must remove stale handles.");
                model.SetSelectedEvents([_line!]);model.ActiveEditor!.SetTiming(_line!,0,1000);Tool("Crosshair");return false;
            case 15:
                Fixture(Plain);model.InverseClip=false;Tool("Clip");WaitForPreview();return false;
            case 16:
                if(!Ready)return Retry();
                capture(Overlay,"visual-rectangle-no-clip-input-surface",1);
                var a=new AssPoint(Size.Width*.4,Size.Height*.4);var b=new AssPoint(Size.Width*.6,Size.Height*.6);
                Require(ReferenceEquals(window.InputHitTest(Root(a)),Overlay),"Empty rectangle canvas must receive real pointer hits away from its hint/handles.");
                // Include a pending text draft, as in the real typing -> tool workflow.
                model.Draft!.Text=Plain.Replace(@"\future(keep)",@"\future(pending)",StringComparison.Ordinal);_baseline=model.Draft.Text;_baselinePixels=pixels();
                Press(b);Move(a);Require(model.HasGesture&&AssVisualTags.Clip(_line!.Text) is {Rectangular:true},"Dragging a no-clip draft must immediately create a live rectangle.");WaitForPreview();return false;
            case 17:
                if(!Ready)return Retry();
                Require(!_baselinePixels!.AsSpan().SequenceEqual(pixels()),"No-clip creation must update actual Mangetsu pixels before release.");
                capture(window,"visual-rectangle-create-live",1);Require(Overlay.RenderedHandles==4,"New rectangle must have four visible corner handles before release.");
                var low=new AssPoint(Size.Width*.4,Size.Height*.4);var high=new AssPoint(Size.Width*.6,Size.Height*.6);
                Release(low);var created=AssVisualTags.Clip(_line!.Text)!;
                Require(!model.HasGesture&&!created.Inverse&&VisualGeometry.Distance(created.Points[0],low)<.001&&VisualGeometry.Distance(created.Points[1],high)<.001,"Release must commit normalized rectangle coordinates.");
                Invoke(CommandIds.EditUndo);Require(_line.Text==_baseline,"One undo must remove the whole new clip and retain the committed preceding draft.");
                Press(low);Move(high);Escape();Require(!model.HasGesture&&_line.Text==_baseline,"Escape must leave no partial newly created clip.");
                model.InverseClip=true;Press(low);Move(high);Release(high);
                Require(AssVisualTags.Clip(_line.Text) is {Rectangular:true,Inverse:true},"New inverse clip must create iclip from no clip.");
                Invoke(CommandIds.EditUndo);Require(_line.Text==_baseline,"New inverse clip must undo exactly.");model.InverseClip=false;Tool("Crosshair");return false;
            case 18:
                var quick=Add(_position,Size.Width*.08,Size.Height*.05);Hover(quick);ShotBoth("crosshair",0);_baseline=_line!.Text;Press(quick,KeyModifiers.None,2);
                Require(!model.HasGesture&&AssVisualTags.Position(_line.Text) is {} placed&&VisualGeometry.Distance(placed,quick)<.001,"Crosshair double-click must position the line and finish one transaction.");
                Invoke(CommandIds.EditUndo);Require(_line!.Text==_baseline,"Quick position must undo losslessly.");Tool("Position");return true;
            default:return true;
        }
    }
    private string At(AssPoint p)=>@"{\an5\fs80\bord0\shad0\1c&HFFFFFF&\pos("+N(p.X)+","+N(p.Y)+")";
    private string Drawing()=>"m "+N(Size.Width)+" "+N(Size.Height)+" l "+N(Size.Width*3)+" "+N(Size.Height)+" b "+N(Size.Width*3)+" "+N(Size.Height*2)+" "+N(Size.Width*2)+" "+N(Size.Height*3)+" "+N(Size.Width)+" "+N(Size.Height*3);
    private void Fixture(string text){model.ActiveEditor!.SetField(_line!,"Text",text,"Visual verification fixture");}
    private void Tool(string name)=>Invoke("video/tool/"+name.ToLowerInvariant());
    private void VectorMode(string name)=>Invoke("video/vector/"+name.ToLowerInvariant());
    private void Invoke(string id){var result=model.Registry.InvokeAsync(id,new());Require(result.IsCompletedSuccessfully&&result.Result,"Visual verification command failed: "+id);}
    private bool Retry(){_stage--;return false;}
    private void Shot(string name,int handles)
    {
        capture(window,"visual-"+name+"-dark",1);
        Require(Overlay.RenderedTool==model.ActiveVisualTool&&Overlay.RenderedHandles>=handles,"Expected rendered features for "+name+", got "+Overlay.RenderedHandles);
        var active=window.FindControl<ToggleButton>("VisualTool"+model.ActiveVisualTool)!;
        Require(active.IsChecked==true,"Active visual tool must have checked toggle state.");
    }
    private void ShotBoth(string name,int handles)
    {
        Shot(name,handles);window.RequestedThemeVariant=ThemeVariant.Light;capture(window,"visual-"+name+"-light",1);window.RequestedThemeVariant=ThemeVariant.Dark;
        foreach(var scale in new[]{1.25,1.5,2})capture(Overlay,"visual-"+name+"-"+N(scale*100),scale);
    }
    private Rect Video()
    {
        var frame=model.VideoFrame!;var size=Overlay.Bounds.Size;var scale=Math.Min(size.Width/frame.PixelSize.Width,size.Height/frame.PixelSize.Height);
        var w=frame.PixelSize.Width*scale;var h=frame.PixelSize.Height*scale;return new((size.Width-w)/2,(size.Height-h)/2,w,h);
    }
    private double ScriptDip(double dip)=>dip*Size.Width/Video().Width;
    private Point Root(AssPoint p){var video=Video();return Overlay.TranslatePoint(new(video.X+p.X/Size.Width*video.Width,video.Y+p.Y/Size.Height*video.Height),window)!.Value;}
    private static PointerPointProperties Properties(bool down,PointerUpdateKind kind)=>new(down?RawInputModifiers.LeftMouseButton:RawInputModifiers.None,kind);
    private void Press(AssPoint p,KeyModifiers modifiers=KeyModifiers.None,int count=1)=>Overlay.RaiseEvent(new PointerPressedEventArgs(Overlay,_pointer,window,Root(p),0,Properties(true,PointerUpdateKind.LeftButtonPressed),modifiers,count));
    private void Move(AssPoint p,KeyModifiers modifiers=KeyModifiers.None)=>Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,Overlay,_pointer,window,Root(p),0,Properties(true,PointerUpdateKind.Other),modifiers));
    private void Hover(AssPoint p)=>Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,Overlay,_pointer,window,Root(p),0,Properties(false,PointerUpdateKind.Other),KeyModifiers.None));
    private void Release(AssPoint p)=>Overlay.RaiseEvent(new PointerReleasedEventArgs(Overlay,_pointer,window,Root(p),0,Properties(false,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
    private void Click(AssPoint p){Press(p);Release(p);}
    private void Escape()=>Overlay.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape,Source=Overlay});
    private static AssPoint Add(AssPoint p,double x,double y)=>new(p.X+x,p.Y+y);
    private static string N(double value)=>value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
