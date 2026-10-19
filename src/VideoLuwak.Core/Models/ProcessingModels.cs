namespace VideoLuwak.Core.Models;

/// <summary>Discrete phases of processing a single video file.</summary>
public enum ProcessingStage
{
    Starting,
    Probing,
    ExtractingAudio,
    ExtractingKeyframes,
    DeduplicatingKeyframes,
    Completed,
    Failed
}

/// <summary>Progress report for a single video being processed.</summary>
public sealed record ProcessingProgress
{
    public required string VideoPath { get; init; }

    public ProcessingStage Stage { get; init; }

    /// <summary>Progress within the current stage, 0..100. May be -1 when unknown.</summary>
    public double StagePercent { get; init; }

    /// <summary>Overall progress for this video, 0..100.</summary>
    public double OverallPercent { get; init; }

    public string? Message { get; init; }
}

/// <summary>Outcome of extracting one audio stream to a .wav file.</summary>
public sealed record AudioStreamResult
{
    public int AudioOrdinal { get; init; }

    public int StreamIndex { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string Description { get; init; } = string.Empty;

    public AudioStreamInfo Stream { get; init; } = new();

    /// <summary>Sample rate (Hz) of the produced WAV file.</summary>
    public int OutputSampleRate { get; init; }

    /// <summary>Channel count of the produced WAV file.</summary>
    public int OutputChannels { get; init; }

    /// <summary>ffmpeg sample format of the produced WAV file (always 16-bit PCM).</summary>
    public string OutputSampleFormat { get; init; } = "pcm_s16le";

    /// <summary>True when the audio was converted to the STT-friendly mono PCM layout.</summary>
    public bool SttOptimized { get; init; }
}

/// <summary>
/// Complete summary for one processed video. Serialized to
/// <c>processing_result.json</c> inside the output directory.
/// </summary>
public sealed record ProcessingResult
{
    public string VideoPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public long OriginalSizeBytes { get; init; }

    public double DurationSeconds { get; init; }

    public IReadOnlyList<AudioStreamResult> AudioStreams { get; init; } = [];

    public int KeyframesExtracted { get; init; }

    public long KeyframesSizeBytes { get; init; }

    public int KeyframesKept { get; init; }

    public int KeyframesRemoved { get; init; }

    /// <summary>Absolute path of the generated <c>keyframes.json</c>, empty on failure.</summary>
    public string KeyframeManifestPath { get; init; } = string.Empty;

    /// <summary>Absolute path of the generated <c>final_keyframes.json</c>, empty when de-duplication is disabled.</summary>
    public string FinalKeyframeManifestPath { get; init; } = string.Empty;

    public long FinalKeyframesSizeBytes { get; init; }

    /// <summary>Total bytes of all produced artifacts (audio wavs + final keyframes).</summary>
    public long TotalOutputSizeBytes { get; init; }

    /// <summary>Percentage of the original file size saved, 0..100.</summary>
    public double SizeReductionPercent { get; init; }

    public double ElapsedSeconds { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}
