using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class PostprocessWindow : Window
{
    public PostprocessWindow(MainWindowViewModel model,Guid owner)
    {
        Name="TimingPostprocessDialog";Title="Timing Postprocessor";Width=760;Height=610;MinWidth=580;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var preferences=model.PostprocessPreferences;var names=new NameScope();NameScope.SetNameScope(this,names);
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("*,Auto,Auto"),RowSpacing=10 };Content=root;
        var body=new Grid { ColumnDefinitions=new("180,*"),ColumnSpacing=14 };root.Children.Add(body);
        var stylesPanel=new Grid { RowDefinitions=new("Auto,*,Auto"),RowSpacing=8 };body.Children.Add(stylesPanel);
        stylesPanel.Children.Add(new TextBlock { Text="Apply to styles",FontWeight=FontWeight.SemiBold });
        var styles=new StackPanel { Spacing=4 };Grid.SetRow(styles,1);stylesPanel.Children.Add(new ScrollViewer { Content=styles,[Grid.RowProperty]=1 });
        foreach(var style in model.StyleNames)styles.Children.Add(new CheckBox { Content=style,IsChecked=preferences.Styles is null||preferences.Styles.Contains(style) });
        var styleActions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6 };At(stylesPanel,styleActions,2);
        foreach(var label in new[] { "All","None" }) { var button=new Button { Content=label };styleActions.Children.Add(button);button.Click+=(_,_)=> { foreach(var item in styles.Children.OfType<CheckBox>())item.IsChecked=label=="All"; }; }
        var content=new StackPanel { Spacing=9 };Grid.SetColumn(content,1);body.Children.Add(new ScrollViewer { Content=content,[Grid.ColumnProperty]=1,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        CheckBox Toggle(string name,string text,bool value)
        {var box=new CheckBox { Name=name,Content=text,IsChecked=value };names.Register(name,box);content.Children.Add(box);return box;}
        NumericUpDown Number(string name,string text,int value,int max=60000)
        {
            var row=new Grid { ColumnDefinitions=new("*,115"),ColumnSpacing=8 };row.Children.Add(new TextBlock { Text=text,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap });
            var number=new NumericUpDown { Name=name,Minimum=0,Maximum=max,Increment=1,FormatString="0",Value=value };names.Register(name,number);Grid.SetColumn(number,1);row.Children.Add(number);content.Children.Add(row);return number;
        }
        var selected=Toggle("PostprocessSelectedOnly","Affect selected rows only",preferences.SelectedOnly);
        var leadIn=Toggle("PostprocessLeadInEnabled","Add lead-in (avoid new overlaps)",preferences.AddLeadIn);var leadInTime=Number("PostprocessLeadIn","Lead-in (ms)",preferences.LeadIn);
        var leadOut=Toggle("PostprocessLeadOutEnabled","Add lead-out (avoid new overlaps)",preferences.AddLeadOut);var leadOutTime=Number("PostprocessLeadOut","Lead-out (ms)",preferences.LeadOut);
        var adjacent=Toggle("PostprocessAdjacent","Make nearby lines adjacent",preferences.MakeAdjacent);
        var gap=Number("PostprocessGap","Maximum gap (ms)",preferences.MaximumGap);var overlap=Number("PostprocessOverlap","Maximum overlap (ms)",preferences.MaximumOverlap);
        var bias=Number("PostprocessBias","Boundary bias: 0 = previous end, 100 = next start",(int)(preferences.AdjacentBias*100),100);
        var snap=Toggle("PostprocessKeyframes","Snap to keyframes",preferences.SnapKeyframes);
        ToolTip.SetTip(snap,"Requires loaded video/timecodes and keyframes.");
        var startBefore=Number("PostprocessStartBefore","Start before keyframe (ms)",preferences.StartBefore);var startAfter=Number("PostprocessStartAfter","Start after keyframe (ms)",preferences.StartAfter);
        var endBefore=Number("PostprocessEndBefore","End before keyframe (ms)",preferences.EndBefore);var endAfter=Number("PostprocessEndAfter","End after keyframe (ms)",preferences.EndAfter);
        content.Children.Add(new TextBlock { Text="Runs leads, adjacency, then keyframe snapping. Comments, metadata and inherited events are excluded. Preview calculates without changing subtitles. Apply creates one undo step.",TextWrapping=TextWrapping.Wrap,Opacity=0.8 });
        var status=new TextBlock { Name="PostprocessStatus",TextWrapping=TextWrapping.Wrap };names.Register(status.Name!,status);At(root,status,1);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8 };At(root,actions,2);
        var lifetime=new CancellationTokenSource();var closed=false;
        foreach(var (label,name) in new[] { ("Preview","PostprocessPreview"),("Apply","PostprocessApply"),("Cancel","PostprocessCancel") })
        {
            var button=new Button { Name=name,Content=label,IsCancel=label=="Cancel",IsDefault=label=="Apply" };names.Register(name,button);actions.Children.Add(button);
            button.Click+=async (_,_)=>
            {
                if(label=="Cancel") { Close();return; }
                try
                {
                    int Value(NumericUpDown control)=>control.Value is {} value&&!control.GetValue(DataValidationErrors.HasErrorsProperty)?(int)value:throw new ArgumentException("Enter a numeric timing threshold.");
                    var options=new SubtitlePostprocessOptions { Styles=styles.Children.OfType<CheckBox>().Where(c=>c.IsChecked==true).Select(c=>(string)c.Content!).ToArray(),SelectedOnly=selected.IsChecked==true,
                        AddLeadIn=leadIn.IsChecked==true,LeadIn=Value(leadInTime),AddLeadOut=leadOut.IsChecked==true,LeadOut=Value(leadOutTime),MakeAdjacent=adjacent.IsChecked==true,
                        MaximumGap=Value(gap),MaximumOverlap=Value(overlap),AdjacentBias=Value(bias)/100d,SnapKeyframes=snap.IsChecked==true,StartBefore=Value(startBefore),StartAfter=Value(startAfter),EndBefore=Value(endBefore),EndAfter=Value(endAfter) };
                    IsEnabled=false;
                    var success=await model.Registry.InvokeAsync("tool/time/postprocess/apply",new(owner),new SubtitlePostprocessToolRequest(owner,options,label=="Preview"),lifetime.Token);
                    if(!closed) { status.Text=model.SubtitleStatus;if(success&&label=="Apply")Close(); }
                }
                catch(Exception error) when(error is not OperationCanceledException) { if(!closed)status.Text=error.Message; }
                catch(OperationCanceledException) { }
                finally { if(!closed)IsEnabled=true; }
            };
        }
        void OwnerChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args) { if(model.ActiveDocumentId!=owner)Close(); }
        model.PropertyChanged+=OwnerChanged;Closed+=(_,_)=> { closed=true;lifetime.Cancel();lifetime.Dispose();model.PropertyChanged-=OwnerChanged; };
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key is Key.Enter or Key.Escape&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))e.Handled=true;},RoutingStrategies.Tunnel);
        names.Complete();
    }
    private static void At(Grid parent,Control child,int row) { Grid.SetRow(child,row);parent.Children.Add(child); }
}
