using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yoake.Core.Fonts;
using Yoake.UI.Commands;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed class FontCollectorWindow:Window
{
    private readonly MainWindowViewModel _model;private readonly Guid _document;
    private readonly ComboBox _mode=new(){Name="FontCollectorMode"};
    private readonly TextBox _destination=new(){Name="FontCollectorDestination",PlaceholderText="Choose a destination parent folder or ZIP filename"};
    private readonly CheckBox _unused=new(){Name="FontCollectorUnused",Content="Include unused style fonts"};
    private readonly CheckBox _rights=new(){Name="FontCollectorRights",Content="I have checked redistribution permission for the fonts I want to copy"};
    private readonly Button _browse=new(){Name="FontCollectorBrowse",Content="Browse…"};
    private readonly ListBox _fonts=new(){Name="FontCollectorFonts"};
    private readonly TextBox _log=new(){Name="FontCollectorLog",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _status=new(){Name="FontCollectorStatus",TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar _progress=new(){Name="FontCollectorProgress",Minimum=0,Maximum=1,Height=5};
    private readonly ComboBoxItem _script;
    private bool _closed,_changing;
    public FontCollectorWindow(MainWindowViewModel model,Guid document)
    {
        _model=model;_document=document;Name="FontCollectorDialog";Title="Fonts Collector";Width=860;Height=610;MinWidth=650;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);
        foreach(var control in new Control[]{_mode,_destination,_unused,_rights,_browse,_fonts,_log,_status,_progress})names.Register(control.Name!,control);
        ComboBoxItem Choice(FontCollectionMode mode,string label)=>new(){Content=label,Tag=mode};
        _script=Choice(FontCollectionMode.ScriptFolder,"Copy to script folder (fonts / fonts-2…) ");
        _mode.ItemsSource=new[]{Choice(FontCollectionMode.Check,"Check fonts only"),Choice(FontCollectionMode.Folder,"Copy to folder (complete fonts / fonts-2… batch)"),_script,Choice(FontCollectionMode.Zip,"Copy to ZIP")};_mode.SelectedIndex=0;
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new("Auto,Auto,Auto,*,Auto,Auto"),RowSpacing=8};Content=root;
        var actions=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};root.Children.Add(actions);actions.Children.Add(_mode);Grid.SetColumn(_unused,1);actions.Children.Add(_unused);
        var destination=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};Grid.SetRow(destination,1);root.Children.Add(destination);destination.Children.Add(_destination);Grid.SetColumn(_browse,1);destination.Children.Add(_browse);
        var notice=new StackPanel{Spacing=4};Grid.SetRow(notice,2);root.Children.Add(notice);notice.Children.Add(new TextBlock{Text=FontCollection.LicenseNotice,TextWrapping=TextWrapping.Wrap,FontSize=11});notice.Children.Add(_rights);
        var report=new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=10};Grid.SetRow(report,3);root.Children.Add(report);report.Children.Add(_fonts);Grid.SetColumn(_log,1);report.Children.Add(_log);
        _fonts.ItemTemplate=new FuncDataTemplate<ResolvedFont>((font,scope)=>{var row=new TextBlock{Text=font?.ToString(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3)};if(font?.Match!=FontMatch.Exact)row.Foreground=Brushes.OrangeRed;return row;});
        var status=new StackPanel{Spacing=5};Grid.SetRow(status,4);root.Children.Add(status);status.Children.Add(_progress);status.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};Grid.SetRow(buttons,5);root.Children.Add(buttons);
        var start=new Button{Name="FontCollectorStart",Content="Start",IsDefault=true,Command=new RegistryCommand(model.Registry,"fonts/start",()=>new(_document),parameterFactory:Request)};
        var cancel=new Button{Name="FontCollectorCancel",Content="Cancel work",Command=new RegistryCommand(model.Registry,"fonts/cancel",()=>new(_document))};
        var close=new Button{Name="FontCollectorClose",Content="Close",IsCancel=true,Command=new RegistryCommand(model.Registry,"fonts/close",()=>new(_document))};
        foreach(var button in new[]{start,cancel,close}){names.Register(button.Name!,button);buttons.Children.Add(button);}
        _mode.SelectionChanged+=(sender,args)=>UpdateInputs();_browse.Click+=Browse;
        model.PropertyChanged+=ModelChanged;model.FontCollectorCloseRequested+=CloseRequested;model.Registry.CommandFailed+=Failure;
        AddHandler(InputElement.KeyDownEvent,(sender,args)=>
        {
            if(args.Key is not (Key.Enter or Key.Escape))return;var source=args.Source as Control;var ancestors=source?.GetVisualAncestors().OfType<Control>().Prepend(source).ToArray()??[];
            if(ancestors.OfType<TextBox>().Any(input=>input.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText))))args.Handled=true;
            if(args.Key==Key.Enter&&ancestors.OfType<ComboBox>().Any(combo=>combo.IsDropDownOpen))args.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(sender,args)=>start.Focus();Closed+=(sender,args)=>{_closed=true;model.PropertyChanged-=ModelChanged;model.FontCollectorCloseRequested-=CloseRequested;model.Registry.CommandFailed-=Failure;_=model.Registry.InvokeAsync("fonts/close",new(_document));};
        Refresh();names.Complete();
    }
    private FontCollectionMode Mode=>(_mode.SelectedItem as ComboBoxItem)?.Tag is FontCollectionMode mode?mode:FontCollectionMode.Check;
    private object Request()=>new FontCollectorRequest(Mode,_destination.Text??"",_unused.IsChecked==true,_rights.IsChecked==true);
    private async void Browse(object? sender,RoutedEventArgs args)
    {
        try
        {
            if(Mode==FontCollectionMode.Zip)
            {var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Choose font ZIP destination",SuggestedFileName="fonts.zip",DefaultExtension="zip",ShowOverwritePrompt=false,FileTypeChoices=[new FilePickerFileType("ZIP archive"){Patterns=["*.zip"]}]});if(file is not null)_destination.Text=file.TryGetLocalPath();}
            else{var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Choose font collection parent folder",AllowMultiple=false});if(folders.FirstOrDefault() is {} folder)_destination.Text=folder.TryGetLocalPath();}
        }
        catch(Exception error){_model.Registry.ReportFailure("fonts/browse",error);}
    }
    private void Refresh()
    {
        if(_changing||_closed)return;_changing=true;
        try
        {
            UpdateInputs();
            _fonts.ItemsSource=_model.FontReport?.Fonts;_log.Text=_model.FontCollectorLog;_status.Text=_model.FontCollectorStatus;_progress.Value=_model.FontCollectorProgress;
        }
        finally{_changing=false;}
    }
    private void UpdateInputs()
    {var busy=_model.FontCollectorBusy;_mode.IsEnabled=_unused.IsEnabled=_rights.IsEnabled=!busy;_script.IsEnabled=_model.ActiveSubtitlePath is not null;_destination.IsEnabled=_browse.IsEnabled=!busy&&Mode is FontCollectionMode.Folder or FontCollectionMode.Zip;}
    private void ModelChanged(object? sender,PropertyChangedEventArgs args)
    {
        if(!Dispatcher.UIThread.CheckAccess()){Dispatcher.UIThread.Post(()=>ModelChanged(sender,args));return;}
        if(_closed)return;if(_model.ActiveDocumentId!=_document){Close();return;}
        switch(args.PropertyName)
        {
            case nameof(MainWindowViewModel.FontCollectorBusy):case nameof(MainWindowViewModel.ActiveSubtitlePath):UpdateInputs();break;
            case nameof(MainWindowViewModel.FontReport):_fonts.ItemsSource=_model.FontReport?.Fonts;break;
            case nameof(MainWindowViewModel.FontCollectorStatus):_status.Text=_model.FontCollectorStatus;break;
            case nameof(MainWindowViewModel.FontCollectorLog):_log.Text=_model.FontCollectorLog;break;
            case nameof(MainWindowViewModel.FontCollectorProgress):_progress.Value=_model.FontCollectorProgress;break;
        }
    }
    private void Failure(object? sender,Yoake.Core.Commands.CommandFailureEventArgs failure)
    {if(!_closed&&failure.CommandId.StartsWith("fonts/",StringComparison.Ordinal))_status.Text=failure.Exception.Message;}
    private void CloseRequested(object? sender,FontCollectorCloseEventArgs args){if(!_closed&&args.Document==_document)Close();}
}
