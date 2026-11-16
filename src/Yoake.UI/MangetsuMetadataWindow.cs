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

public sealed class MangetsuMetadataWindow : Window
{
    public MangetsuMetadataWindow(MainWindowViewModel model,Guid owner)
    {
        var editor=model.ActiveEditor??throw new InvalidOperationException("Open subtitles first.");
        var revision=editor.Document.Revision;
        Name="MangetsuMetadataDialog";Title="Mangetsu Metadata";Width=750;Height=570;MinWidth=520;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        var kind=new ComboBox { Name="MangetsuMetadataKind",ItemsSource=new[] { MangetsuMetadataKind.Style,MangetsuMetadataKind.Actor },SelectedIndex=0 };
        var target=new AutoCompleteBox { Name="MangetsuMetadataTarget",MinimumPrefixLength=0,FilterMode=AutoCompleteFilterMode.Contains,Text=model.SelectedEvent?.Style??model.StyleNames.FirstOrDefault()??"Default" };
        var text=new TextBox { Name="MangetsuMetadataText",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=90 };
        var status=new TextBlock { Name="MangetsuMetadataStatus",TextWrapping=TextWrapping.Wrap };
        var explanation=new TextBlock { Text="Style extensions and Actor Colorcoding are initial ASS Comment events. Apply creates one undo step and updates the subtitle preview. Unknown tags are retained. Close discards unapplied text.",TextWrapping=TextWrapping.Wrap };
        var root=new Grid { Margin=new Thickness(14),RowDefinitions=new("Auto,Auto,Auto,Auto,*,Auto,Auto"),RowSpacing=9 };root.Children.Add(explanation);
        var header=new Grid { ColumnDefinitions=new("Auto,*"),ColumnSpacing=8 };header.Children.Add(kind);Grid.SetColumn(target,1);header.Children.Add(target);At(root,header,1);
        At(root,new TextBlock { Text="ASS override payload / actor style whitelist",FontWeight=FontWeight.SemiBold },2);
        var presets=new WrapPanel();At(root,presets,3);
        foreach(var (label,payload) in new[] {
            ("Furigana",@"{\furis70\furifsp1}"),("Vertical text",@"{\vert1\vtype3\vdir2\vsp2\vcolsp8}"),
            ("Box",@"{\boxpx10\boxpy5\boxr3\bbs2\bbc&HFFFFFF&}"),("Gradient",@"{\1grd(45,&HFF0000&,&H0000FF&)}"),
            ("Multiple borders",@"{\2bs3\2bc&HFFAA00&\2ba&H40&}") })
        {
            var button=new Button { Content=label,Margin=new Thickness(0,0,5,4) };ToolTip.SetTip(button,payload);presets.Children.Add(button);
            button.Click+=(_,_)=>text.Text=(text.Text??"")+payload;
        }
        At(root,text,4);At(root,status,5);var actions=new WrapPanel();At(root,actions,6);
        foreach(var (label,name) in new[] { ("Apply","MangetsuMetadataApply"),("Remove defaults","MangetsuMetadataRemove"),("Reload","MangetsuMetadataReload"),("Close","MangetsuMetadataClose") })
        {
            var button=new Button { Name=name,Content=label,Margin=new Thickness(0,0,6,4) };actions.Children.Add(button);names.Register(name,button);
            button.Click+=async (_,_)=>
            {
                if(label=="Close") { Close();return; }
                if(label=="Reload") { Load();return; }
                var request=new MangetsuMetadataEdit(owner,revision,(MangetsuMetadataKind)kind.SelectedItem!,target.Text??"",text.Text??"",label=="Remove defaults");
                if(await model.Registry.InvokeAsync("mangetsu/metadata/apply",new(owner),request)) { revision=editor.Document.Revision;status.Text="Metadata applied. Undo restores the previous source."; }
                else status.Text=model.SubtitleStatus;
            };
        }
        foreach(var control in new Control[] { kind,target,text,status })names.Register(control.Name!,control);
        void Load()
        {
            revision=editor.Document.Revision;
            var selected=(MangetsuMetadataKind)kind.SelectedItem!;
            target.ItemsSource=selected==MangetsuMetadataKind.Style?model.StyleNames:editor.Document.Events.Select(e=>e.Actor).Where(a=>a.Length>0).Append("mangetsu-colorcode-applied-styles").Distinct().Order().ToArray();
            text.Text=MangetsuMetadata.Initial(editor.Document).LastOrDefault(e=>MangetsuMetadata.Kind(e)==selected&&MangetsuMetadata.MatchesTarget(e,selected,target.Text??""))?.Text??"";
            presets.IsVisible=selected==MangetsuMetadataKind.Style;
            status.Text=selected==MangetsuMetadataKind.Style?"Choose a style and edit its renderer defaults.":"Enter an actor name. The reserved actor mangetsu-colorcode-applied-styles accepts {Style}{Style 2} whitelist entries.";
        }
        kind.SelectionChanged+=(_,_)=> { target.Text=kind.SelectedItem is MangetsuMetadataKind.Style?model.StyleNames.FirstOrDefault()??"Default":"";Load(); };
        target.PropertyChanged+=(_,e)=>{if(e.Property==AutoCompleteBox.TextProperty)Load();};
        void OwnerChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args) { if(model.ActiveDocumentId!=owner||!ReferenceEquals(model.ActiveEditor,editor))Close(); }
        model.PropertyChanged+=OwnerChanged;Closed+=(_,_)=>model.PropertyChanged-=OwnerChanged;
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.Key is Key.Enter or Key.Escape&&this.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))e.Handled=true;
            else if(e.Key==Key.Escape) { Close();e.Handled=true; }
        },RoutingStrategies.Tunnel);
        Content=root;Load();names.Complete();
    }
    private static void At(Grid parent,Control child,int row) { Grid.SetRow(child,row);parent.Children.Add(child); }
}
