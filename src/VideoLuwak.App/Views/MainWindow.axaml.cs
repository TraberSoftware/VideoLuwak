using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VideoLuwak.App.Services;
using VideoLuwak.App.ViewModels;

namespace VideoLuwak.App.Views;

/// <summary>
/// Main window. Kept intentionally thin: it only wires up drag &amp; drop, the
/// native file/folder pickers and shell actions, then forwards the results to
/// the view model.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string[] VideoPickerPatterns =
    [
        "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v", "*.wmv",
        "*.flv", "*.mpg", "*.mpeg", "*.ts", "*.m2ts", "*.mts", "*.3gp",
        "*.vob", "*.ogv",
    ];

    public MainWindow()
    {
        InitializeComponent();
        Icon = BrandAssets.LoadWindowIcon();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;

        if (ViewModel is { } viewModel)
        {
            viewModel.IsDragOver = hasFiles;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            viewModel.IsDragOver = false;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.IsDragOver = false;

        var files = e.DataTransfer.TryGetFiles();
        if (files is not null)
        {
            viewModel.AddFiles(ToLocalPaths(files));
        }

        e.Handled = true;
    }

    private async void OnBrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || GetStorageProvider() is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose output folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0)
        {
            viewModel.SetOutputDirectory(folders[0].TryGetLocalPath());
        }
    }

    private async void OnAddFilesClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || GetStorageProvider() is not { } storage)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add video files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Video files") { Patterns = VideoPickerPatterns },
                new FilePickerFileType("All files") { Patterns = ["*.*"] },
            ],
        });

        if (files.Count > 0)
        {
            viewModel.AddFiles(ToLocalPaths(files));
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var window = new SettingsWindow { DataContext = viewModel };
        await window.ShowDialog(this);
    }

    private void OnOpenOutputClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var path = viewModel.Results.FirstOrDefault(r => r.Success)?.OutputDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = viewModel.OutputDirectory;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            viewModel.StatusLine = "Choose an output folder first.";
            return;
        }

        OpenFolder(path, "The output folder does not exist yet.");
    }

    private void OnOpenResultFolderClick(object? sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;
        OpenFolder(path, "The output folder does not exist yet.");
    }

    private void OnOpenAudioFolderClick(object? sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;
        OpenFolder(path, "No audio folder was produced for this video.");
    }

    private void OnRevealKeyframesClick(object? sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;

        // Deduplication can be disabled, in which case only the output root exists.
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            if ((sender as Button)?.DataContext is ResultRowViewModel row)
            {
                path = row.OutputDirectory;
            }
        }

        OpenFolder(path, "No keyframes folder was produced for this video.");
    }

    private async void OnCopyTextClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);

        if (ViewModel is { } viewModel)
        {
            viewModel.StatusLine = "Copied to clipboard.";
        }
    }

    private void OpenFolder(string? path, string missingMessage)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            viewModel.StatusLine = missingMessage;
            return;
        }

        if (!ShellService.OpenFolder(path))
        {
            viewModel.StatusLine = "Could not open the folder.";
        }
    }

    private IStorageProvider? GetStorageProvider() => TopLevel.GetTopLevel(this)?.StorageProvider;

    private static List<string> ToLocalPaths(IEnumerable<IStorageItem> items)
    {
        var paths = new List<string>();
        foreach (var item in items)
        {
            var path = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }
}
