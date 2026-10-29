using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Yoake.Core.Commands;
using Yoake.Core.Logging;
using Yoake.UI.Commands;

namespace Yoake.UI;

public sealed class LogWindow : Window
{
    private readonly TextBox _text=new(){Name="ApplicationLog",IsReadOnly=true,AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.NoWrap};
    private readonly TextBlock _status=new(){Name="LogStatus"};
    private readonly CheckBox _follow=new(){Content="Auto refresh",IsChecked=true};
    private readonly CommandRegistry _registry=new();
    private readonly CancellationTokenSource _lifetime=new();
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly string _path;private bool _reading;

    public LogWindow(string path,Action<Exception> report)
    {
        _path=path;Name="ApplicationLogDialog";Title="Yoake log";Width=880;Height=550;MinWidth=500;MinHeight=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var names=new NameScope();NameScope.SetNameScope(this,names);names.Register(_text.Name!,_text);names.Register(_status.Name!,_status);
        var root=new Grid{Margin=new Thickness(10),RowDefinitions=new("Auto,*,Auto"),RowSpacing=8};Content=root;
        root.Children.Add(new TextBlock{Text=path,TextWrapping=Avalonia.Media.TextWrapping.Wrap});Grid.SetRow(_text,1);root.Children.Add(_text);
        var bottom=new DockPanel{LastChildFill=true};Grid.SetRow(bottom,2);root.Children.Add(bottom);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};DockPanel.SetDock(actions,Dock.Right);bottom.Children.Add(actions);bottom.Children.Add(_status);
        void Register(string id,string label,Func<Task> action,Func<bool>? enabled=null)
        {
            _registry.Register(new(new(id,label,label,"Log"),async(_,_)=>await action(),_=>enabled?.Invoke()??true));
            var button=new Button{Name=id.Replace('/','-'),Content=label,Command=new RegistryCommand(_registry,id,()=>new())};
            names.Register(button.Name!,button);actions.Children.Add(button);
        }
        Register("log/refresh","Refresh",ReadAsync,()=>!_reading&&!_lifetime.IsCancellationRequested);
        Register("log/copy","Copy",async()=>
        {
            if(Clipboard is null)throw new InvalidOperationException("Clipboard is unavailable.");
            var transfer=new DataTransfer();transfer.Add(DataTransferItem.CreateText(_text.Text??""));await Clipboard.SetDataAsync(transfer);await Clipboard.FlushAsync();
        });
        Register("log/close","Close",()=>{Close();return Task.CompletedTask;});actions.Children.Insert(0,_follow);
        _registry.CommandFailed+=(_,failure)=>{_status.Text=failure.Exception.Message;report(failure.Exception);};
        Opened+=async(_,_)=>{await _registry.InvokeAsync("log/refresh",new());_timer.Start();};
        _timer.Tick+=async(_,_)=>{if(_follow.IsChecked==true)await _registry.InvokeAsync("log/refresh",new());};
        Closed+=(_,_)=>{_timer.Stop();_lifetime.Cancel();};
        names.Complete();
    }
    private async Task ReadAsync()
    {
        _reading=true;_registry.NotifyStateChanged();
        try
        {
            var result=await LogTailReader.ReadAsync(_path,cancellationToken:_lifetime.Token);
            if(_lifetime.IsCancellationRequested)return;
            if(_text.Text!=result.Text)_text.Text=result.Text;
            _status.Text=!result.Exists?"Log file has not been created yet.":result.Truncated?"Latest complete lines (up to 1 MiB).":"Current application log.";
        }
        catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){}
        finally{_reading=false;_registry.NotifyStateChanged();}
    }
}
