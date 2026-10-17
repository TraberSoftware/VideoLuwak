using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VideoLuwak.App.Services;
using VideoLuwak.App.ViewModels;

namespace VideoLuwak.App.Views;

/// <summary>
/// Dedicated settings view. Shares the main window's view model so edits are
/// applied (and persisted) immediately; closing it simply returns to the app.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Icon = BrandAssets.LoadWindowIcon();
        Closing += (_, _) => ViewModel?.SaveSettingsNow();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void OnBrowseOutputClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.SaveSettingsNow();
        Close();
    }
}
