using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;

namespace Yoake.UI;

// A native control dialog. All semantic actions use its registry and the same
// document editor; no reflection-based editors or serialization frameworks.
public sealed class StylesWindow : Window
{
    private readonly SubtitleEditor _editor;
    private readonly CommandRegistry _commands=new();
    private readonly ListBox _list=new();
    private readonly StackPanel _fields=new(){Spacing=3};
    private readonly Dictionary<string,TextBox> _inputs=[];
    private readonly TextBlock _status=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly ComboBox _replacement=new(){MinWidth=160};
    private AssStyle? _selected;
    private bool _switching;
    public StylesWindow(SubtitleEditor editor)
    {
        _editor=editor;Title="Styles Manager";Width=780;Height=680;MinWidth=640;MinHeight=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{RowDefinitions=new("*,Auto,Auto"),ColumnDefinitions=new("220,*"),Margin=new Thickness(12),ColumnSpacing=12,RowSpacing=8};
        _list.ItemsSource=editor.Document.Styles.Select(s=>s.Name).ToArray();_list.SelectionChanged+=(_,_)=>LoadSelection();
        root.Children.Add(_list);var scroll=new ScrollViewer{Content=_fields};Grid.SetColumn(scroll,1);root.Children.Add(scroll);
        Grid.SetRow(_status,1);Grid.SetColumnSpan(_status,2);root.Children.Add(_status);
        var bar=new WrapPanel{Orientation=Orientation.Horizontal};Grid.SetRow(bar,2);Grid.SetColumnSpan(bar,2);root.Children.Add(bar);
        _commands.CommandFailed+=(_,e)=>_status.Text=e.Exception.Message;
        Add("styles/apply","Apply",()=>{Apply();Refresh();});
        Add("styles/create","New",()=>{if(!Apply())return;var style=editor.AddStyle();Refresh(style.Name);});
        Add("styles/duplicate","Duplicate",()=>{if(!Apply())return;var style=editor.AddStyle(_selected);Refresh(style.Name);});
        Add("styles/up","Move up",()=>{if(!Apply()||_selected is null)return;editor.MoveStyle(_selected,-1);Refresh();});
        Add("styles/down","Move down",()=>{if(!Apply()||_selected is null)return;editor.MoveStyle(_selected,1);Refresh();});
        bar.Children.Add(new TextBlock{Text="Replace references with:",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,4,0)});bar.Children.Add(_replacement);
        Add("styles/delete","Delete",()=>{if(_selected is null)return;editor.DeleteStyle(_selected,_replacement.SelectedItem as string??"");_selected=null;Refresh();});
        Add("styles/undo","Undo",()=>{editor.Undo.Undo();Refresh();});Add("styles/redo","Redo",()=>{editor.Undo.Redo();Refresh();});
        Add("styles/close","Close",()=>{if(Apply())Close();});Content=root;Refresh(editor.Document.Styles.FirstOrDefault()?.Name);
        void Add(string id,string label,Action action)
        {
            _commands.Register(new AppCommand(new(id,label,label,"Styles"),(_,_)=>{action();return ValueTask.CompletedTask;}));
            bar.Children.Add(new Button{Content=label,Command=new RegistryCommand(_commands,id,()=>new()),Margin=new Thickness(2),Padding=new Thickness(8,3)});
        }
        Closing+=(_,e)=>{if(!Apply())e.Cancel=true;};
    }
    private bool Apply()
    {
        if(_selected is null)return true;
        try
        {
            var values=_inputs.ToDictionary(p=>p.Key,p=>p.Value.Text??"");
            StyleValidation.Validate(values.Where(p=>_selected.Get(p.Key)!=p.Value).ToDictionary());
            _editor.EditStyle(_selected,values);_status.Text="Style applied. Rename updates event references; delete requires a replacement.";return true;
        }
        catch(Exception e){_status.Text=e.Message;return false;}
    }
    private void Refresh(string? name=null)
    {
        _switching=true;var selected=name??_selected?.Name;_selected=null;_inputs.Clear();var names=_editor.Document.Styles.Select(s=>s.Name).ToArray();_list.ItemsSource=names;_replacement.ItemsSource=names;_list.SelectedItem=names.Contains(selected)?selected:names.FirstOrDefault();_replacement.SelectedItem=names.FirstOrDefault(n=>n!=_list.SelectedItem as string);_switching=false;LoadSelection();
    }
    private void LoadSelection()
    {
        if(_switching)return;var next=_editor.Document.Styles.FirstOrDefault(s=>s.Name==_list.SelectedItem as string);if(next==_selected&&_inputs.Count>0)return;
        if(!Apply()){_switching=true;_list.SelectedItem=_selected?.Name;_switching=false;return;}
        _selected=next;_inputs.Clear();_fields.Children.Clear();if(next is null)return;
        foreach(var name in next.FieldNames)
        {
            var row=new Grid{ColumnDefinitions=new("145,*"),ColumnSpacing=6};row.Children.Add(new TextBlock{Text=name,VerticalAlignment=VerticalAlignment.Center});
            var input=new TextBox{Text=next.Get(name),Padding=new Thickness(5,2)};_inputs[name]=input;Grid.SetColumn(input,1);row.Children.Add(input);_fields.Children.Add(row);
        }
    }
}

internal static class StyleValidation
{
    public static void Validate(IReadOnlyDictionary<string,string> values)
    {
        foreach(var p in values)
        {
            if(p.Key is "Fontsize" or "ScaleX" or "ScaleY" or "Spacing" or "Angle" or "Outline" or "Shadow")
            {
                if(!double.TryParse(p.Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new ArgumentException($"{p.Key} must be a number.");
                if(p.Key is "Fontsize" or "ScaleX" or "ScaleY" && n<=0)throw new ArgumentException($"{p.Key} must be positive.");
            }
            if(p.Key is "PrimaryColour" or "SecondaryColour" or "OutlineColour" or "BackColour")
            {
                var color=p.Value.Trim();if(!(color.StartsWith("&H",StringComparison.OrdinalIgnoreCase)&&uint.TryParse(color[2..].TrimEnd('&'),System.Globalization.NumberStyles.HexNumber,null,out _))&&!int.TryParse(color,out _)&&!uint.TryParse(color,out _))throw new ArgumentException($"{p.Key}: enter ASS &HAABBGGRR hex color.");
            }
            if(p.Key is "Alignment" or "BorderStyle" or "MarginL" or "MarginR" or "MarginV" or "Encoding" or "Bold" or "Italic" or "Underline" or "StrikeOut")
                if(!int.TryParse(p.Value,out var integer)||p.Key=="Alignment"&&integer is <1 or >9)throw new ArgumentException($"Invalid {p.Key}.");
        }
    }
}
