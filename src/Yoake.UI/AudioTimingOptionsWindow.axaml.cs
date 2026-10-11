using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public sealed partial class AudioTimingOptionsWindow : Window
{
    public AudioTimingOptionsWindow(MainWindowViewModel model)
    {
        AvaloniaXamlLoader.Load(this);DataContext=model;
        this.FindControl<Button>("CloseOptions")!.Click+=(_,_)=>Close();
    }
}
