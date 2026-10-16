using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed partial class AudioTimingOptionsWindow : Window
{
    public AudioTimingOptionsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("CloseOptions")!.Click+=(_,_)=>Close();
    }
    public AudioTimingOptionsWindow(MainWindowViewModel model):this(){DataContext=model;}
}
