using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Yoake.Core.Media;

namespace Yoake.UI;

public sealed class AudioTrackWindow : Window
{
    public AudioTrackWindow(IReadOnlyList<AudioTrackInfo> tracks, int? current)
    {
        Name = "AudioTrackDialog"; Title = "Select audio stream";
        Width = 520; Height = 320; MinWidth = 360; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var names = new NameScope(); NameScope.SetNameScope(this, names);
        var root = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 10 };
        root.Children.Add(new TextBlock { Text = "Choose audio from the current source. Video and subtitle edits are retained.", TextWrapping = TextWrapping.Wrap });
        var list = new ListBox { Name = "AudioTracks", ItemsSource = tracks,
            SelectedItem = tracks.FirstOrDefault(track => track.Index == current) ?? tracks.FirstOrDefault() };
        Grid.SetRow(list, 1); root.Children.Add(list);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(actions, 2); root.Children.Add(actions);
        var apply = new Button { Name = "SelectAudioTrack", Content = "Select", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        actions.Children.Add(apply); actions.Children.Add(cancel);
        list.SelectionChanged += (_, _) => apply.IsEnabled = list.SelectedItem is AudioTrackInfo;
        apply.IsEnabled = list.SelectedItem is AudioTrackInfo;
        apply.Click += (_, _) => { if (list.SelectedItem is AudioTrackInfo track) Close((int?)track.Index); };
        cancel.Click += (_, _) => Close((int?)null);
        names.Register(list.Name!, list); names.Register(apply.Name!, apply); names.Complete(); Content = root;
        Opened += async (_, _) => { await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); if (IsVisible) list.Focus(); };
    }
}
