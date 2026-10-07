using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Yoake.Core.Subtitles;

namespace Yoake.UI.ViewModels;

internal sealed class EventSelection : ObservableCollection<AssEvent>
{
    public void Replace(IEnumerable<AssEvent> source)
    {
        var lines=source.ToArray();
        Items.Clear();foreach(var line in lines)Items.Add(line);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
