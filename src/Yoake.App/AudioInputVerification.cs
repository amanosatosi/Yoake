using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Yoake.UI;
using Yoake.UI.Controls;
using Yoake.UI.ViewModels;

namespace Yoake.App;

internal static class AudioInputVerification
{
    public static void Run(MainWindow window,MainWindowViewModel model)
    {
        var audio=window.FindControl<AudioWaveformControl>("AudioDisplay")!;
        var pane=window.FindControl<Grid>("TemporalTextColumn")!;
        var height=pane.RowDefinitions[0].ActualHeight;
        var span=audio.VisibleSeconds;var amplitude=model.AudioAmplitude;var gain=model.PlaybackVolume;
        Up("AudioHorizontalZoom");
        Require(audio.VisibleSeconds!=span&&model.AudioAmplitude==amplitude&&model.PlaybackVolume==gain,"Time keyboard input must change only horizontal span.");
        span=audio.VisibleSeconds;Up("AudioIntensity");
        Require(audio.VisibleSeconds==span&&model.AudioAmplitude!=amplitude&&model.PlaybackVolume==gain,"Amp keyboard input must change only display gain.");
        amplitude=model.AudioAmplitude;Up("AudioVolume");
        Require(audio.VisibleSeconds==span&&model.AudioAmplitude==amplitude&&model.PlaybackVolume!=gain,"Vol keyboard input must change only playback gain.");
        Require(pane.RowDefinitions[0].ActualHeight==height,"Focused audio sliders must never resize the pane.");

        var panner=window.FindControl<ScrollBar>("AudioPanner")!;
        var thumb=panner.GetVisualDescendants().OfType<Thumb>().Single(t=>t.IsVisible);
        Require(thumb.Bounds.Width>0&&thumb.Bounds.Height>0,"Audio panner thumb must be realized.");
        var start=audio.ViewportStart;var pointer=new Pointer(415,PointerType.Mouse,true);
        var center=thumb.TranslatePoint(new(thumb.Bounds.Width/2,thumb.Bounds.Height/2),window)!.Value;
        var hit=window.InputHitTest(center) as Visual;
        Require(ReferenceEquals(hit,thumb)||hit?.GetVisualAncestors().Contains(thumb)==true,"Audio panner must receive actual thumb hits.");
        thumb.RaiseEvent(new PointerPressedEventArgs(thumb,pointer,window,center,0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
        thumb.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,thumb,pointer,window,center+new Vector(24,0),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        thumb.RaiseEvent(new PointerReleasedEventArgs(thumb,pointer,window,center+new Vector(24,0),0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        Require(audio.ViewportStart>start&&audio.VisibleSeconds==span&&model.AudioAmplitude==amplitude,"Dragging the real panner thumb must pan without changing zoom or amplitude.");
        Require(pointer.Captured is null,"Panner release must end pointer capture.");
        void Up(string name)
        {
            var slider=window.FindControl<Slider>(name)!;Require(slider.Focus(),name+" must accept keyboard focus.");
            slider.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=slider,Key=Key.Up});
        }
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
