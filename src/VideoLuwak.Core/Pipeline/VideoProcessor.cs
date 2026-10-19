using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Pipeline;

/// <summary>
/// Orchestrates the full extraction pipeline for a single video file:
/// probe -&gt; audio streams -&gt; keyframes -&gt; pHash deduplication -&gt; summary JSON.
/// </summary>
/// <remarks>
/// Cancellation (<see cref="OperationCanceledException"/>) always propagates. Any other
/// failure is captured in a <see cref="ProcessingResult"/> with <c>Success == false</c> so
/// callers (UI and CLI) can inspect it instead of handling exceptions.
/// </remarks>
public sealed class VideoProcessor : IVideoProcessor
{
    private static readonly JsonSerializerOptions ResultSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IFfprobeService _ffprobe;
    private readonly IAudioExtractor _audioExtractor;
    private readonly IKeyframeExtractor _keyframeExtractor;
    private readonly IKeyframeDeduplicator _deduplicator;
    private readonly IPerceptualHasher _hasher;

    public VideoProcessor(
        IFfprobeService ffprobe,
        IAudioExtractor audioExtractor,
        IKeyframeExtractor keyframeExtractor,
        IKeyframeDeduplicator deduplicator,
        IPerceptualHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(ffprobe);
        ArgumentNullException.ThrowIfNull(audioExtractor);
        ArgumentNullException.ThrowIfNull(keyframeExtractor);
        ArgumentNullException.ThrowIfNull(deduplicator);
        ArgumentNullException.ThrowIfNull(hasher);

        _ffprobe = ffprobe;
        _audioExtractor = audioExtractor;
        _keyframeExtractor = keyframeExtractor;
        _deduplicator = deduplicator;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public async Task<ProcessingResult> ProcessAsync(
        string videoPath,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var outputDirectory = string.Empty;
        var keyframeManifestPath = string.Empty;
        var finalKeyframeManifestPath = string.Empty;
        ProbeResult? probe = null;
        IReadOnlyList<AudioStreamResult> audioResults = [];
        IReadOnlyList<string> keyframes = [];
        DeduplicationResult? deduplication = null;

        try
        {
            outputDirectory = OutputLayout.GetOutputDirectory(videoPath, options.OutputBaseDirectory);

            if (!File.Exists(videoPath))
            {
                return BuildResult(
                    videoPath, outputDirectory, probe, audioResults, keyframes, deduplication,
                    string.Empty, string.Empty,
                    options, stopwatch, success: false, error: $"Input file not found: {videoPath}");
            }

            Report(progress, videoPath, ProcessingStage.Starting, 0, 0, "Starting");

            PrepareOutputDirectories(outputDirectory, options);

            Report(progress, videoPath, ProcessingStage.Probing, 0, 5, "Probing media");
            probe = await _ffprobe.ProbeAsync(videoPath, cancellationToken).ConfigureAwait(false);

            audioResults = await _audioExtractor
                .ExtractAsync(probe, OutputLayout.GetAudioDirectory(outputDirectory), options, progress, cancellationToken)
                .ConfigureAwait(false);

            keyframes = await _keyframeExtractor
                .ExtractAsync(videoPath, OutputLayout.GetKeyframesDirectory(outputDirectory), options, progress, cancellationToken)
                .ConfigureAwait(false);

            if (options.DeduplicateKeyframes)
            {
                deduplication = _deduplicator.Deduplicate(
                    OutputLayout.GetKeyframesDirectory(outputDirectory),
                    OutputLayout.GetFinalKeyframesDirectory(outputDirectory),
                    options.PhashSimilarityThreshold,
                    _hasher,
                    progress,
                    cancellationToken);
            }

            IReadOnlyList<double> keyframeTimestamps = [];
            if (keyframes.Count > 0)
            {
                try
                {
                    keyframeTimestamps = await _ffprobe
                        .ProbeKeyframeTimestampsAsync(videoPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is FfmpegExecutionException or JsonException)
                {
                    // Timestamp probing is best-effort; the manifest falls back to estimated timestamps.
                }
            }

            var allManifest = KeyframeManifestWriter.BuildAll(
                videoPath,
                probe?.DurationSeconds ?? 0.0,
                keyframes,
                keyframeTimestamps,
                audioResults,
                outputDirectory);

            keyframeManifestPath = KeyframeManifestWriter.WriteAll(outputDirectory, allManifest);

            if (deduplication is not null)
            {
                var finalManifest = KeyframeManifestWriter.BuildFinal(
                    videoPath,
                    probe?.DurationSeconds ?? 0.0,
                    keyframes,
                    keyframeTimestamps,
                    audioResults,
                    outputDirectory,
                    deduplication);

                finalKeyframeManifestPath = KeyframeManifestWriter.WriteFinal(outputDirectory, finalManifest);
            }
            else
            {
                // A previous run's final manifest must not survive when de-duplication is disabled.
                DeleteFile(OutputLayout.GetFinalKeyframeManifestPath(outputDirectory));
            }

            var result = BuildResult(
                videoPath, outputDirectory, probe, audioResults, keyframes, deduplication,
                keyframeManifestPath, finalKeyframeManifestPath,
                options, stopwatch, success: true, error: null);

            WriteResult(outputDirectory, result);

            Report(progress, videoPath, ProcessingStage.Completed, 100, 100, "Completed");
            return result;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a failure: propagate it to the caller.
            throw;
        }
        catch (Exception ex)
        {
            var failed = BuildResult(
                videoPath, outputDirectory, probe, audioResults, keyframes, deduplication,
                string.Empty, string.Empty,
                options, stopwatch, success: false, error: ex.Message);

            WriteResultBestEffort(outputDirectory, failed);
            Report(progress, videoPath, ProcessingStage.Failed, -1, -1, ex.Message);
            return failed;
        }
    }

    private static void PrepareOutputDirectories(string outputDirectory, ProcessingOptions options)
    {
        if (options.Overwrite && Directory.Exists(outputDirectory))
        {
            // Only ever delete the three known subfolders, never the output root itself.
            DeleteSubdirectory(Path.Combine(outputDirectory, OutputLayout.AudioFolderName));
            DeleteSubdirectory(Path.Combine(outputDirectory, OutputLayout.KeyframesFolderName));
            DeleteSubdirectory(Path.Combine(outputDirectory, OutputLayout.FinalKeyframesFolderName));
            DeleteFile(OutputLayout.GetKeyframeManifestPath(outputDirectory));
            DeleteFile(OutputLayout.GetFinalKeyframeManifestPath(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(OutputLayout.GetAudioDirectory(outputDirectory));
        Directory.CreateDirectory(OutputLayout.GetKeyframesDirectory(outputDirectory));

        if (options.DeduplicateKeyframes)
        {
            Directory.CreateDirectory(OutputLayout.GetFinalKeyframesDirectory(outputDirectory));
        }
    }

    private static void DeleteSubdirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: stale folders that cannot be removed are overwritten in place.
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: cleanup failures must not fail processing.
        }
    }

    private static ProcessingResult BuildResult(
        string videoPath,
        string outputDirectory,
        ProbeResult? probe,
        IReadOnlyList<AudioStreamResult> audioResults,
        IReadOnlyList<string> keyframes,
        DeduplicationResult? deduplication,
        string keyframeManifestPath,
        string finalKeyframeManifestPath,
        ProcessingOptions options,
        Stopwatch stopwatch,
        bool success,
        string? error)
    {
        var originalSize = GetOriginalSize(videoPath, probe);
        var audioTotal = SumAudioSize(audioResults);
        var keyframesSize = SumFilesSize(keyframes);

        var hasOutputDirectory = !string.IsNullOrWhiteSpace(outputDirectory);
        var finalKeysize = options.DeduplicateKeyframes && hasOutputDirectory
            ? SumDirectorySize(OutputLayout.GetFinalKeyframesDirectory(outputDirectory))
            : 0;

        var totalOutput = audioTotal + finalKeysize;
        var reduction = originalSize > 0
            ? Math.Clamp((1.0 - totalOutput / (double)originalSize) * 100.0, 0, 100)
            : 0;

        return new ProcessingResult
        {
            VideoPath = videoPath,
            OutputDirectory = outputDirectory,
            OriginalSizeBytes = originalSize,
            DurationSeconds = probe?.DurationSeconds ?? 0,
            AudioStreams = audioResults,
            KeyframesExtracted = keyframes.Count,
            KeyframesSizeBytes = keyframesSize,
            KeyframesKept = deduplication?.KeptFiles ?? 0,
            KeyframesRemoved = deduplication?.RemovedFiles ?? 0,
            KeyframeManifestPath = keyframeManifestPath,
            FinalKeyframeManifestPath = finalKeyframeManifestPath,
            FinalKeyframesSizeBytes = finalKeysize,
            TotalOutputSizeBytes = totalOutput,
            SizeReductionPercent = reduction,
            ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
            Success = success,
            Error = error,
            CompletedAt = DateTimeOffset.Now,
        };
    }

    private static long GetOriginalSize(string videoPath, ProbeResult? probe)
    {
        if (probe is { FileSizeBytes: > 0 })
        {
            return probe.FileSizeBytes;
        }

        return GetFileSize(videoPath);
    }

    private static long SumAudioSize(IReadOnlyList<AudioStreamResult> audioResults)
    {
        long total = 0;
        foreach (var stream in audioResults)
        {
            total += stream.SizeBytes;
        }

        return total;
    }

    private static long SumFilesSize(IReadOnlyList<string> paths)
    {
        long total = 0;
        foreach (var path in paths)
        {
            total += GetFileSize(path);
        }

        return total;
    }

    private static long SumDirectorySize(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                total += GetFileSize(file);
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long GetFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void WriteResult(string outputDirectory, ProcessingResult result)
    {
        Directory.CreateDirectory(outputDirectory);
        var json = JsonSerializer.Serialize(result, ResultSerializerOptions);
        File.WriteAllText(OutputLayout.GetResultPath(outputDirectory), json);
    }

    private static void WriteResultBestEffort(string outputDirectory, ProcessingResult result)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return;
        }

        try
        {
            WriteResult(outputDirectory, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Failure reporting must not mask the original failure.
        }
    }

    private static void Report(
        IProgress<ProcessingProgress>? progress,
        string videoPath,
        ProcessingStage stage,
        double stagePercent,
        double overallPercent,
        string? message)
    {
        progress?.Report(new ProcessingProgress
        {
            VideoPath = videoPath,
            Stage = stage,
            StagePercent = stagePercent,
            OverallPercent = overallPercent,
            Message = message,
        });
    }
}
