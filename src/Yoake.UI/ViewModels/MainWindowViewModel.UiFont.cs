namespace Yoake.UI.ViewModels;

public sealed record UiFontOptions(string? Family, double? Size);

public sealed partial class MainWindowViewModel
{
    public string? UiFontFamily => _settings.UiFontFamily;
    public double? UiFontSize => _settings.UiFontSize;

    private void RegisterUiFontCommands()
    {
        _registry.Register(new(new("settings/ui-font", "Application font", "Set interface typography", "Application"), (invocation, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (invocation.Parameter is not UiFontOptions options || options.Size is {} size && (!double.IsFinite(size) || size < 9 || size > 24))
                throw new ArgumentException("Font size must be between 9 and 24.");
            var settings = (_settings with { UiFontFamily = options.Family, UiFontSize = options.Size }).Normalize();
            _settingsStore.Save(settings);
            _settings = settings;
            UiFontResources.Apply(settings.UiFontFamily, settings.UiFontSize);
            OnPropertyChanged(nameof(UiFontFamily));
            OnPropertyChanged(nameof(UiFontSize));
            return ValueTask.CompletedTask;
        }));
    }
}
