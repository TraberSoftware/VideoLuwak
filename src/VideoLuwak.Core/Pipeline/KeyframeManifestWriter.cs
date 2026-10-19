using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Pipeline;

/// <summary>
/// Builds and writes the two keyframe timelines: <c>keyframes.json</c> for every
/// extracted keyframe and <c>final_keyframes.json</c> for the de-duplicated subset.
/// Every entry carries a start and end timestamp plus the extracted audio files that
/// cover its interval.
/// </summary>
public static class KeyframeManifestWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static KeyframeManifest BuildAll(
        string videoPath,
        double durationSeconds,
        IReadOnlyList<string> extractedKeyframePaths,
        IReadOnlyList<double> timestampSeconds,
        IReadOnlyList<AudioStreamResult> audioStreams,
        string outputDirectory)
    {
        ValidateArguments(
            videoPath,
            extractedKeyframePaths,
            timestampSeconds,
            audioStreams,
            outputDirectory);

        double duration = NormalizeDuration(durationSeconds);
        var entries = ApplySequentialEnds(
            BuildEntries(extractedKeyframePaths, timestampSeconds, duration),
            duration);

        return CreateManifest(videoPath, duration, audioStreams, outputDirectory, entries);
    }

    public static KeyframeManifest BuildFinal(
        string videoPath,
        double durationSeconds,
        IReadOnlyList<string> extractedKeyframePaths,
        IReadOnlyList<double> timestampSeconds,
        IReadOnlyList<AudioStreamResult> audioStreams,
        string outputDirectory,
        DeduplicationResult deduplication)
    {
        ValidateArguments(
            videoPath,
            extractedKeyframePaths,
            timestampSeconds,
            audioStreams,
            outputDirectory);
        ArgumentNullException.ThrowIfNull(deduplication);

        double duration = NormalizeDuration(durationSeconds);
        var allEntries = BuildEntries(extractedKeyframePaths, timestampSeconds, duration);
        var keptFileNames = new HashSet<string>(
            deduplication.KeptPaths.Select(path => Path.GetFileName(path) ?? string.Empty),
            StringComparer.Ordinal);

        var keptEntries = new List<KeyframeEntry>(keptFileNames.Count);
        for (int i = 0; i < allEntries.Count; i++)
        {
            string fileName = Path.GetFileName(extractedKeyframePaths[i]) ?? string.Empty;
            if (!keptFileNames.Contains(fileName))
            {
                continue;
            }

            keptEntries.Add(allEntries[i] with
            {
                File = OutputLayout.FinalKeyframesFolderName + "/" + fileName,
            });
        }

        if (keptEntries.Count != keptFileNames.Count)
        {
            throw new InvalidOperationException(
                $"One or more kept paths from the deduplication result did not match an extracted keyframe " +
                $"(filename matching is case-sensitive). Kept paths: {keptFileNames.Count}, matched: {keptEntries.Count}.");
        }

        if (keptEntries.Count != deduplication.KeptFiles)
        {
            throw new InvalidOperationException(
                $"Deduplicated keyframe count ({keptEntries.Count}) disagrees with the " +
                $"deduplication result ({deduplication.KeptFiles}).");
        }

        ApplySequentialEnds(keptEntries, duration);

        return CreateManifest(videoPath, duration, audioStreams, outputDirectory, keptEntries);
    }

    public static string WriteAll(string outputDirectory, KeyframeManifest manifest) =>
        Write(outputDirectory, manifest, OutputLayout.GetKeyframeManifestPath(outputDirectory));

    public static string WriteFinal(string outputDirectory, KeyframeManifest manifest) =>
        Write(outputDirectory, manifest, OutputLayout.GetFinalKeyframeManifestPath(outputDirectory));

    /// <summary>Formats seconds as <c>HH:MM:SS.mmm</c>, using total hours rather than a 24-hour wrap.</summary>
    public static string FormatTimestamp(double seconds)
    {
        long totalMilliseconds = (long)Math.Round(
            Math.Max(0.0, seconds) * 1000.0,
            MidpointRounding.AwayFromZero);

        long hours = totalMilliseconds / 3_600_000;
        long minutes = totalMilliseconds / 60_000 % 60;
        long wholeSeconds = totalMilliseconds / 1000 % 60;
        long milliseconds = totalMilliseconds % 1000;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{minutes:00}:{wholeSeconds:00}.{milliseconds:000}");
    }

    private static List<KeyframeEntry> BuildEntries(
        IReadOnlyList<string> extractedKeyframePaths,
        IReadOnlyList<double> timestampSeconds,
        double durationSeconds)
    {
        int count = extractedKeyframePaths.Count;
        bool useProvidedTimestamps = timestampSeconds.Count == count;
        var entries = new List<KeyframeEntry>(count);
        double previousTimestamp = 0.0;

        for (int i = 0; i < count; i++)
        {
            double rawTimestamp = useProvidedTimestamps
                ? timestampSeconds[i]
                : EstimateTimestamp(i, count, durationSeconds, timestampSeconds);

            double roundedTimestamp = RoundSeconds(rawTimestamp);
            double timestamp = RoundSeconds(Math.Max(0.0, rawTimestamp));
            if (i > 0 && timestamp < previousTimestamp)
            {
                timestamp = previousTimestamp;
            }

            previousTimestamp = timestamp;
            string fileName = Path.GetFileName(extractedKeyframePaths[i]) ?? string.Empty;

            entries.Add(new KeyframeEntry
            {
                Ordinal = i + 1,
                File = OutputLayout.KeyframesFolderName + "/" + fileName,
                TimestampSeconds = timestamp,
                Timestamp = FormatTimestamp(timestamp),
                TimestampEstimated = !useProvidedTimestamps || timestamp != roundedTimestamp,
            });
        }

        return entries;
    }

    private static List<KeyframeEntry> ApplySequentialEnds(
        List<KeyframeEntry> entries,
        double durationSeconds)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            double end = i + 1 < entries.Count
                ? entries[i + 1].TimestampSeconds
                : LastEnd(entries[i].TimestampSeconds, durationSeconds);

            entries[i] = entries[i] with
            {
                EndTimestampSeconds = end,
                EndTimestamp = FormatTimestamp(end),
            };
        }

        return entries;
    }

    private static KeyframeManifest CreateManifest(
        string videoPath,
        double durationSeconds,
        IReadOnlyList<AudioStreamResult> audioStreams,
        string outputDirectory,
        List<KeyframeEntry> entries)
    {
        var audio = new List<KeyframeAudioReference>(audioStreams.Count);
        foreach (var stream in audioStreams)
        {
            audio.Add(new KeyframeAudioReference
            {
                File = ToRelativePath(outputDirectory, stream.OutputPath),
                Language = stream.Stream.Language,
                Description = stream.Description,
            });
        }

        return new KeyframeManifest
        {
            Video = Path.GetFullPath(videoPath),
            DurationSeconds = durationSeconds,
            TimeFormat = "HH:MM:SS.mmm",
            KeyframeCount = entries.Count,
            Audio = audio,
            Keyframes = entries,
        };
    }

    private static string Write(
        string outputDirectory,
        KeyframeManifest manifest,
        string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        string path = Path.GetFullPath(manifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string tempPath = path + ".tmp";
        string json = JsonSerializer.Serialize(manifest, SerializerOptions);
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: cleanup must not mask the primary write result.
            }
        }

        return path;
    }

    private static void ValidateArguments(
        string videoPath,
        IReadOnlyList<string> extractedKeyframePaths,
        IReadOnlyList<double> timestampSeconds,
        IReadOnlyList<AudioStreamResult> audioStreams,
        string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentNullException.ThrowIfNull(extractedKeyframePaths);
        ArgumentNullException.ThrowIfNull(timestampSeconds);
        ArgumentNullException.ThrowIfNull(audioStreams);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
    }

    private static double NormalizeDuration(double durationSeconds) =>
        RoundSeconds(Math.Max(0.0, durationSeconds));

    private static double EstimateTimestamp(
        int index,
        int count,
        double durationSeconds,
        IReadOnlyList<double> timestampSeconds)
    {
        if (durationSeconds > 0.0 && count > 0)
        {
            return durationSeconds * index / count;
        }

        return timestampSeconds.Count > 0 ? timestampSeconds[^1] : 0.0;
    }

    private static double LastEnd(double startTimestampSeconds, double durationSeconds) =>
        Math.Max(startTimestampSeconds, durationSeconds);

    private static string ToRelativePath(string outputDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string relative = Path.GetRelativePath(
            Path.GetFullPath(outputDirectory),
            Path.GetFullPath(path));

        return relative.Replace('\\', '/');
    }

    private static double RoundSeconds(double seconds) =>
        Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
}
