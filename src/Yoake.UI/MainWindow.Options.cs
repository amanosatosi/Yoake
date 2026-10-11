using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow
{
    public Task ShowLogAsync(string path)=>new LogWindow(path,error=>_model?.Registry.ReportFailure("app/log",error)).ShowDialog(this);
    public Task ShowOptionsAsync(MainWindowViewModel model)=>new OptionsWindow(model).ShowDialog(this);
}
