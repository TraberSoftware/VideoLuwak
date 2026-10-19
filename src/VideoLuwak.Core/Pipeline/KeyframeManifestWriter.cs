using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Pipeline;

/// <summary>
/// Builds and writes <c>keyframes.json</c>, which pairs every extracted keyframe with
/// its source timestamp, de-duplication outcome, representative time range and the
/// extracted audio files that cover that range.
/// </summary>
public static class KeyframeManifestWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static KeyframeManifest Build(
        string videoPath,
        double durationSeconds,
        IReadOnlyList<string> extractedKeyframePaths,
        IReadOnlyList<double> timestampSeconds,
        IReadOnlyList<AudioStreamResult> audioStreams,
        string outputDirectory,
        DeduplicationResult? deduplication)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentNullException.ThrowIfNull(extractedKeyframePaths);
        ArgumentNullException.ThrowIfNull(timestampSeconds);
        ArgumentNullException.ThrowIfNull(audioStreams);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        double duration = RoundSeconds(Math.Max(0.0, durationSeconds));
        HashSet<string>? keptFileNames = deduplication is null
            ? null
            : new HashSet<string>(
                deduplication.KeptPaths.Select(path => Path.GetFileName(path) ?? string.Empty),
                StringComparer.Ordinal);

        int count = extractedKeyframePaths.Count;
        bool useProvidedTimestamps = timestampSeconds.Count == count;
        var entries = new List<KeyframeEntry>(count);
        double previousTimestamp = 0.0;

        for (int i = 0; i < count; i++)
        {
            double exactTimestamp = useProvidedTimestamps ? timestampSeconds[i] : double.NaN;
            double timestamp = useProvidedTimestamps
                ? exactTimestamp
                : EstimateTimestamp(i, count, duration, timestampSeconds);

            double roundedTimestamp = RoundSeconds(timestamp);
            timestamp = RoundSeconds(Math.Max(0.0, timestamp));
            if (i > 0 && timestamp < previousTimestamp)
            {
                timestamp = previousTimestamp;
            }

            previousTimestamp = timestamp;

            string fileName = Path.GetFileName(extractedKeyframePaths[i]);
            string sourceFile = OutputLayout.KeyframesFolderName + "/" + fileName;
            bool kept = keptFileNames is null || keptFileNames.Contains(fileName);

            entries.Add(new KeyframeEntry
            {
                Ordinal = i + 1,
                File = kept && keptFileNames is not null
                    ? OutputLayout.FinalKeyframesFolderName + "/" + fileName
                    : sourceFile,
                SourceFile = sourceFile,
                TimestampSeconds = timestamp,
                Timestamp = FormatTimestamp(timestamp),
                Kept = kept,
                TimestampEstimated = !useProvidedTimestamps || timestamp != roundedTimestamp,
            });
        }

        ApplyEndTimestamps(entries, duration);

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
            DurationSeconds = duration,
            TimeFormat = "HH:MM:SS.mmm",
            Audio = audio,
            Keyframes = entries,
        };
    }

    public static string Write(string outputDirectory, KeyframeManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        string path = Path.GetFullPath(OutputLayout.GetKeyframeManifestPath(outputDirectory));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string json = JsonSerializer.Serialize(manifest, SerializerOptions);
        File.WriteAllText(path, json);
        return path;
    }

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

    private static void ApplyEndTimestamps(List<KeyframeEntry> entries, double durationSeconds)
    {
        var keptIndices = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Kept)
            {
                keptIndices.Add(i);
            }
        }

        for (int i = 0; i < keptIndices.Count; i++)
        {
            int entryIndex = keptIndices[i];
            double? end = i + 1 < keptIndices.Count
                ? entries[keptIndices[i + 1]].TimestampSeconds
                : durationSeconds > 0.0
                    ? Math.Max(entries[entryIndex].TimestampSeconds, durationSeconds)
                    : null;

            entries[entryIndex] = entries[entryIndex] with
            {
                EndTimestampSeconds = end,
                EndTimestamp = end.HasValue ? FormatTimestamp(end.Value) : null,
            };
        }
    }

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
