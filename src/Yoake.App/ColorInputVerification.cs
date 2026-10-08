using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Yoake.UI.Controls;

namespace Yoake.App;

internal static class ColorInputVerification
{
    public static void Run(AssColorDialog dialog,Action<Control,string,double> capture)
    {
        var pointer=new Pointer(411,PointerType.Mouse,true);
        var hue=Named<ColorStrip>(dialog,"ColorHue");var alpha=Named<ColorStrip>(dialog,"ColorTransparency");
        capture(dialog,"color-picker-before-input",1);
        Require(ReferenceEquals(dialog.InputHitTest(hue.TranslatePoint(new(12,64),dialog)!.Value),hue),"Hue gradient must route real pointer hits to its selector away from the thin marker.");
        Require(ReferenceEquals(dialog.InputHitTest(alpha.TranslatePoint(new(12,64),dialog)!.Value),alpha),"Alpha gradient must route real pointer hits to its selector away from the thin marker.");
        var original=hue.Value;
        Press(hue,new(12,64));Move(hue,new(12,192));
        Require(Math.Abs(hue.Value-hue.Maximum*.75)<.01,$"Hue strip drag must directly follow pointer position (value {hue.Value}, expected {hue.Maximum*.75}, bounds {hue.Bounds}).");
        pointer.Capture(null);Require(Math.Abs(hue.Value-original)<.01,"Color strip capture loss must restore its initial value.");
        Press(hue,new(12,64));Move(hue,new(12,192));Release(hue,new(12,192));
        Require(Math.Abs(hue.Value-hue.Maximum*.75)<.01,"Hue strip release must keep the chosen value.");
        var transparency=alpha.Value;
        alpha.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=alpha,Key=Key.End});
        Require(alpha.Value==255&&dialog.SelectedColor.Transparency==255,"Alpha strip must support exact keyboard endpoints.");
        alpha.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=alpha,Key=Key.Home});
        Require(alpha.Value==0&&dialog.SelectedColor.Transparency==0,"Alpha strip Home must reach opaque.");alpha.Value=transparency;
        var exact=Named<TextBox>(dialog,"AssHex");var previous=exact.Text;
        Named<TextBox>(dialog,"HtmlHex").SetCurrentValue(TextBox.TextProperty,"#808080");hue.Value=143.25;
        Require(hue.Value==143.25&&dialog.SelectedColor.Red==128&&dialog.SelectedColor.Green==128&&dialog.SelectedColor.Blue==128,"Hue authoring must remain stable on achromatic colors.");
        exact.SetCurrentValue(TextBox.TextProperty,previous);
        if(OperatingSystem.IsWindows())
        {
            var dropper=Named<ScreenColorDropper>(dialog,"ScreenMagnifier");var button=Named<Button>(dialog,"Eyedropper");
            var color=dialog.SelectedColor;
            Press(button,new(10,10));Require(dropper.IsSampling,"Eyedropper button must begin live desktop sampling.");
            capture(dialog,"color-picker-live-magnifier",1);
            Release(dropper,new(29,29));Require(dropper.IsSampling,"An unmoved click must latch the eyedropper for desktop movement.");
            pointer.Capture(null);Require(!dropper.IsSampling&&dialog.SelectedColor==color,"Capture loss must cancel sampling without changing the color.");
            Press(button,new(10,10));Release(dropper,new(29,29));Press(dropper,new(29,29));Release(dropper,new(29,29));
            Require(!dropper.IsSampling&&dialog.SelectedColor.Transparency==color.Transparency,"Sample acceptance must keep ASS alpha separate.");
            Press(button,new(10,10));dropper.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Source=dropper,Key=Key.Escape});
            Require(!dropper.IsSampling&&dialog.IsVisible,"Escape must cancel the magnifier while leaving the color dialog open.");
        }
        Point Root(Control control,Point p)=>control.TranslatePoint(p,dialog)!.Value;
        void Press(Control control,Point p)=>control.RaiseEvent(new PointerPressedEventArgs(control,pointer,dialog,Root(control,p),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
        void Move(Control control,Point p)=>control.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,control,pointer,dialog,Root(control,p),0,new(RawInputModifiers.LeftMouseButton,PointerUpdateKind.Other),KeyModifiers.None));
        void Release(Control control,Point p)=>control.RaiseEvent(new PointerReleasedEventArgs(control,pointer,dialog,Root(control,p),0,new(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
    }
    private static T Named<T>(Control root,string name) where T:Control=>root.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
