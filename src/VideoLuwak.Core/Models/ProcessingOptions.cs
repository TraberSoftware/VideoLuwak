namespace VideoLuwak.Core.Models;

/// <summary>
/// Tunable options for a processing run. Defaults reflect the documented behaviour.
/// </summary>
public sealed record ProcessingOptions
{
    /// <summary>
    /// Root directory under which per-video output folders are created.
    /// When <c>null</c> (default) output is created next to the source video.
    /// </summary>
    public string? OutputBaseDirectory { get; init; }

    /// <summary>Image format for extracted keyframes: "jpg" or "png".</summary>
    public string KeyframeFormat { get; init; } = "jpg";

    /// <summary>ffmpeg -q:v style quality for jpg keyframes (2 = near-lossless, 31 = worst).</summary>
    public int JpegQuality { get; init; } = 2;

    /// <summary>When false the deduplicated (final) keyframe folder is skipped.</summary>
    public bool DeduplicateKeyframes { get; init; } = true;

    /// <summary>
    /// When true (default) extracted audio is optimized for speech-to-text models
    /// (Whisper, Parakeet/NVIDIA NeMo, wav2vec, …): downmixed to mono, resampled to
    /// <see cref="AudioSampleRate"/>, and encoded as 16-bit PCM. When false the source
    /// channel layout and sample rate are kept.
    /// </summary>
    public bool SttOptimizeAudio { get; init; } = true;

    /// <summary>Target sample rate (Hz) for STT-optimized WAV output. Default 16000.</summary>
    public int AudioSampleRate { get; init; } = 16000;

    /// <summary>Target channel count for STT-optimized WAV output. Default 1 (mono).</summary>
    public int AudioChannels { get; init; } = 1;

    /// <summary>
    /// Optional high-pass filter cutoff (Hz) applied before encoding to remove low-frequency
    /// rumble. 0 (default) disables the filter. A value around 60-80 Hz is a common speech choice.
    /// </summary>
    public int AudioHighPassHz { get; init; }

    /// <summary>
    /// When true, EBU R128 loudness normalization is applied. Off by default: STT models tolerate
    /// varying levels well and single-pass normalization can add artifacts on short/silent clips.
    /// </summary>
    public bool AudioNormalizeLoudness { get; init; }

    /// <summary>
    /// Maximum Hamming distance between two 64-bit perceptual hashes for the frames to
    /// be considered duplicates. A frame is removed when distance &lt;= this value.
    /// </summary>
    public int PhashSimilarityThreshold { get; init; } = 8;

    /// <summary>Recreate output directories if they already exist.</summary>
    public bool Overwrite { get; init; } = true;

    /// <summary>Optional explicit ffmpeg executable path (overrides auto-discovery).</summary>
    public string? FfmpegPath { get; init; }

    /// <summary>Optional explicit ffprobe executable path (overrides auto-discovery).</summary>
    public string? FfprobePath { get; init; }

    public static ProcessingOptions Default { get; } = new();
}
