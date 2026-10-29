using System.ComponentModel;
using Yoake.Core.Settings;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

// Tool preferences/results owned by the existing document state, not a second
// editor/session. Snapshot scopes retain original selected record identities.
public sealed class SubtitleSearchToolState(SubtitleSearchPreferences preferences) : INotifyPropertyChanged
{
    public SubtitleSearchPreferences Preferences {get;internal set;}=preferences;
    internal AssEvent[] Scope=[];
    internal SubtitleSearchMatch? Match;
    internal CancellationTokenSource? Cancellation;
    private string _status="";
    public string Status {get=>_status;internal set{if(_status==value)return;_status=value;PropertyChanged?.Invoke(this,new(nameof(Status)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record SubtitleSearchRequest(SubtitleSearchPreferences Preferences,IReadOnlyList<AssEvent>? Scope=null);
public sealed class SearchWindowCloseEventArgs(Guid document) : EventArgs {public Guid Document {get;}=document;}
