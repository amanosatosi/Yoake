using Avalonia;
using Avalonia.Controls;

namespace Yoake.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;
            TitleTabStrip.Padding = new Thickness(0, 0, 140, 0);
        }
    }
}
