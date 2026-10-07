using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.Controls;

namespace Yoake.UI;

// Library, script list and directly editable style share one window. Input
// changes update a draft/preview; registry actions own persistent mutations.
public sealed class StylesWindow : Window
{
    private readonly SubtitleEditor _script;
    private readonly AssEvent? _currentLine;
    private readonly StyleLibraryStore _library;
    private readonly CommandRegistry _commands=new();
    private readonly ListBox _scriptList=new(),_libraryList=new();
    private readonly ComboBox _collections=new(){HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(4,1),MinHeight=24};
    private readonly TextBox _collectionName=new(){PlaceholderText="Collection name",Padding=new Thickness(4,1),MinHeight=24};
    private readonly ComboBox _replacement=new(){Padding=new Thickness(4,1),MinHeight=24,HorizontalAlignment=HorizontalAlignment.Stretch};
    private readonly StackPanel _fields=new(){Spacing=5};
    private readonly TextBlock _status=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _identity=new(){FontSize=12,FontWeight=FontWeight.SemiBold};
    private readonly TextBox _sample=new(){Text="Yoake 0123 日本語\\Nမြန်မာ",Padding=new Thickness(4,2),MinHeight=26};
    private readonly ComboBox _previewMode=new(){SelectedIndex=0,ItemsSource=new[]{"Sample text","Current subtitle line"},Padding=new Thickness(4,1),MinHeight=24};
    private StyleCollection? _collection;
    private AssStyle? _selected,_scriptSelected,_librarySelected;
    private SubtitleEditor? _target;
    private StyleDraft? _draft;
    private bool _refreshing,_applying,_applyOk=true;
    public StylePreviewControl Preview {get;}=new();
    public StylesWindow(SubtitleEditor editor,string? libraryPath=null,AssEvent? currentLine=null)
    {
        _script=editor;_currentLine=currentLine;
        _library=new(libraryPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Yoake","style-library.json"));
        if(_library.Collections.Count==0)_library.Create("Personal");
        Title="Styles Manager";Width=1180;Height=740;MinWidth=940;MinHeight=570;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new Grid{ColumnDefinitions=new("200,180,*"),RowDefinitions=new("*,Auto"),Margin=new Thickness(8),ColumnSpacing=8,RowSpacing=5};Content=root;
        var storage=new Grid{RowDefinitions=new("22,28,28,Auto,*,Auto"),RowSpacing=3};root.Children.Add(storage);
        storage.Children.Add(new TextBlock{Text="STYLE LIBRARY",FontWeight=FontWeight.SemiBold,FontSize=12});At(storage,_collections,1);At(storage,_collectionName,2);
        var catalogBar=new WrapPanel();At(storage,catalogBar,3);var libraryBar=new WrapPanel();At(storage,libraryBar,5);At(storage,_libraryList,4);
        var script=new Grid{RowDefinitions=new("22,Auto,*,Auto,20,28"),RowSpacing=3};Grid.SetColumn(script,1);root.Children.Add(script);
        script.Children.Add(new TextBlock{Text="CURRENT SCRIPT",FontWeight=FontWeight.SemiBold,FontSize=12});
        var copyBar=new WrapPanel();At(script,copyBar,1);At(script,_scriptList,2);var scriptBar=new WrapPanel();At(script,scriptBar,3);
        At(script,new TextBlock{Text="Delete: replace references with",FontSize=10},4);At(script,_replacement,5);
        var pane=new Grid{RowDefinitions=new("24,*,28,28,180"),RowSpacing=3};Grid.SetColumn(pane,2);root.Children.Add(pane);
        pane.Children.Add(_identity);At(pane,new ScrollViewer{Content=_fields,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},1);
        var editBar=new WrapPanel();At(pane,editBar,2);At(pane,Preview,4);
        var sampleRow=new Grid{ColumnDefinitions=new("150,*"),ColumnSpacing=5};sampleRow.Children.Add(_previewMode);Grid.SetColumn(_sample,1);sampleRow.Children.Add(_sample);At(pane,sampleRow,3);
        Grid.SetRow(_status,1);Grid.SetColumnSpan(_status,3);root.Children.Add(_status);
        _commands.CommandFailed+=(_,e)=>{_applyOk=false;_status.Text=e.Exception.Message;};
        Register("styles/apply","Apply",editBar,()=>{Apply();RefreshLists();});
        Register("styles/undo","Undo",editBar,()=>{if(!Commit())return;_target?.Undo.Undo();SaveLibrary();Reload();});
        Register("styles/redo","Redo",editBar,()=>{if(!Commit())return;_target?.Undo.Redo();SaveLibrary();Reload();});
        Register("styles/close","Close",editBar,()=>{if(Commit())Close();});
        Register("library/create","New",catalogBar,()=>{if(!Commit())return;_collection=_library.Create(_collectionName.Text??"");RefreshCollections();});
        Register("library/rename","Rename",catalogBar,()=>{if(_collection is null||!Commit())return;_library.Rename(_collection,_collectionName.Text??"");RefreshCollections();});
        RegisterAsync("library/delete","Delete",catalogBar,async()=>
        {
            if(_collection is null||!Commit()||!await ConfirmDeleteCollection())return;
            _library.Delete(_collection);_collection=_library.Collections.FirstOrDefault();_librarySelected=null;if(!ReferenceEquals(_target,_script)){_draft=null;_selected=null;_target=null;BuildFields();}RefreshCollections();
        });
        Register("styles/to-script","→ Script",copyBar,()=>{if(!Commit()||_librarySelected is null)return;var style=StyleLibraryStore.Copy(_librarySelected,_script);RefreshLists();_scriptList.SelectedItem=style.Name;});
        Register("styles/to-library","← Library",copyBar,()=>{if(!Commit()||_scriptSelected is null||_collection is null)return;var style=StyleLibraryStore.Copy(_scriptSelected,_library.Editor(_collection));_library.Save();RefreshLists();_libraryList.SelectedItem=style.Name;});
        AddOperations(scriptBar,false);AddOperations(libraryBar,true);
        _collections.SelectionChanged+=(_,_)=>{if(_refreshing)return;if(!Commit())return;_collection=_library.Collections.FirstOrDefault(c=>c.Name==_collections.SelectedItem as string);_librarySelected=null;if(!ReferenceEquals(_target,_script)){_draft=null;_selected=null;_target=null;BuildFields();}RefreshCollections();};
        _scriptList.SelectionChanged+=(_,_)=>Select(false);_libraryList.SelectionChanged+=(_,_)=>Select(true);
        _sample.TextChanged+=(_,_)=>RequestPreview();_previewMode.SelectionChanged+=(_,_)=>RequestPreview();
        Closing+=(_,e)=>{if(!Commit())e.Cancel=true;};Closed+=(_,_)=>Preview.Dispose();
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.KeyModifiers==KeyModifiers.Control&&e.Key is Key.Z or Key.Y){Invoke(e.Key==Key.Z?"styles/undo":"styles/redo");e.Handled=true;}
        });
        _collection=_library.Collections[0];RefreshCollections();_scriptList.SelectedItem=editor.Document.Styles.FirstOrDefault(s=>s.Name==currentLine?.Style)?.Name??editor.Document.Styles.FirstOrDefault()?.Name;
        Select(false);
    }
    private static void At(Grid grid,Control child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
    private void Register(string id,string label,Panel bar,Action action)=>RegisterAsync(id,label,bar,()=>{action();return Task.CompletedTask;});
    private void RegisterAsync(string id,string label,Panel bar,Func<Task> action)
    {
        _commands.Register(new AppCommand(new(id,label,label,"Styles"),async(_,_)=>await action()));
        bar.Children.Add(new Button{Content=label,Command=new RegistryCommand(_commands,id,()=>new()),Padding=new Thickness(5,1),MinHeight=24,Margin=new Thickness(1),FontSize=11});
    }
    private void Invoke(string id)
    {
        var pending=_commands.InvokeAsync(id,new());
        if(!pending.IsCompletedSuccessfully){_ = Observe(pending);return;}
        pending.GetAwaiter().GetResult();
    }
    private async Task Observe(ValueTask<bool> pending){try{await pending;}catch(Exception exception){_commands.ReportFailure("styles/action",exception);}}
    private bool Commit(){_applyOk=true;Invoke("styles/apply");return _applyOk;}
    private void Apply()
    {
        if(_applying||_draft is null||_target is null||!_draft.IsChanged)return;
        _applying=true;_applyOk=false;
        try{_draft.Apply(_target);SaveLibrary();_draft=new(_selected!);_applyOk=true;_status.Text="Style applied. Rename updates script references.";}
        finally{_applying=false;}
    }
    private void SaveLibrary(){if(_target is not null&&!ReferenceEquals(_target,_script))_library.Save();}
    private void AddOperations(Panel bar,bool library)
    {
        var prefix=library?"library/preset/":"script/style/";
        SubtitleEditor? Target()=>library?_collection is null?null:_library.Editor(_collection):_script;
        AssStyle? Selected()=>library?_librarySelected:_scriptSelected;
        Register(prefix+"new","New",bar,()=>{if(!Commit()||Target() is not {} editor)return;var added=editor.AddStyle();if(library)_library.Save();RefreshLists();(library?_libraryList:_scriptList).SelectedItem=added.Name;});
        Register(prefix+"duplicate","Duplicate",bar,()=>{if(!Commit()||Target() is not {} editor||Selected() is not {} selected)return;var added=editor.AddStyle(selected);if(library)_library.Save();RefreshLists();(library?_libraryList:_scriptList).SelectedItem=added.Name;});
        foreach(var direction in new[]{-1,1})
        {
            var delta=direction;Register(prefix+(delta<0?"up":"down"),delta<0?"Up":"Down",bar,()=>{if(!Commit()||Selected() is not {} selected||Target() is not {} editor)return;editor.MoveStyle(selected,delta);if(library)_library.Save();RefreshLists();});
        }
        Register(prefix+"delete","Delete",bar,()=>
        {
            if(!Commit()||Selected() is not {} selected)return;
            if(library){if(_collection is null)return;_library.DeletePreset(_collection,selected);_librarySelected=null;}
            else{_script.DeleteStyle(selected,_replacement.SelectedItem as string??"");_scriptSelected=null;}
            _selected=null;_draft=null;RefreshLists();(library?_libraryList:_scriptList).SelectedIndex=0;Select(library);
        });
        RegisterAsync(prefix+"import","Import ASS…",bar,async()=>
        {
            if(!Commit()||Target() is not {} target)return;
            var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Import styles from ASS",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("ASS subtitles"){Patterns=["*.ass","*.ssa"]}]});
            if(files.Count==0||!files[0].Path.IsFile)return;
            var imported=await Task.Run(()=>AssDocument.Load(files[0].Path.LocalPath));_library.Import(imported,target);if(library)_library.Save();RefreshLists();
            _status.Text=$"Imported {imported.Styles.Count} styles; duplicate names received a suffix.";
        });
    }
    private void RefreshCollections()
    {
        _refreshing=true;_collections.ItemsSource=_library.Collections.Select(c=>c.Name).ToArray();_collections.SelectedItem=_collection?.Name;_collectionName.Text=_collection?.Name??"";_refreshing=false;RefreshLists();
    }
    private void RefreshLists()
    {
        _refreshing=true;
        var scriptNames=_script.Document.Styles.Select(s=>s.Name).ToArray();_scriptList.ItemsSource=scriptNames;_scriptList.SelectedItem=_scriptSelected?.Name;
        _replacement.ItemsSource=scriptNames;_replacement.SelectedItem=scriptNames.FirstOrDefault(n=>n!=_scriptSelected?.Name);
        _libraryList.ItemsSource=_collection is null?Array.Empty<string>():_library.Editor(_collection).Document.Styles.Select(s=>s.Name).ToArray();_libraryList.SelectedItem=_librarySelected?.Name;
        _refreshing=false;
    }
    private void Select(bool library)
    {
        if(_refreshing)return;
        var target=library?_collection is null?null:_library.Editor(_collection):_script;
        var list=library?_libraryList:_scriptList;
        var next=target?.Document.Styles.FirstOrDefault(s=>s.Name==list.SelectedItem as string);
        if(next==_selected&&target==_target)return;
        if(!Commit()){_refreshing=true;list.SelectedItem=(library?_librarySelected:_scriptSelected)?.Name;_refreshing=false;return;}
        if(library)_librarySelected=next;else _scriptSelected=next;
        _selected=next;_target=target;_draft=next is null?null:new(next);BuildFields();RefreshLists();
    }
    private void Reload(){_draft=_selected is not null&&_target?.Document.Styles.Contains(_selected)==true?new(_selected):null;RefreshLists();BuildFields();}
    private void BuildFields()
    {
        _fields.Children.Clear();if(_draft is null){_identity.Text="Select a script style or library preset";return;}
        _identity.Text=(ReferenceEquals(_target,_script)?"Script style · ":"Library preset · ")+_selected!.Name;
        Text("Name","Name");Heading("Font");
        if(_draft.Has("Fontname")){var picker=new FontPicker{FontName=_draft.Get("Fontname")};picker.ValueChanged+=(_,_)=>Changed("Fontname",picker.FontName);FinishOnBlur(picker);_fields.Children.Add(picker);}
        Number("Fontsize","Size",0.1m,10000,1);
        var flags=new WrapPanel();foreach(var pair in new[]{("Bold","Bold"),("Italic","Italic"),("Underline","Underline"),("StrikeOut","Strikeout")})
        {
            if(!_draft.Has(pair.Item1))continue;var field=pair.Item1;var box=new CheckBox{Content=pair.Item2,IsChecked=_draft.Flag(field),FontSize=12,Margin=new Thickness(0,0,10,0)};
            box.Click+=(_,_)=>{Changed(field,box.IsChecked==true?"-1":"0");Invoke("styles/apply");};flags.Children.Add(box);
        }
        _fields.Children.Add(flags);Heading("Colors · exact ASS alpha / BGR retained");
        var colors=new Grid{ColumnDefinitions=new("*,*"),RowDefinitions=new("28,28"),ColumnSpacing=8,RowSpacing=3};var colorIndex=0;
        foreach(var pair in new[]{("PrimaryColour","Primary"),("SecondaryColour","Secondary"),("OutlineColour","Outline"),("BackColour","Shadow")})
        {
            if(!_draft.Has(pair.Item1))continue;var field=pair.Item1;var input=new AssColorField{Value=_draft.Get(field)};input.ValueChanged+=(_,_)=>Changed(field,input.Value);FinishOnBlur(input);
            var row=Row(pair.Item2,input,64);Grid.SetColumn(row,colorIndex%2);Grid.SetRow(row,colorIndex/2);colors.Children.Add(row);colorIndex++;
        }
        _fields.Children.Add(colors);Heading("Outline / shadow");
        if(_draft.Has("BorderStyle"))
        {
            var raw=_draft.Get("BorderStyle");var values=new[]{"1 · Outline","3 · Opaque box",raw+" · Source value"};
            var input=new ComboBox{ItemsSource=values,SelectedIndex=raw=="1"?0:raw=="3"?1:2,Padding=new Thickness(4,1),MinHeight=24,HorizontalAlignment=HorizontalAlignment.Stretch};
            input.SelectionChanged+=(_,_)=>{Changed("BorderStyle",input.SelectedIndex==0?"1":input.SelectedIndex==1?"3":raw);Invoke("styles/apply");};_fields.Children.Add(Row("Border",input));
        }
        Number("Outline","Outline",0,1000,0.1m);Number("Shadow","Shadow",0,1000,0.1m);
        Heading("Scale / transformation");Number("ScaleX","Scale X %",0.01m,100000,1);Number("ScaleY","Scale Y %",0.01m,100000,1);Number("Spacing","Spacing",-10000,10000,0.1m);Number("Angle","Rotation °",-360000,360000,1);
        Heading("Alignment / margins");
        if(_draft.Has("Alignment")){var alignment=new AssAlignmentPicker{Value=(int)Math.Clamp(_draft.Number("Alignment",2),1,9)};alignment.ValueChanged+=(_,_)=>{Changed("Alignment",alignment.Value.ToString(CultureInfo.InvariantCulture));Invoke("styles/apply");};_fields.Children.Add(Row("Anchor",alignment));}
        Number("MarginL","Left",0,int.MaxValue,1);Number("MarginR","Right",0,int.MaxValue,1);Number("MarginV","Vertical",0,int.MaxValue,1);Number("Encoding","Encoding",int.MinValue,int.MaxValue,1);
        var advanced=new StackPanel{Spacing=3};var standard=AssDocument.StyleFormat.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var name in _selected.FieldNames.Where(n=>!standard.Contains(n)))
        {
            var field=name;var input=new TextBox{Text=_draft.Get(field),Padding=new Thickness(4,1),MinHeight=24};input.TextChanged+=(_,_)=>Changed(field,input.Text??"");FinishOnBlur(input);advanced.Children.Add(Row(name,input));
        }
        _fields.Children.Add(new Expander{Header="Advanced / raw future fields",Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch});RequestPreview();
    }
    private static Grid Row(string label,Control input,int labelWidth=90)
    {
        var row=new Grid{ColumnDefinitions=new($"{labelWidth},*"),ColumnSpacing=5};row.Children.Add(new TextBlock{Text=label,FontSize=12,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(input,1);row.Children.Add(input);return row;
    }
    private void Heading(string text)=>_fields.Children.Add(new TextBlock{Text=text,FontWeight=FontWeight.SemiBold,FontSize=12,Margin=new Thickness(0,5,0,1)});
    private void Text(string field,string label)
    {
        if(_draft?.Has(field)!=true)return;var input=new TextBox{Text=_draft.Get(field),Padding=new Thickness(4,1),MinHeight=24};input.TextChanged+=(_,_)=>Changed(field,input.Text??"");FinishOnBlur(input);_fields.Children.Add(Row(label,input));
    }
    private void Number(string field,string label,decimal minimum,decimal maximum,decimal increment)
    {
        if(_draft?.Has(field)!=true)return;var value=_draft.Number(field);var input=new NumericUpDown{Minimum=Math.Min(minimum,value),Maximum=Math.Max(maximum,value),Value=value,Increment=increment,Padding=new Thickness(4,1),MinHeight=24,FormatString=increment<1?"0.##":"0"};
        input.ValueChanged+=(_,_)=>{if(input.Value is {} number)Changed(field,number.ToString(CultureInfo.InvariantCulture));};FinishOnBlur(input);_fields.Children.Add(Row(label,input));
    }
    private void FinishOnBlur(Control input)=>input.LostFocus+=(_,_)=>Invoke("styles/apply");
    private void Changed(string field,string value)
    {
        if(_draft is null||_applying)return;_draft.Set(field,value);RequestPreview();
    }
    private void RequestPreview()
    {
        if(_draft is null)return;
        try
        {
            var style=_draft.Preview();var useLine=_previewMode.SelectedIndex==1;
            var resX=useLine?_script.Document.GetScriptInfo("PlayResX"):"640";var resY=useLine?_script.Document.GetScriptInfo("PlayResY"):"180";
            var doc=AssDocument.Parse($"[Script Info]\nScriptType: v4.00+\nPlayResX: {resX}\nPlayResY: {resY}\n[V4+ Styles]\n");var editor=new SubtitleEditor(doc);
            foreach(var other in _script.Document.Styles.Where(s=>s.Name!=_selected!.Name))editor.AddStyle(other);
            var copied=editor.AddStyle(style);var line=editor.Insert(null,false);editor.SetField(line,"Style",copied.Name,"Preview");
            editor.SetField(line,"Text",useLine?_currentLine?.Text??_sample.Text??"":_sample.Text??"","Preview");Preview.Update(doc.Serialize());
        }
        catch(Exception exception){_status.Text="Draft: "+exception.Message;}
    }
    private async Task<bool> ConfirmDeleteCollection()
    {
        var dialog=new Window{Title="Delete style collection",Width=360,SizeToContent=SizeToContent.Height,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=8};panel.Children.Add(new TextBlock{Text=$"Delete collection '{_collection?.Name}' and its presets?",TextWrapping=TextWrapping.Wrap});var yes=new Button{Content="Delete collection"};yes.Click+=(_,_)=>dialog.Close(true);var no=new Button{Content="Cancel",IsCancel=true};no.Click+=(_,_)=>dialog.Close(false);panel.Children.Add(yes);panel.Children.Add(no);dialog.Content=panel;return await dialog.ShowDialog<bool>(this);
    }
}
