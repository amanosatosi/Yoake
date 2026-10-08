using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Avalonia.Input;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;

namespace Yoake.UI.Controls;

// Keep TextBox itself in charge of input, clipboard, accessibility, IME and
// shaped-cluster navigation. Only its real text presenter's colors change.
public sealed class AssTextBox : TextBox
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // An Enter still delivered during composition belongs to the IME;
        // never let the multiline TextBox insert a physical newline for it.
        if(e.Key==Key.Enter&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))return;
        if(!e.Handled&&MoveAtVisualBoundary(e.Key,e.KeyModifiers)){e.Handled=true;return;}
        base.OnKeyDown(e);
    }
    public bool MoveAtVisualBoundary(Key key,KeyModifiers modifiers)
    {
        if(key is not Key.Up and not Key.Down||(modifiers&~KeyModifiers.Shift)!=0)return false;
        var presenter=this.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if(presenter is null||!string.IsNullOrEmpty(presenter.PreeditText))return false;
        var layout=presenter.TextLayout;var line=layout.GetLineIndexFromCharacterIndex(CaretIndex,true);
        if(key==Key.Up&&line!=0||key==Key.Down&&line!=layout.TextLines.Count-1)return false;
        var target=key==Key.Up?0:Text?.Length??0;
        var anchor=SelectionStart==SelectionEnd?CaretIndex:SelectionStart;
        CaretIndex=target;SelectionStart=modifiers.HasFlag(KeyModifiers.Shift)?anchor:target;SelectionEnd=target;
        return true;
    }
}

public sealed class AssTextPresenter : TextPresenter
{
    private readonly AssSyntaxDocument _syntax = new();
    private readonly TextRunCache _runs = new();
    private Size _constraint = Size.Infinity;
    public AssTextPresenter() => ActualThemeVariantChanged += (_,_) => InvalidateTextLayout();
    protected override Size MeasureOverride(Size availableSize) { _constraint=availableSize;return base.MeasureOverride(availableSize); }
    protected override Size ArrangeOverride(Size finalSize) { _constraint=new(finalSize.Width,double.PositiveInfinity);return base.ArrangeOverride(finalSize); }
    protected override void InvalidateTextLayout() { _runs.Invalidate();base.InvalidateTextLayout(); }
    protected override TextLayout CreateTextLayout()
    {
        // Avalonia owns combined/preedit text and its underline/caret indices.
        // Color runs resume when composition completes; never mutate preedit.
        if(!string.IsNullOrEmpty(PreeditText))return base.CreateTextLayout();
        _runs.Invalidate();
        var typeface=new Typeface(FontFamily,FontStyle,FontWeight,FontStretch);
        List<ValueSpan<TextRunProperties>> styles=[];
        var start=Math.Min(SelectionStart,SelectionEnd);var end=Math.Max(SelectionStart,SelectionEnd);
        foreach(var token in _syntax.Update(Text??""))
        {
            var brush=SyntaxBrush(token.Kind);
            var left=token.Start;var right=left+token.Length;
            if(ShowSelectionHighlight&&SelectionForegroundBrush is not null&&start<right&&end>left)
            {
                Add(left,Math.Min(start,right),brush);
                Add(Math.Max(left,start),Math.Min(right,end),SelectionForegroundBrush);
                Add(Math.Max(left,end),right,brush);
            }
            else Add(left,right,brush);
        }
        return new TextLayout(Text,typeface,FontSize,Foreground,TextAlignment,TextWrapping,null,null,FlowDirection,
            _constraint.Width>0?_constraint.Width:double.PositiveInfinity,
            _constraint.Height>0?_constraint.Height:double.PositiveInfinity,
            LineHeight,LetterSpacing,0,FontFeatures,styles,_runs);
        void Add(int left,int right,IBrush? brush)
        {
            if(right>left)styles.Add(new(left,right-left,new GenericTextRunProperties(typeface,FontSize,foregroundBrush:brush,fontFeatures:FontFeatures)));
        }
    }
    public IBrush? SyntaxBrush(AssSyntaxKind kind)
    {
        var key="AssSyntax"+kind;
        return this.TryFindResource(key,ActualThemeVariant,out var value)&&value is IBrush brush?brush:Foreground;
    }
}
