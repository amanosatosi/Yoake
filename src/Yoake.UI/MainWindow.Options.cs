using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow
{
    public Task ShowAssistantAsync(MainWindowViewModel model,Guid owner,EditorAssistantMode mode,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(model.ActiveDocumentId!=owner)return Task.CompletedTask;
        var existing=OwnedWindows.OfType<EditorAssistantWindow>().FirstOrDefault(w=>w.Mode==mode);
        if(existing is not null)existing.Activate();else new EditorAssistantWindow(model,mode).Show(this);
        return Task.CompletedTask;
    }
    public Task ShowLogAsync(string path)=>new LogWindow(path,error=>_model?.Registry.ReportFailure("app/log",error)).ShowDialog(this);
    public Task ShowOptionsAsync(MainWindowViewModel model)=>new OptionsWindow(model).ShowDialog(this);
}
