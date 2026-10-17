namespace VideoLuwak.Core.Abstractions;

/// <summary>A request to execute an external process.</summary>
public sealed record ProcessRequest
{
    public required string FileName { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan? Timeout { get; init; }
}

/// <summary>Captured result of an external process invocation.</summary>
public sealed record ProcessResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool TimedOut { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>Runs external processes (ffmpeg / ffprobe) with line streaming.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the ffmpeg / ffprobe executables for the current platform.
/// Resolution order: explicit override -> app directory -> embedded resource
/// (Windows) -> system PATH.
/// </summary>
public interface IFfmpegLocator
{
    string ResolveFfmpeg();

    string ResolveFfprobe();

    bool TryResolveFfmpeg(out string path);

    bool TryResolveFfprobe(out string path);
}
