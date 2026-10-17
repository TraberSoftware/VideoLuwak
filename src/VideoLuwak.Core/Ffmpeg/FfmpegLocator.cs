using VideoLuwak.Core.Abstractions;

namespace VideoLuwak.Core.Ffmpeg;

/// <summary>
/// Resolves the ffmpeg / ffprobe executables for the current platform.
/// Resolution order (first hit wins): explicit override -&gt; <c>VIDEOLUWAK_*</c>
/// environment variable -&gt; application directory (including a <c>tools</c>
/// subfolder) -&gt; embedded resource (Windows release builds only) -&gt; system
/// <c>PATH</c>.
/// </summary>
public sealed class FfmpegLocator : IFfmpegLocator
{
    private const string FfmpegEnvironmentVariable = "VIDEOLUWAK_FFMPEG";
    private const string FfprobeEnvironmentVariable = "VIDEOLUWAK_FFPROBE";

    private readonly string? _ffmpegOverride;
    private readonly string? _ffprobeOverride;

    private string? _resolvedFfmpeg;
    private string? _resolvedFfprobe;

    public FfmpegLocator(string? ffmpegOverride = null, string? ffprobeOverride = null)
    {
        _ffmpegOverride = ffmpegOverride;
        _ffprobeOverride = ffprobeOverride;
    }

    public string ResolveFfmpeg() =>
        _resolvedFfmpeg ??= ResolveExecutable(_ffmpegOverride, FfmpegEnvironmentVariable, "ffmpeg")
            ?? throw new FfmpegNotFoundException(BuildNotFoundMessage("ffmpeg", FfmpegEnvironmentVariable));

    public string ResolveFfprobe() =>
        _resolvedFfprobe ??= ResolveExecutable(_ffprobeOverride, FfprobeEnvironmentVariable, "ffprobe")
            ?? throw new FfmpegNotFoundException(BuildNotFoundMessage("ffprobe", FfprobeEnvironmentVariable));

    public bool TryResolveFfmpeg(out string path)
    {
        try
        {
            path = ResolveFfmpeg();
            return true;
        }
        catch (FfmpegNotFoundException)
        {
            path = string.Empty;
            return false;
        }
    }

    public bool TryResolveFfprobe(out string path)
    {
        try
        {
            path = ResolveFfprobe();
            return true;
        }
        catch (FfmpegNotFoundException)
        {
            path = string.Empty;
            return false;
        }
    }

    private static string? ResolveExecutable(string? overridePath, string environmentVariable, string baseName)
    {
        if (IsExistingFile(overridePath))
        {
            return Path.GetFullPath(overridePath!);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        if (IsExistingFile(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment!);
        }

        var fromApplicationDirectory = ResolveFromApplicationDirectory(baseName);
        if (fromApplicationDirectory is not null)
        {
            return fromApplicationDirectory;
        }

        if (EmbeddedFfmpegProvider.TryExtract(EmbeddedResourceName(baseName), ExecutableFileName(baseName), out var embedded)
            && IsExistingFile(embedded))
        {
            return embedded;
        }

        return TryFindOnPath(baseName, out var onPath) ? onPath : null;
    }

    private static string? ResolveFromApplicationDirectory(string baseName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        foreach (var candidate in EnumerateApplicationDirectoryCandidates(baseDirectory, baseName))
        {
            if (IsExistingFile(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateApplicationDirectoryCandidates(string baseDirectory, string baseName)
    {
        var names = ExecutableFileNames(baseName);
        foreach (var name in names)
        {
            yield return Path.Combine(baseDirectory, name);
        }

        var toolsDirectory = Path.Combine(baseDirectory, "tools");
        foreach (var name in names)
        {
            yield return Path.Combine(toolsDirectory, name);
        }
    }

    private static bool TryFindOnPath(string baseName, out string path)
    {
        path = string.Empty;

        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        var fileName = ExecutableFileName(baseName);
        foreach (var rawEntry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(rawEntry, fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (IsExistingFile(candidate))
            {
                path = candidate;
                return true;
            }
        }

        return false;
    }

    private static string ExecutableFileName(string baseName) =>
        OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    private static IReadOnlyList<string> ExecutableFileNames(string baseName) =>
        OperatingSystem.IsWindows() ? [baseName + ".exe", baseName] : [baseName];

    private static string EmbeddedResourceName(string baseName) =>
        $"VideoLuwak.ffmpeg.win-x64.{baseName}.exe";

    private static bool IsExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string BuildNotFoundMessage(string executable, string environmentVariable) =>
        $"Could not locate '{executable}'. Install ffmpeg and add it to PATH, place it next to the application (or in a 'tools' subfolder), set the {environmentVariable} environment variable, or pass an explicit executable path.";
}
