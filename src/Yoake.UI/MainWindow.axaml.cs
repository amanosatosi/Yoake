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

    private static readonly string[] SameBasenameMediaExtensions =
        [".mkv", ".mp4", ".webm", ".m2ts", ".ts", ".avi", ".mov"];

    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
        {
            WindowDecorations = Avalonia.Controls.WindowDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 36;
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
        if (path is not null && viewModel.OpenSubtitle(path))
            await TryOpenAssociatedMediaAsync(viewModel, path);
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

    private void CloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DocumentTabViewModel tab } && tab.CloseCommand.CanExecute(null))
        {
            tab.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static async Task TryOpenAssociatedMediaAsync(MainWindowViewModel viewModel, string subtitlePath)
    {
        var mediaPath = FindAssociatedMedia(subtitlePath);
        if (mediaPath is not null)
            await viewModel.OpenMediaAsync(mediaPath);
    }

    private static string? FindAssociatedMedia(string subtitlePath)
    {
        var scriptDirectory = Path.GetDirectoryName(subtitlePath) ?? string.Empty;
        string? videoReference = null;
        string? audioReference = null;

        try
        {
            var inProjectGarbage = false;
            foreach (var rawLine in File.ReadLines(subtitlePath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inProjectGarbage = string.Equals(line, "[Aegisub Project Garbage]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inProjectGarbage)
                    continue;

                var colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (string.Equals(key, "Video File", StringComparison.OrdinalIgnoreCase))
                    videoReference = value;
                else if (string.Equals(key, "Audio File", StringComparison.OrdinalIgnoreCase))
                    audioReference = value;
            }
        }
        catch
        {
            // ASS loading already succeeded; media discovery is best-effort only.
        }

        var video = ResolveAegisubMediaReference(videoReference, scriptDirectory, null);
        if (video is not null)
            return video;

        var audio = ResolveAegisubMediaReference(audioReference, scriptDirectory, video);
        if (audio is not null)
            return audio;

        var stem = Path.Combine(scriptDirectory, Path.GetFileNameWithoutExtension(subtitlePath));
        foreach (var extension in SameBasenameMediaExtensions)
        {
            var candidate = stem + extension;
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string? ResolveAegisubMediaReference(string? reference, string scriptDirectory, string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        var value = reference.Trim().Trim('"');
        if (string.Equals(value, "?video", StringComparison.OrdinalIgnoreCase))
            return videoPath;
        if (value.StartsWith("?dummy:", StringComparison.OrdinalIgnoreCase))
            return null;
        if (value.StartsWith("?script", StringComparison.OrdinalIgnoreCase))
            value = value[7..].TrimStart('/', '\\');

        var candidate = Path.IsPathRooted(value) ? value : Path.Combine(scriptDirectory, value);
        try
        {
            candidate = Path.GetFullPath(candidate);
        }
        catch
        {
            return null;
        }
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? LocalPath(IStorageItem item)
        => item.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null;
}
