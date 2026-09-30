using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yoake.Core.Workspace;

public sealed class DocumentSession : INotifyPropertyChanged
{
    private string _title;
    private string? _path;
    private bool _isDirty;

    public DocumentSession(Guid id, string title, string? path = null)
    {
        Id = id;
        _title = title;
        _path = path;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string? Path
    {
        get => _path;
        set => SetField(ref _path, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetField(ref _isDirty, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WorkspaceManager
{
    private readonly List<DocumentSession> _documents = [];
    private int _untitledCounter;

    public event EventHandler? Changed;
    public IReadOnlyList<DocumentSession> Documents => _documents;
    public Guid? ActiveDocumentId { get; private set; }
    public DocumentSession? ActiveDocument => ActiveDocumentId is { } id
        ? _documents.FirstOrDefault(document => document.Id == id)
        : null;

    public DocumentSession CreateUntitled()
    {
        var document = new DocumentSession(Guid.NewGuid(), $"Untitled {++_untitledCounter}");
        _documents.Add(document);
        ActiveDocumentId = document.Id;
        Changed?.Invoke(this, EventArgs.Empty);
        return document;
    }

    public bool Activate(Guid id)
    {
        if (_documents.All(document => document.Id != id))
            return false;
        if (ActiveDocumentId == id)
            return true;
        ActiveDocumentId = id;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Close(Guid id)
    {
        var index = _documents.FindIndex(document => document.Id == id);
        if (index < 0)
            return false;
        var wasActive = ActiveDocumentId == id;
        _documents.RemoveAt(index);
        if (wasActive)
        {
            ActiveDocumentId = _documents.Count == 0
                ? null
                : _documents[Math.Min(index, _documents.Count - 1)].Id;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
