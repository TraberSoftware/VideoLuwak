namespace VideoLuwak.Core.Models;

/// <summary>
/// One keyframe together with the interval during which it represents the source video.
/// Every entry always has a start and an end timestamp.
/// </summary>
public sealed record KeyframeEntry
{
    /// <summary>Original 1-based extracted ordinal (gaps are allowed in the final manifest).</summary>
    public int Ordinal { get; init; }

    /// <summary>Path relative to the output directory, using forward slashes.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Start timestamp of the interval, rounded to milliseconds.</summary>
    public double TimestampSeconds { get; init; }

    /// <summary>Start timestamp formatted as <c>HH:MM:SS.mmm</c> using total hours.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>End of the interval: the next keyframe's start, or the source duration.</summary>
    public double EndTimestampSeconds { get; init; }

    /// <summary>Formatted <see cref="EndTimestampSeconds"/>.</summary>
    public string EndTimestamp { get; init; } = string.Empty;

    /// <summary>True when the timestamp had to be estimated or was clamped.</summary>
    public bool TimestampEstimated { get; init; }
}

/// <summary>An extracted audio file that can be transcribed for a keyframe's time range.</summary>
public sealed record KeyframeAudioReference
{
    /// <summary>Path relative to the output directory, using forward slashes.</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>ISO-639 language tag from the source stream, when present.</summary>
    public string? Language { get; init; }

    /// <summary>Human readable description of the source stream.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Machine-readable keyframe timeline. Serialized to <c>keyframes.json</c> (every
/// extracted keyframe) and <c>final_keyframes.json</c> (deduplicated keyframes only)
/// inside the output directory.
/// </summary>
public sealed record KeyframeManifest
{
    /// <summary>Absolute path of the source video.</summary>
    public string Video { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public string TimeFormat { get; init; } = "HH:MM:SS.mmm";

    public int KeyframeCount { get; init; }

    public IReadOnlyList<KeyframeAudioReference> Audio { get; init; } = [];

    public IReadOnlyList<KeyframeEntry> Keyframes { get; init; } = [];
}
