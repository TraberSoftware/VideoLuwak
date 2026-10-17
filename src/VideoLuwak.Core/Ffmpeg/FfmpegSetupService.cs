using System.ComponentModel;
using VideoLuwak.Core.Abstractions;

namespace VideoLuwak.Core.Ffmpeg;

public enum FfmpegSetupStatus
{
    AlreadyAvailable,
    Installed,
    UnsupportedPlatform,
    Failed,
}

public sealed record FfmpegSetupResult(
    FfmpegSetupStatus Status,
    string? Message = null,
    string? FfmpegPath = null,
    string? FfprobePath = null)
{
    public bool IsReady => Status is FfmpegSetupStatus.AlreadyAvailable or FfmpegSetupStatus.Installed;
}

/// <summary>
/// Checks for FFmpeg/FFprobe and performs a first-run installation on Windows when they are
/// missing. Other platforms receive package-manager instructions instead of an automatic install.
/// </summary>
public interface IFfmpegSetupService
{
    bool IsAvailable { get; }

    Task<FfmpegSetupResult> EnsureAvailableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FfmpegSetupService : IFfmpegSetupService
{
    private const string WingetEssentialsId = "Gyan.FFmpeg.Essentials";
    private const string WingetFullId = "Gyan.FFmpeg";
    private const int InstallTimeoutMinutes = 10;

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public FfmpegSetupService()
        : this(new FfmpegLocator(), new ProcessRunner())
    {
    }

    public FfmpegSetupService(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public bool IsAvailable =>
        _locator.TryResolveFfmpeg(out _) &&
        _locator.TryResolveFfprobe(out _);

    public async Task<FfmpegSetupResult> EnsureAvailableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsAvailable)
        {
            return CurrentResult(FfmpegSetupStatus.AlreadyAvailable);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new FfmpegSetupResult(
                FfmpegSetupStatus.UnsupportedPlatform,
                BuildManualInstructions());
        }

        progress?.Report("FFmpeg and FFprobe were not found. Installing FFmpeg...");

        var winget = await TryInstallWithWingetAsync(WingetEssentialsId, progress, cancellationToken).ConfigureAwait(false);
        if (winget.Succeeded && await WaitForAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            progress?.Report("FFmpeg is ready.");
            return CurrentResult(FfmpegSetupStatus.Installed);
        }

        if (IsAvailable)
        {
            progress?.Report("FFmpeg is ready.");
            return CurrentResult(FfmpegSetupStatus.Installed);
        }

        winget = await TryInstallWithWingetAsync(WingetFullId, progress, cancellationToken).ConfigureAwait(false);
        if (winget.Succeeded && await WaitForAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            progress?.Report("FFmpeg is ready.");
            return CurrentResult(FfmpegSetupStatus.Installed);
        }

        if (IsAvailable)
        {
            progress?.Report("FFmpeg is ready.");
            return CurrentResult(FfmpegSetupStatus.Installed);
        }

        progress?.Report("Automatic FFmpeg installation failed.");
        return new FfmpegSetupResult(
            FfmpegSetupStatus.Failed,
            BuildManualInstructions(winget.Error));
    }

    private async Task<(bool Succeeded, string? Error)> TryInstallWithWingetAsync(
        string packageId,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var request = new ProcessRequest
        {
            FileName = FindWinget(),
            Arguments =
            [
                "install",
                "--id", packageId,
                "--exact",
                "--silent",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--disable-interactivity",
            ],
            Timeout = TimeSpan.FromMinutes(InstallTimeoutMinutes),
        };

        try
        {
            var result = await _runner.RunAsync(
                request,
                onStdOutLine: line => ReportLine(progress, line),
                onStdErrLine: line => ReportLine(progress, line),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.Succeeded)
            {
                RefreshProcessPath();
                return (true, null);
            }

            RefreshProcessPath();
            var reason = result.TimedOut
                ? "winget timed out."
                : $"winget exited with code {result.ExitCode}.";
            return (false, reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (false, ex.Message);
        }
    }

    private async Task<bool> WaitForAvailabilityAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (IsAvailable)
            {
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return IsAvailable;
    }

    private FfmpegSetupResult CurrentResult(FfmpegSetupStatus status)
    {
        _locator.TryResolveFfmpeg(out var ffmpeg);
        _locator.TryResolveFfprobe(out var ffprobe);

        return new FfmpegSetupResult(
            status,
            status == FfmpegSetupStatus.Installed ? "FFmpeg was installed successfully." : null,
            ffmpeg,
            ffprobe);
    }

    private static void ReportLine(IProgress<string>? progress, string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            progress?.Report(line.Trim());
        }
    }

    private static string BuildManualInstructions(string? detail = null)
    {
        var details = string.IsNullOrWhiteSpace(detail)
            ? string.Empty
            : Environment.NewLine + detail;

        if (OperatingSystem.IsWindows())
        {
            return
                "FFmpeg and FFprobe are required. Automatic installation with winget failed." +
                details +
                Environment.NewLine +
                "Install FFmpeg manually with: winget install --id Gyan.FFmpeg.Essentials -e" +
                Environment.NewLine +
                "or download it from https://ffmpeg.org/download.html and add FFmpeg/FFprobe to PATH.";
        }

        if (OperatingSystem.IsMacOS())
        {
            return
                "FFmpeg and FFprobe are required. Install FFmpeg with Homebrew:" +
                details +
                Environment.NewLine +
                "brew install ffmpeg";
        }

        return
            "FFmpeg and FFprobe are required. Install FFmpeg with your package manager:" +
            details +
            Environment.NewLine +
            "sudo apt install ffmpeg  |  sudo dnf install ffmpeg  |  sudo pacman -S ffmpeg";
    }

    private static string FindWinget()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDirectory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = rawDirectory.Trim('"');
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, "winget.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : "winget";
    }

    private static void RefreshProcessPath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
        var machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetLinks = Path.Combine(localAppData, "Microsoft", "WinGet", "Links");

        var entries = current
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(user.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Concat(machine.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Append(wingetLinks)
            .Select(Environment.ExpandEnvironmentVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Environment.SetEnvironmentVariable(
            "PATH",
            string.Join(Path.PathSeparator, entries),
            EnvironmentVariableTarget.Process);
    }
}
