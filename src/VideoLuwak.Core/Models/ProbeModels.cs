namespace VideoLuwak.Core.Models;

/// <summary>
/// Describes a single audio stream inside a media container.
/// </summary>
public sealed record AudioStreamInfo
{
    /// <summary>Global stream index inside the container (ffmpeg's <c>index</c>).</summary>
    public int Index { get; init; }

    /// <summary>1-based ordinal among the audio streams only (1 = first audio stream).</summary>
    public int AudioOrdinal { get; init; }

    public string Codec { get; init; } = "unknown";

    public int Channels { get; init; }

    public int SampleRate { get; init; }

    public long? BitRate { get; init; }

    public double? DurationSeconds { get; init; }

    /// <summary>ISO-639 language tag from stream metadata, e.g. "eng".</summary>
    public string? Language { get; init; }

    /// <summary>Free-form stream title from metadata.</summary>
    public string? Title { get; init; }

    /// <summary>Human readable one-line description, e.g. "Audio #1 - eng - aac 2ch 48000Hz (Commentary)".</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Result of probing a video file with FFprobe.
/// </summary>
public sealed record ProbeResult
{
    public string VideoPath { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public double DurationSeconds { get; init; }

    public string? VideoCodec { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; } = [];

    /// <summary>Total number of streams of any kind in the container.</summary>
    public int StreamCount { get; init; }

    public bool HasAudio => AudioStreams.Count > 0;
}
