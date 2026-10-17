namespace VideoLuwak.Core.Cli;

/// <summary>Parsed command-line options. Unspecified values are <c>null</c>.</summary>
public sealed class CliOptions
{
    /// <summary>Input video files, in the order they were given.</summary>
    public IReadOnlyList<string> InputPaths { get; init; } = [];

    public string? OutputBaseDirectory { get; init; }

    public string? FfmpegPath { get; init; }

    public string? FfprobePath { get; init; }

    public int? PhashSimilarityThreshold { get; init; }

    public string? KeyframeFormat { get; init; }

    public int? JpegQuality { get; init; }

    public bool? DeduplicateKeyframes { get; init; }

    /// <summary>Whether to optimize audio for speech-to-text (mono, target rate). Null = use settings.</summary>
    public bool? SttOptimizeAudio { get; init; }

    /// <summary>Target sample rate (Hz) for STT-optimized WAV output. Null = use settings.</summary>
    public int? AudioSampleRate { get; init; }

    /// <summary>Target channel count for STT-optimized WAV output. Null = use settings.</summary>
    public int? AudioChannels { get; init; }

    /// <summary>High-pass filter cutoff (Hz); 0 disables it. Null = use settings.</summary>
    public int? AudioHighPassHz { get; init; }

    /// <summary>Whether to apply EBU R128 loudness normalization. Null = use settings.</summary>
    public bool? AudioNormalizeLoudness { get; init; }

    public bool ShowHelp { get; init; }

    public bool ShowVersion { get; init; }

    /// <summary>Run the FFmpeg/FFprobe setup check and installation, then exit.</summary>
    public bool SetupFfmpeg { get; init; }
}

/// <summary>Result of <see cref="CliParser.Parse"/>: options plus an optional error message.</summary>
public sealed record CliParseResult(CliOptions Options, string? Error);
