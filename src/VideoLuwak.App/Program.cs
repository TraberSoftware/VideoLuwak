using Avalonia;
using VideoLuwak.App;

namespace VideoLuwak.App;

/// <summary>
/// Dual-mode entry point. When launched with command-line arguments (and without
/// <c>--gui</c>) the process behaves like a console application; when double-clicked
/// with no arguments it starts the Avalonia desktop GUI.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (VideoLuwak.Core.Cli.CliRunner.IsCliRequest(args))
        {
            return RunCli(args);
        }

        // The executable is a console-subsystem process so that shells wait for CLI runs.
        // Drop the console Windows created for this launch before showing the GUI, otherwise
        // double-clicking the application would leave one behind. No-op elsewhere.
        VideoLuwak.Core.Cli.ConsoleBridge.DetachConsoleForGuiMode();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>Wires up the FFmpeg-backed pipeline and runs the CLI workflow.</summary>
    private static int RunCli(string[] args)
    {
        var settings = new VideoLuwak.Core.Config.SettingsStore();
        var parsed = VideoLuwak.Core.Cli.CliParser.Parse(args);
        var locator = VideoLuwak.Core.Composition.ExtractorFactory.CreateLocator(
            parsed.Options.FfmpegPath,
            parsed.Options.FfprobePath);
        var processor = VideoLuwak.Core.Composition.ExtractorFactory.CreateVideoProcessor(locator);
        var setup = new VideoLuwak.Core.Ffmpeg.FfmpegSetupService(locator, new VideoLuwak.Core.Ffmpeg.ProcessRunner());
        var runner = new VideoLuwak.Core.Cli.CliRunner(processor, settings, setup);

        return runner
            .RunAsync(args, Console.Out, Console.Error)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Avalonia bootstrapper (also used by the previewer).</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
