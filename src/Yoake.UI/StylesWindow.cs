using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Yoake.Core.Commands;
using Yoake.Core.Subtitles;
using Yoake.UI.Commands;
using Yoake.UI.Controls;
using Yoake.UI.Icons;

namespace Yoake.UI;

// Library, script list and directly editable style share one window. Input
// changes update a draft/preview; registry actions own persistent mutations.
public sealed class StylesWindow : Window
{
    private readonly SubtitleEditor _script;
    private readonly AssEvent? _currentLine;
    private readonly StyleLibraryStore _library;
    private readonly string _recentColorsPath;
    private readonly CommandRegistry _commands=new();
    private readonly ListBox _scriptList=new(){Name="ScriptStyles",SelectionMode=SelectionMode.Multiple},_libraryList=new(){Name="LibraryStyles",SelectionMode=SelectionMode.Multiple};
    private readonly ComboBox _collections=new(){Name="StyleCollections",HorizontalAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(4,1),MinHeight=24};
    private readonly TextBox _collectionName=new(){Name="CollectionName",PlaceholderText="Collection name",Padding=new Thickness(4,1),MinHeight=24};
    private readonly StackPanel _fields=new(){Spacing=4};
    private Panel? _fieldTarget;
    private readonly TextBlock _status=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _identity=new(){FontSize=12,FontWeight=FontWeight.SemiBold};
    private readonly TextBox _sample=new(){Name="PreviewSample",Text="Yoake 0123\\N日本語 テスト\\Nမြန်မာ",Padding=new Thickness(4,2),MinHeight=26};
    private readonly ComboBox _previewMode=new(){SelectedIndex=0,ItemsSource=new[]{"Sample text","Current subtitle line"},Padding=new Thickness(4,1),MinHeight=24};
    private StyleCollection? _collection;
    private AssStyle? _scriptSelected,_librarySelected;
    private readonly StyleEditSession _session=new();
    private AssStyle? _selected=>_session.Style;
    private SubtitleEditor? _target=>_session.Editor;
    private StyleDraft? _draft=>_session.Draft;
    public CommandRegistry Registry=>_commands;
    private bool _refreshing,_applying,_applyOk=true;
    public StylePreviewControl Preview {get;}=new();
    public StylesWindow(SubtitleEditor editor,string? libraryPath=null,AssEvent? currentLine=null,double[]? splitWeights=null,Action<double[]>? saveLayout=null)
    {
        _script=editor;_currentLine=currentLine;
        var libraryFile=libraryPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Yoake","style-library.json");
        _library=new(libraryFile);_recentColorsPath=Path.Combine(Path.GetDirectoryName(libraryFile)!,"recent-colors.txt");
        if(_library.Collections.Count==0)_library.Create("Personal");
        FontSize=12;Title="Styles Manager";Width=1180;Height=740;MinWidth=940;MinHeight=570;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty,this.GetResourceObservable("AppBackgroundBrush"));
        var root=new Grid{ColumnDefinitions=new("1*,4,1*,4,3.4*"),RowDefinitions=new("*,Auto"),Margin=new Thickness(8),ColumnSpacing=5,RowSpacing=5};Content=root;
        if(splitWeights is {Length:3})for(var i=0;i<3;i++)root.ColumnDefinitions[i*2].Width=new GridLength(splitWeights[i],GridUnitType.Star);
        Closed+=(_,_)=>{try{saveLayout?.Invoke(new[]{root.ColumnDefinitions[0].ActualWidth,root.ColumnDefinitions[2].ActualWidth,root.ColumnDefinitions[4].ActualWidth}.Select(w=>w/200).ToArray());}catch(Exception e){_commands.ReportFailure("styles/layout",e);}};
        root.ColumnDefinitions[0].MinWidth=168;root.ColumnDefinitions[2].MinWidth=160;root.ColumnDefinitions[4].MinWidth=440;
        foreach(var column in new[]{1,3}){var splitter=new GridSplitter{ResizeDirection=GridResizeDirection.Columns,Background=new SolidColorBrush(Color.FromArgb(70,128,128,128))};Grid.SetColumn(splitter,column);root.Children.Add(splitter);}
        var storage=new Grid{RowDefinitions=new("22,28,Auto,*,Auto"),RowSpacing=3};root.Children.Add(storage);
        storage.Children.Add(new TextBlock{Text="STYLE LIBRARY",FontWeight=FontWeight.SemiBold,FontSize=12});At(storage,_collections,1);
        var collectionControls=new StackPanel{Spacing=3};collectionControls.Children.Add(_collectionName);
        var collectionEdit=new Expander{Header="Manage collections",Content=collectionControls,FontSize=11,Padding=new Thickness(0)};At(storage,collectionEdit,2);
        var catalogBar=new UniformGrid{Columns=3};collectionControls.Children.Add(catalogBar);var libraryBar=new UniformGrid{Columns=2};At(storage,libraryBar,4);At(storage,_libraryList,3);
        var script=new Grid{RowDefinitions=new("22,Auto,*,Auto"),RowSpacing=3};Grid.SetColumn(script,2);root.Children.Add(script);
        script.Children.Add(new TextBlock{Text="CURRENT SCRIPT",FontWeight=FontWeight.SemiBold,FontSize=12});
        var copyBar=new StackPanel{Spacing=3};At(script,copyBar,1);At(script,_scriptList,2);var scriptBar=new UniformGrid{Columns=2};At(script,scriptBar,3);
        var pane=new Grid{Name="StyleEditorPane",RowDefinitions=new("24,*,28,150"),RowSpacing=3};Grid.SetColumn(pane,4);root.Children.Add(pane);
        pane.Children.Add(_identity);At(pane,new ScrollViewer{Name="StyleProperties",Content=_fields,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled},1);
        var footer=new Grid{ColumnDefinitions=new("*,Auto")};Grid.SetRow(footer,1);Grid.SetColumnSpan(footer,5);root.Children.Add(footer);footer.Children.Add(_status);
        var editBar=new StackPanel{Orientation=Orientation.Horizontal,Spacing=3,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetColumn(editBar,1);footer.Children.Add(editBar);At(pane,Preview,3);
        var sampleRow=new Grid{ColumnDefinitions=new("150,*"),ColumnSpacing=5};sampleRow.Children.Add(_previewMode);Grid.SetColumn(_sample,1);sampleRow.Children.Add(_sample);At(pane,sampleRow,2);
        _commands.CommandFailed+=(_,e)=>{_applyOk=false;_status.Text=e.Exception.Message;};
        Register("styles/apply","Apply",editBar,()=>{var previousName=_selected?.Name;Apply();RefreshLists(previousName);});
        Register("styles/undo","Undo",editBar,()=>{if(!Commit())return;_target?.Undo.Undo();SaveLibrary();Reload();});
        Register("styles/redo","Redo",editBar,()=>{if(!Commit())return;_target?.Undo.Redo();SaveLibrary();Reload();});
        Register("styles/close","Close",editBar,()=>{if(Commit())Close();});
        Register("library/create","New",catalogBar,()=>{if(!Commit())return;_collection=_library.Create(_collectionName.Text??"");ResetLibrarySelection();RefreshCollections();});
        Register("library/rename","Rename",catalogBar,()=>{if(_collection is null||!Commit())return;_library.Rename(_collection,_collectionName.Text??"");RefreshCollections();});
        RegisterAsync("library/delete","Delete",catalogBar,async()=>
        {
            if(_collection is null||!Commit()||!await ConfirmDeleteCollection())return;
            _library.Delete(_collection);_collection=_library.Collections.FirstOrDefault();ResetLibrarySelection();RefreshCollections();
        });
        Register("styles/to-script","Library → Script",copyBar,()=>{if(!Commit())return;var styles=_script.CopyStyles(SelectedStyles(true));RefreshLists();SelectNames(_scriptList,styles);});
        Register("styles/to-library","Script → Library",copyBar,()=>{if(!Commit()||_collection is null)return;var styles=_library.Editor(_collection).CopyStyles(SelectedStyles(false));_library.Save();RefreshLists();SelectNames(_libraryList,styles);});
        AddOperations(scriptBar,false);AddOperations(libraryBar,true);
        _collections.SelectionChanged+=(_,_)=>
        {
            if(_refreshing)return;var next=_collections.SelectedItem as string;
            if(!Commit()){_refreshing=true;_collections.SelectedItem=_collection?.Name;_refreshing=false;return;}
            _collection=_library.Collections.FirstOrDefault(c=>c.Name==next);ResetLibrarySelection();RefreshCollections();
        };
        _scriptList.SelectionChanged+=(_,_)=>Select(false);_libraryList.SelectionChanged+=(_,_)=>Select(true);
        SelectOnFocus(_scriptList,false);SelectOnFocus(_libraryList,true);
        _sample.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)RequestPreview();};_previewMode.SelectionChanged+=(_,_)=>RequestPreview();
        Closing+=(_,e)=>{if(!Commit())e.Cancel=true;};Closed+=(_,_)=>Preview.Dispose();
        AddHandler(KeyDownEvent,(_,e)=>
        {
            if(e.KeyModifiers==KeyModifiers.Control&&e.Key is Key.Z or Key.Y){Invoke(e.Key==Key.Z?"styles/undo":"styles/redo");e.Handled=true;}
        });
        _collection=_library.Collections[0];RefreshCollections();_scriptList.SelectedItem=editor.Document.Styles.FirstOrDefault(s=>s.Name==currentLine?.Style)?.Name??editor.Document.Styles.FirstOrDefault()?.Name;
        Select(false);
    }
    private static void At(Grid grid,Control child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
    private void SelectOnFocus(ListBox list,bool library)
    {
        // Both lists retain a selection for copy commands. Returning to an
        // already selected row must still activate its inline editor.
        list.GotFocus+=(_,_)=>Dispatcher.UIThread.Post(()=>
        {
            if(list.IsKeyboardFocusWithin)Select(library);
        });
    }
    private void Register(string id,string label,Panel bar,Action action)=>RegisterAsync(id,label,bar,()=>{action();return Task.CompletedTask;});
    private void RegisterAsync(string id,string label,Panel bar,Func<Task> action)
    {
        _commands.Register(new AppCommand(new(id,label,label,"Styles"),async(_,_)=>await action()));
        var button=new Button{Content=label,Tag=id,HorizontalAlignment=HorizontalAlignment.Stretch,Command=new RegistryCommand(_commands,id,()=>new()),Padding=new Thickness(5,1),MinHeight=24,Margin=new Thickness(1),FontSize=11};
        var geometry=id.Split('/').Last() switch{"up"=>IconGeometries.StyleUp,"down"=>IconGeometries.StyleDown,"top"=>IconGeometries.StyleTop,"bottom"=>IconGeometries.StyleBottom,"sort"=>IconGeometries.StyleSort,_=>null};
        if(geometry is not null){var icon=new PathIcon{Width=14,Height=14,Data=geometry};icon.Bind(PathIcon.ForegroundProperty,this.GetResourceObservable("IconForegroundBrush"));var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};content.Children.Add(icon);content.Children.Add(new TextBlock{Text=label});button.Content=content;}
        ToolTip.SetTip(button,id=="styles/to-script"?"Copy the selected library preset into the current script; duplicate names get a suffix.":id=="styles/to-library"?"Copy the selected script style into this library collection; duplicate names get a suffix.":label);bar.Children.Add(button);
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
        if(_applying||_draft is null||_target is null)return;
        if(!_draft.IsChanged){SaveLibrary();return;}
        _applying=true;_applyOk=false;
        try{_session.Commit();SaveLibrary();_applyOk=true;_identity.Text=(ReferenceEquals(_target,_script)?"Script style · ":"Library preset · ")+_selected!.Name;_status.Text="Style applied. Rename updates script references.";}
        finally{_applying=false;}
    }
    private void SaveLibrary(){if(_target is not null&&!ReferenceEquals(_target,_script))_library.Save();}
    private AssStyle[] SelectedStyles(bool library)
    {
        var names=(library?_libraryList:_scriptList).SelectedItems?.OfType<string>().ToHashSet()??[];
        var editor=library?_collection is null?null:_library.Editor(_collection):_script;
        return editor?.Document.Styles.Where(s=>names.Contains(s.Name)).ToArray()??[];
    }
    private void SelectNames(ListBox list,IEnumerable<AssStyle> styles)
    {
        // Replace the whole selection before activating its editor. Intermediate
        // selection notifications must not commit/rebuild fields between rows.
        _refreshing=true;
        try{list.SelectedItems?.Clear();foreach(var style in styles)list.SelectedItems?.Add(style.Name);}
        finally{_refreshing=false;}
        Select(ReferenceEquals(list,_libraryList));
    }
    private void AddOperations(Panel bar,bool library)
    {
        var prefix=library?"library/preset/":"script/style/";
        SubtitleEditor? Target()=>library?_collection is null?null:_library.Editor(_collection):_script;
        Register(prefix+"new","New",bar,()=>{if(!Commit()||Target() is not {} editor)return;var added=editor.AddStyle();if(library)_library.Save();RefreshLists();SelectNames(library?_libraryList:_scriptList,[added]);});
        Register(prefix+"duplicate","Duplicate",bar,()=>{if(!Commit()||Target() is not {} editor)return;var added=editor.CopyStyles(SelectedStyles(library));if(library)_library.Save();RefreshLists();SelectNames(library?_libraryList:_scriptList,added);});
        foreach(var order in Enum.GetValues<StyleOrder>())
        {
            var action=order;Register(prefix+action.ToString().ToLowerInvariant(),action.ToString(),bar,()=>{if(!Commit()||Target() is not {} editor)return;editor.ReorderStyles(SelectedStyles(library),action);if(library)_library.Save();RefreshLists();});
        }
        RegisterAsync(prefix+"copy","Copy",bar,async()=>{if(!Commit()||Target() is not {} editor||Clipboard is null)return;var data=new DataTransfer();data.Add(DataTransferItem.CreateText(editor.CopyStyleText(SelectedStyles(library))));await Clipboard.SetDataAsync(data);await Clipboard.FlushAsync();});
        RegisterAsync(prefix+"paste","Paste",bar,async()=>{if(!Commit()||Target() is not {} editor||Clipboard is null)return;using var data=await Clipboard.TryGetDataAsync();var text=data is null?null:await data.TryGetTextAsync();if(string.IsNullOrWhiteSpace(text))return;var added=editor.PasteStyles(text);if(library)_library.Save();RefreshLists();SelectNames(library?_libraryList:_scriptList,added);});
        RegisterAsync(prefix+"delete","Delete",bar,async()=>
        {
            if(!Commit()||Target() is not {} editor)return;var selected=SelectedStyles(library);if(selected.Length==0)return;
            string? replacement=null;
            if(!library&&editor.Document.Events.Any(e=>selected.Any(s=>e.Style==s.Name||AssStyleReferences.Uses(e.Text,s.Name))))
            {replacement=await ChooseReplacement(selected);if(replacement is null)return;}
            editor.DeleteStyles(selected,replacement);if(library){_library.Save();_librarySelected=null;}else _scriptSelected=null;
            _session.Select(null,null);RefreshLists();(library?_libraryList:_scriptList).SelectedIndex=0;Select(library);
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
    private async Task<string?> ChooseReplacement(AssStyle[] deleting)
    {
        var choices=_script.Document.Styles.Except(deleting).Select(s=>s.Name).ToArray();if(choices.Length==0)throw new InvalidOperationException("Referenced styles require a surviving replacement.");
        var dialog=new Window{Title="Replace style references",Width=360,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(12),Spacing=6};panel.Children.Add(new TextBlock{Text="Replace event styles and explicit \\r resets with:",TextWrapping=TextWrapping.Wrap});var choice=new ComboBox{ItemsSource=choices,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(choice);
        var ok=new Button{Content="Replace and delete",IsDefault=true};ok.Click+=(_,_)=>dialog.Close(choice.SelectedItem as string);panel.Children.Add(ok);var cancel=new Button{Content="Cancel",IsCancel=true};cancel.Click+=(_,_)=>dialog.Close();panel.Children.Add(cancel);dialog.Content=panel;return await dialog.ShowDialog<string?>(this);
    }
    private void RefreshCollections()
    {
        _refreshing=true;_collections.ItemsSource=_library.Collections.Select(c=>c.Name).ToArray();_collections.SelectedItem=_collection?.Name;_collectionName.Text=_collection?.Name??"";_refreshing=false;RefreshLists();
    }
    private void RefreshLists(string? previousName=null)
    {
        _refreshing=true;
        var selectedScript=_scriptList.SelectedItems?.OfType<string>().ToArray()??[];var selectedLibrary=_libraryList.SelectedItems?.OfType<string>().ToArray()??[];
        var scriptNames=_script.Document.Styles.Select(s=>s.Name).ToArray();_scriptList.ItemsSource=scriptNames;
        RestoreSelection(_scriptList,scriptNames,selectedScript,ReferenceEquals(_target,_script)?previousName:null,_selected?.Name);
        var libraryNames=_collection is null?Array.Empty<string>():_library.Editor(_collection).Document.Styles.Select(s=>s.Name).ToArray();_libraryList.ItemsSource=libraryNames;RestoreSelection(_libraryList,libraryNames,selectedLibrary,!ReferenceEquals(_target,_script)?previousName:null,_selected?.Name);
        _refreshing=false;
    }
    private static void RestoreSelection(ListBox list,string[] names,string[] selected,string? previousName,string? currentName)
    {
        list.SelectedItems?.Clear();
        foreach(var oldName in selected)
        {
            var name=oldName==previousName?currentName:oldName;
            if(name is not null&&names.Contains(name))list.SelectedItems?.Add(name);
        }
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
        _session.Select(target,next);BuildFields();RefreshLists();
    }
    private void Reload(){_session.Reload();RefreshLists();BuildFields();}
    private void ResetLibrarySelection()
    {
        _librarySelected=null;
        if(!ReferenceEquals(_target,_script)){_session.Select(null,null);BuildFields();}
    }
    private void BuildFields()
    {
        _fields.Children.Clear();if(_draft is null){_identity.Text="Select a script style or library preset";Preview.Clear();return;}
        _identity.Text=(ReferenceEquals(_target,_script)?"Script style · ":"Library preset · ")+_selected!.Name;
        _fieldTarget=_fields;
        Text("Name","Identity");Heading("Font");
        var fontRow=new Grid{ColumnDefinitions=new("*,96"),ColumnSpacing=5};_fields.Children.Add(fontRow);
        if(_draft.Has("Fontname")){var picker=new FontPicker{Name="StyleFont",FontName=_draft.Get("Fontname")};picker.ValueChanged+=(_,_)=>Changed("Fontname",picker.FontName);FinishOnBlur(picker);fontRow.Children.Add(picker);}
        var sizeGroup=new StackPanel();Grid.SetColumn(sizeGroup,1);fontRow.Children.Add(sizeGroup);Number("Fontsize","",0.1m,10000,1,sizeGroup);
        var flags=new WrapPanel();foreach(var pair in new[]{("Bold","Bold"),("Italic","Italic"),("Underline","Underline"),("StrikeOut","Strikeout")})
        {
            if(!_draft.Has(pair.Item1))continue;var field=pair.Item1;var box=new CheckBox{Content=pair.Item2,IsChecked=_draft.Flag(field),FontSize=12,MinHeight=22,Margin=new Thickness(0,0,8,0)};
            box.Click+=(_,_)=>{Changed(field,box.IsChecked==true?"-1":"0");Invoke("styles/apply");};flags.Children.Add(box);
        }
        _fields.Children.Add(flags);Heading("Colors");
        var colors=new UniformGrid{Columns=2,Rows=2};_fields.Children.Add(colors);
        foreach(var pair in new[]{("PrimaryColour","Primary"),("SecondaryColour","Secondary"),("OutlineColour","Outline"),("BackColour","Shadow")})
        {
            if(!_draft.Has(pair.Item1))continue;var field=pair.Item1;var input=new AssColorField{Name="Style"+field,Value=_draft.Get(field),RecentColorsPath=_recentColorsPath,ReportFailure=e=>_commands.ReportFailure("styles/color",e)};input.ValueChanged+=(_,_)=>Changed(field,input.Value);input.ValueCommitted+=(_,_)=>Invoke("styles/apply");FinishOnBlur(input);
            var row=Row(pair.Item2,input,58);row.Margin=new Thickness(0,0,6,2);colors.Children.Add(row);
        }
        var transforms=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8};_fields.Children.Add(transforms);
        var outline=new StackPanel{Spacing=2};transforms.Children.Add(outline);_fieldTarget=outline;Heading("Outline / shadow");
        if(_draft.Has("BorderStyle"))
        {
            var raw=_draft.Get("BorderStyle");var values=raw is "1" or "3"?new[]{"1 · Outline","3 · Opaque box"}:new[]{"1 · Outline","3 · Opaque box",raw+" · Custom source value"};
            var input=new ComboBox{ItemsSource=values,SelectedIndex=raw=="1"?0:raw=="3"?1:2,Padding=new Thickness(4,1),MinHeight=24,HorizontalAlignment=HorizontalAlignment.Stretch};
            input.SelectionChanged+=(_,_)=>{Changed("BorderStyle",input.SelectedIndex==0?"1":input.SelectedIndex==1?"3":raw);Invoke("styles/apply");};outline.Children.Add(Row("Border",input,48));
        }
        Numbers(("Outline","Outline",0,1000,0.1m),("Shadow","Shadow",0,1000,0.1m));
        var scale=new StackPanel{Spacing=2};Grid.SetColumn(scale,1);transforms.Children.Add(scale);_fieldTarget=scale;Heading("Scale / transformation");
        Numbers(("ScaleX","X %",0.01m,100000,1),("ScaleY","Y %",0.01m,100000,1),("Spacing","Spacing",-10000,10000,0.1m),("Angle","Angle",-360000,360000,1));
        _fieldTarget=_fields;Heading("Alignment / margins / encoding");
        var placement=new Grid{ColumnDefinitions=new("118,*"),ColumnSpacing=8};_fields.Children.Add(placement);
        if(_draft.Has("Alignment")){var alignment=new AssAlignmentPicker{Name="StyleAlignment",Value=(int)Math.Clamp(_draft.Number("Alignment",2),1,9)};alignment.ValueChanged+=(_,_)=>{Changed("Alignment",alignment.Value.ToString(CultureInfo.InvariantCulture));Invoke("styles/apply");};placement.Children.Add(alignment);}
        var margins=new StackPanel{Spacing=2};Grid.SetColumn(margins,1);placement.Children.Add(margins);_fieldTarget=margins;
        Numbers(("MarginL","Left",0,int.MaxValue,1),("MarginR","Right",0,int.MaxValue,1),("MarginV","Vertical",0,int.MaxValue,1),("Encoding","Encoding",int.MinValue,int.MaxValue,1));
        _fieldTarget=_fields;
        var advanced=new StackPanel{Spacing=3};var standard=AssDocument.StyleFormat.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var name in _selected.FieldNames.Where(n=>!standard.Contains(n)))
        {
            var field=name;var input=new TextBox{Name="Style"+field,Text=_draft.Get(field),Padding=new Thickness(4,1),MinHeight=24};input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)Changed(field,input.Text??"");};FinishOnBlur(input);advanced.Children.Add(Row(name,input));
        }
        _fields.Children.Add(new Expander{Header="Advanced / raw future fields",Content=advanced,HorizontalAlignment=HorizontalAlignment.Stretch});RequestPreview();
    }
    private static Grid Row(string label,Control input,int labelWidth=90)
    {
        var row=new Grid{ColumnDefinitions=new($"{labelWidth},*"),ColumnSpacing=5};row.Children.Add(new TextBlock{Text=label,FontSize=12,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(input,1);row.Children.Add(input);return row;
    }
    private void Heading(string text)=>(_fieldTarget??_fields).Children.Add(new TextBlock{Text=text,FontWeight=FontWeight.SemiBold,FontSize=12,Margin=new Thickness(0,5,0,1)});
    private void Text(string field,string label)
    {
        if(_draft?.Has(field)!=true)return;var input=new TextBox{Name="Style"+field,Text=_draft.Get(field),Padding=new Thickness(4,1),MinHeight=24};input.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty)Changed(field,input.Text??"");};FinishOnBlur(input);_fields.Children.Add(Row(label,input));
    }
    private void Numbers(params (string Field,string Label,decimal Minimum,decimal Maximum,decimal Increment)[] values)
    {
        var group=new WrapPanel();(_fieldTarget??_fields).Children.Add(group);
        foreach(var value in values)Number(value.Field,value.Label,value.Minimum,value.Maximum,value.Increment,group);
    }
    private void Number(string field,string label,decimal minimum,decimal maximum,decimal increment,Panel? parent=null)
    {
        if(_draft?.Has(field)!=true)return;var value=_draft.Number(field);var input=new NumericUpDown{Name="Style"+field,Minimum=Math.Min(minimum,value),Maximum=Math.Max(maximum,value),Value=value,Increment=increment,Padding=new Thickness(4,1),MinHeight=24,FormatString=increment<1?"0.##":"0"};
        input.ValueChanged+=(_,_)=>{if(input.Value is {} number)Changed(field,number.ToString(CultureInfo.InvariantCulture));};FinishOnBlur(input);
        input.Width=90;input.HorizontalAlignment=HorizontalAlignment.Left;
        var row=Row(label,input,string.IsNullOrEmpty(label)?0:parent is null?90:48);row.Margin=new Thickness(0,0,8,2);
        if(parent is not null)row.Width=string.IsNullOrEmpty(label)?96:145;
        (parent??_fieldTarget??_fields).Children.Add(row);
    }
    private void FinishOnBlur(Control input)
    {
        var style=_selected;
        input.LostFocus+=(_,_)=>Dispatcher.UIThread.Post(()=>
        {
            if(ReferenceEquals(style,_selected)&&!input.IsKeyboardFocusWithin)Invoke("styles/apply");
        });
    }
    private void Changed(string field,string value)
    {
        if(_draft is null||_applying)return;_draft.Set(field,value);_identity.Text=(ReferenceEquals(_target,_script)?"Script style - ":"Library preset - ")+_selected!.Name+(_draft.IsChanged?" (unapplied)":"");RequestPreview();
    }
    private void RequestPreview()
    {
        if(_draft is null)return;
        try
        {
            var style=_draft.Preview();var useLine=_previewMode.SelectedIndex==1;
            var resX=useLine?_script.Document.GetScriptInfo("PlayResX"):StylePreviewControl.WidthPixels.ToString(CultureInfo.InvariantCulture);var resY=useLine?_script.Document.GetScriptInfo("PlayResY"):StylePreviewControl.HeightPixels.ToString(CultureInfo.InvariantCulture);
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
