using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VideoLuwak.App.ViewModels;
using VideoLuwak.App.Views;
using VideoLuwak.Core.Composition;
using VideoLuwak.Core.Config;
using VideoLuwak.Core.Ffmpeg;

namespace VideoLuwak.App;

/// <summary>Avalonia application. Wires the main window to its view model.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsStore = new SettingsStore();
            var locator = ExtractorFactory.CreateLocator();
            var processor = ExtractorFactory.CreateVideoProcessor(locator);
            var ffmpegSetup = new FfmpegSetupService(locator, new ProcessRunner());
            var viewModel = new MainWindowViewModel(settingsStore, processor, ffmpegSetup);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Best-effort flush of any settings edited just before closing.
            desktop.ShutdownRequested += (_, _) => viewModel.SaveSettingsNow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
