using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Yoake.Core.Subtitles;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

// Real path-entry/browse workflow also permits keyboard-only and Unicode paths.
public sealed class AttachmentPathWindow : Window
{
    public AttachmentPathWindow(bool importing)
    {
        Name=importing?"AttachmentImportDialog":"AttachmentExtractDialog";Title=importing?"Import attachments":"Extract attachments";
        Width=600;MinWidth=450;CanResize=true;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);var panel=new StackPanel{Margin=new Thickness(12),Spacing=8};Content=panel;
        var kind=new ComboBox{Name="AttachmentImportKind",ItemsSource=Enum.GetValues<AssAttachmentKind>(),SelectedItem=AssAttachmentKind.Font,MinWidth=150};
        if(importing){panel.Children.Add(new TextBlock{Text="Attachment type"});panel.Children.Add(kind);}
        panel.Children.Add(new TextBlock{Text=importing?"Files (one full path per line)":"Destination directory"});
        var paths=new TextBox{Name="AttachmentPaths",AcceptsReturn=importing,MinHeight=importing?90:28,TextWrapping=Avalonia.Media.TextWrapping.NoWrap};panel.Children.Add(paths);
        var status=new TextBlock{Name="AttachmentPathStatus",TextWrapping=Avalonia.Media.TextWrapping.Wrap};panel.Children.Add(status);
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(row);
        var browse=new Button{Name="AttachmentBrowse",Content="Browse…"};row.Children.Add(browse);
        var confirm=new Button{Name="AttachmentPathConfirm",Content=importing?"Import":"Extract",IsDefault=true};row.Children.Add(confirm);
        var cancel=new Button{Name="AttachmentPathCancel",Content="Cancel",IsCancel=true};row.Children.Add(cancel);cancel.Click+=(_,_)=>Close();
        foreach(var control in new Control[]{kind,paths,status,browse,confirm,cancel})names.Register(control.Name!,control);names.Complete();
        browse.Click+=async(_,_)=>
        {
            try
            {
                if(importing)
                {
                    var font=kind.SelectedItem is AssAttachmentKind.Font;
                    var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose attachments",AllowMultiple=true,
                        FileTypeFilter=[new FilePickerFileType(font?"Fonts":"Graphics"){Patterns=font?["*.ttf","*.otf","*.ttc","*.otc","*.pfb"]:["*.bmp","*.gif","*.png","*.jpg","*.jpeg","*.ico","*.wmf"]},FilePickerFileTypes.All]});
                    if(files.Count>0)paths.Text=string.Join(Environment.NewLine,files.Select(f=>f.Path.LocalPath));
                }
                else
                {
                    var folders=await StorageProvider.OpenFolderPickerAsync(new(){Title="Choose extraction directory",AllowMultiple=false});
                    if(folders.Count>0)paths.Text=folders[0].Path.LocalPath;
                }
            }
            catch(Exception error){status.Text=error.Message;}
        };
        confirm.Click+=(_,_)=>
        {
            try
            {
                if(importing)
                {
                    var files=(paths.Text??"").Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
                    if(files.Length is <1 or >128||files.Any(p=>!Path.IsPathFullyQualified(p)||!File.Exists(p)))throw new InvalidDataException("Choose existing files using full paths (maximum 128).");
                    Close(new AttachmentImportRequest((AssAttachmentKind)(kind.SelectedItem??AssAttachmentKind.Font),files));
                }
                else
                {
                    var directory=paths.Text??"";
                    if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))throw new InvalidDataException("Choose an existing destination directory using its full path.");
                    Close(Path.GetFullPath(directory));
                }
            }
            catch(Exception error){status.Text=error.Message;}
        };
        AddHandler(InputElement.KeyDownEvent,(_,args)=>
        {
            if(args.Key is Key.Enter or Key.Escape&&paths.GetVisualDescendants().OfType<TextPresenter>().Any(p=>!string.IsNullOrEmpty(p.PreeditText)))args.Handled=true;
        },RoutingStrategies.Tunnel);
        Opened+=(_,_)=>paths.Focus();
    }
}
