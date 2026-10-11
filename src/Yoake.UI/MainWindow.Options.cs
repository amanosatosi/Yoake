using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow
{
    public Task ShowOptionsAsync(MainWindowViewModel model)=>new OptionsWindow(model).ShowDialog(this);
}
