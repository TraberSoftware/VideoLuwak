using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Abstractions;

/// <summary>Inspects media containers via ffprobe.</summary>
public interface IFfprobeService
{
    Task<ProbeResult> ProbeAsync(string videoPath, CancellationToken cancellationToken = default);
}

/// <summary>Extracts every audio stream of a video into individual .wav files.</summary>
public interface IAudioExtractor
{
    Task<IReadOnlyList<AudioStreamResult>> ExtractAsync(
        ProbeResult probe,
        string audioDirectory,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Extracts keyframes (I-frames) from a video into an image directory.</summary>
public interface IKeyframeExtractor
{
    Task<IReadOnlyList<string>> ExtractAsync(
        string videoPath,
        string keyframesDirectory,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Computes a 64-bit perceptual hash (pHash) for an image file.</summary>
public interface IPerceptualHasher
{
    /// <summary>Returns the 64-bit perceptual hash of the image at <paramref name="imagePath"/>.</summary>
    ulong ComputeHash(string imagePath);

    /// <summary>Hamming distance between two hashes (0 = identical, 64 = maximally different).</summary>
    static int HammingDistance(ulong a, ulong b) => System.Numerics.BitOperations.PopCount(a ^ b);
}

/// <summary>Outcome of a keyframe deduplication pass.</summary>
public sealed record DeduplicationResult
{
    public int TotalFiles { get; init; }

    public int KeptFiles { get; init; }

    public int RemovedFiles { get; init; }

    public IReadOnlyList<string> KeptPaths { get; init; } = [];

    public IReadOnlyList<string> RemovedPaths { get; init; } = [];
}

/// <summary>
/// Removes perceptually similar keyframes. Files are enumerated in ascending
/// alphabetical order and each file is compared against the previous file's hash;
/// the reference rolls forward to the most recently generated hash even when the
/// previous file was removed.
/// </summary>
public interface IKeyframeDeduplicator
{
    DeduplicationResult Deduplicate(
        string sourceDirectory,
        string destinationDirectory,
        int similarityThreshold,
        IPerceptualHasher hasher,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
