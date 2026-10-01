using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yoake.UI.ViewModels;

namespace Yoake.UI;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType SubtitleFiles = new("ASS/SSA subtitles")
    {
        Patterns = ["*.ass", "*.ssa"],
    };

    private static readonly FilePickerFileType MediaFiles = new("Video/audio")
    {
        Patterns = ["*.mkv", "*.mp4", "*.webm", "*.avi", "*.mov", "*.m2ts", "*.ts", "*.mp3", "*.flac", "*.wav", "*.m4a", "*.ogg", "*.opus"],
    };

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

        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private async void OpenSubtitleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open subtitle",
            AllowMultiple = false,
            FileTypeFilter = [SubtitleFiles, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return;
        var path = LocalPath(files[0]);
        if (path is not null)
            viewModel.OpenSubtitle(path);
    }

    private async void SaveSubtitleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        var path = viewModel.ActiveSubtitlePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save subtitle",
                SuggestedFileName = viewModel.SuggestedSubtitleFileName,
                DefaultExtension = "ass",
                ShowOverwritePrompt = true,
                FileTypeChoices = [SubtitleFiles],
            });
            path = file is null ? null : LocalPath(file);
        }
        if (!string.IsNullOrWhiteSpace(path))
            viewModel.SaveActiveSubtitle(path);
    }

    private async void OpenMediaClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open video/audio",
            AllowMultiple = false,
            FileTypeFilter = [MediaFiles, FilePickerFileTypes.All],
        });
        if (files.Count == 0)
            return;
        var path = LocalPath(files[0]);
        if (path is not null)
            await viewModel.OpenMediaAsync(path);
    }

    private static string? LocalPath(IStorageItem item)
        => item.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null;
}
