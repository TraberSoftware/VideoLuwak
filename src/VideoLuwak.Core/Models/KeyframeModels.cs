namespace VideoLuwak.Core.Models;

/// <summary>
/// One extracted keyframe together with its position in the source video and its
/// de-duplication outcome.
/// </summary>
public sealed record KeyframeEntry
{
    /// <summary>1-based extracted order (chronological).</summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Path relative to the output directory, using forward slashes. Points at
    /// <c>final_keyframes/...</c> when the frame was kept and <c>keyframes/...</c> otherwise.
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Path relative to the output directory of the originally extracted frame.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>Timestamp of the frame in the source video, rounded to milliseconds.</summary>
    public double TimestampSeconds { get; init; }

    /// <summary>Timestamp formatted as <c>HH:MM:SS.mmm</c> using total hours.</summary>
    public string Timestamp { get; init; } = string.Empty;

    /// <summary>
    /// End of the interval this kept frame represents: the next kept frame's timestamp,
    /// or the source duration for the last kept frame. <c>null</c> for removed frames.
    /// </summary>
    public double? EndTimestampSeconds { get; init; }

    /// <summary>Formatted <see cref="EndTimestampSeconds"/>, or <c>null</c> for removed frames.</summary>
    public string? EndTimestamp { get; init; }

    /// <summary>True when the frame survived pHash de-duplication.</summary>
    public bool Kept { get; init; }

    /// <summary>True when the timestamp had to be estimated because probing returned none.</summary>
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
/// Machine-readable pairing of every extracted keyframe with its source timestamp,
/// de-duplication outcome, representative time range and matching audio files.
/// Serialized to <c>keyframes.json</c> inside the output directory.
/// </summary>
public sealed record KeyframeManifest
{
    /// <summary>Absolute path of the source video.</summary>
    public string Video { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public string TimeFormat { get; init; } = "HH:MM:SS.mmm";

    public IReadOnlyList<KeyframeAudioReference> Audio { get; init; } = [];

    public IReadOnlyList<KeyframeEntry> Keyframes { get; init; } = [];
}
