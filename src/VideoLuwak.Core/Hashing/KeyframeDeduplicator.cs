using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Hashing;

/// <summary>
/// Removes perceptually duplicate keyframes using a rolling reference hash.
/// Files are sorted by filename in <see cref="StringComparer.Ordinal"/> ascending order
/// (which, thanks to the extractor's zero-padded numbering, equals chronological order).
/// Each file is hashed and compared against the hash of the immediately preceding file.
/// A frame is removed when its Hamming distance to the reference is
/// <c>&lt;= similarityThreshold</c>. The reference ALWAYS rolls forward to the hash just
/// computed, even when that frame was removed, so chains of near-identical frames do not
/// drift past the threshold.
/// </summary>
public sealed class KeyframeDeduplicator : IKeyframeDeduplicator
{
    /// <inheritdoc />
    public DeduplicationResult Deduplicate(
        string sourceDirectory,
        string destinationDirectory,
        int similarityThreshold,
        IPerceptualHasher hasher,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(hasher);

        Directory.CreateDirectory(destinationDirectory);

        if (!Directory.Exists(sourceDirectory))
        {
            return new DeduplicationResult();
        }

        string[] files = Directory
            .GetFiles(sourceDirectory)
            .Where(file => !Path.GetFileName(file).StartsWith('.'))
            .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            return new DeduplicationResult();
        }

        var keptPaths = new List<string>(files.Length);
        var removedPaths = new List<string>();
        int kept = 0;
        int removed = 0;

        ulong previous = 0UL;
        bool hasPrevious = false;

        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string file = files[i];
            ulong hash = hasher.ComputeHash(file);

            bool duplicate = hasPrevious &&
                             PerceptualHash64.HammingDistance(hash, previous) <= similarityThreshold;

            if (duplicate)
            {
                removedPaths.Add(file);
                removed++;
            }
            else
            {
                string destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
                File.Copy(file, destination, overwrite: true);
                keptPaths.Add(destination);
                kept++;
            }

            // Always roll the reference forward — this is the core of the algorithm.
            previous = hash;
            hasPrevious = true;

            progress?.Report(new ProcessingProgress
            {
                VideoPath = sourceDirectory,
                Stage = ProcessingStage.DeduplicatingKeyframes,
                StagePercent = (i + 1) / (double)files.Length * 100.0,
                OverallPercent = (i + 1) / (double)files.Length * 100.0,
                Message = $"Deduplicating keyframes ({i + 1}/{files.Length})",
            });
        }

        return new DeduplicationResult
        {
            TotalFiles = files.Length,
            KeptFiles = kept,
            RemovedFiles = removed,
            KeptPaths = keptPaths,
            RemovedPaths = removedPaths,
        };
    }
}
