using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Yoake.Core.Workspace;

namespace Yoake.UI.ViewModels;

public sealed class DocumentTabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DocumentSession _document;
    private bool _isActive;

    public DocumentTabViewModel(
        DocumentSession document,
        bool isActive,
        ICommand activateCommand,
        ICommand closeCommand)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _isActive = isActive;
        ActivateCommand = activateCommand ?? throw new ArgumentNullException(nameof(activateCommand));
        CloseCommand = closeCommand ?? throw new ArgumentNullException(nameof(closeCommand));
        _document.PropertyChanged += OnDocumentPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id => _document.Id;
    public string DisplayTitle => _document.Title;
    public bool IsDirty => _document.IsDirty;
    public bool IsActive => _isActive;
    public ICommand ActivateCommand { get; }
    public ICommand CloseCommand { get; }

    public void SetActive(bool value)
    {
        if (_isActive == value) return;
        _isActive = value;
        OnPropertyChanged(nameof(IsActive));
    }

    public void Dispose() => _document.PropertyChanged -= OnDocumentPropertyChanged;

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.Title))
            OnPropertyChanged(nameof(DisplayTitle));
        else if (e.PropertyName == nameof(DocumentSession.IsDirty))
            OnPropertyChanged(nameof(IsDirty));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
