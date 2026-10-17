namespace VideoLuwak.Core;

/// <summary>Thrown when ffmpeg or ffprobe cannot be located on the current system.</summary>
public sealed class FfmpegNotFoundException : Exception
{
    public FfmpegNotFoundException(string message) : base(message)
    {
    }

    public FfmpegNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>Thrown when an ffmpeg / ffprobe invocation fails.</summary>
public sealed class FfmpegExecutionException : Exception
{
    public FfmpegExecutionException(string message, int exitCode, string standardError)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string StandardError { get; }
}
