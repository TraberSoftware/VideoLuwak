using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Config;

/// <summary>
/// Persisted user preferences. Loaded from / saved to <see cref="SettingsStore"/>.
/// All members are plain mutable properties so the JSON (de)serializer can round-trip them.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Last used global output directory. <c>null</c> means "next to each video".</summary>
    public string? OutputBaseDirectory { get; set; }

    /// <summary>Maximum Hamming distance for two keyframes to be treated as duplicates.</summary>
    public int PhashSimilarityThreshold { get; set; } = 8;

    /// <summary>Keyframe image format: "jpg" or "png".</summary>
    public string KeyframeFormat { get; set; } = "jpg";

    /// <summary>ffmpeg -q:v quality for jpg keyframes (1..31).</summary>
    public int JpegQuality { get; set; } = 2;

    /// <summary>When false the deduplicated <c>final_keyframes</c> folder is skipped.</summary>
    public bool DeduplicateKeyframes { get; set; } = true;

    /// <summary>When true (default) extracted audio is downmixed to mono 16 kHz PCM for STT.</summary>
    public bool SttOptimizeAudio { get; set; } = true;

    /// <summary>Target sample rate (Hz) for STT-optimized WAV output.</summary>
    public int AudioSampleRate { get; set; } = 16000;

    /// <summary>Target channel count for STT-optimized WAV output (1 = mono).</summary>
    public int AudioChannels { get; set; } = 1;

    /// <summary>Optional high-pass filter cutoff (Hz) for extracted audio; 0 disables it.</summary>
    public int AudioHighPassHz { get; set; }

    /// <summary>When true, EBU R128 loudness normalization is applied to extracted audio.</summary>
    public bool AudioNormalizeLoudness { get; set; }

    /// <summary>
    /// Projects these settings onto <see cref="ProcessingOptions"/>. The base directory
    /// precedence is: <paramref name="outputBaseOverride"/> -> <see cref="OutputBaseDirectory"/>
    /// -> <c>null</c> (output alongside each source video).
    /// </summary>
    public ProcessingOptions ToProcessingOptions(string? outputBaseOverride = null)
    {
        var baseDirectory = !string.IsNullOrWhiteSpace(outputBaseOverride)
            ? outputBaseOverride
            : !string.IsNullOrWhiteSpace(OutputBaseDirectory)
                ? OutputBaseDirectory
                : null;

        return new ProcessingOptions
        {
            OutputBaseDirectory = baseDirectory,
            PhashSimilarityThreshold = PhashSimilarityThreshold,
            KeyframeFormat = KeyframeFormat,
            JpegQuality = JpegQuality,
            DeduplicateKeyframes = DeduplicateKeyframes,
            SttOptimizeAudio = SttOptimizeAudio,
            AudioSampleRate = AudioSampleRate,
            AudioChannels = AudioChannels,
            AudioHighPassHz = AudioHighPassHz,
            AudioNormalizeLoudness = AudioNormalizeLoudness,
        };
    }
}
