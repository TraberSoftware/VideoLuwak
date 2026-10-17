using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Media;

/// <summary>
/// Extracts every audio stream of a probed container into an individual
/// uncompressed <c>.wav</c> file and writes a human-readable
/// <c>audio_streams.json</c> manifest alongside them.
/// </summary>
public sealed class AudioExtractor : IAudioExtractor
{
    private const int MinSampleRate = 8000;
    private const int MaxSampleRate = 192000;
    private const int DefaultSampleRate = 16000;
    private const int MinChannels = 1;
    private const int MaxChannels = 8;
    private const int DefaultChannels = 1;
    private const string PcmSampleFormat = "pcm_s16le";
    private const string LoudNormFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";

    private static readonly JsonSerializerOptions DescriptionSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public AudioExtractor(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<IReadOnlyList<AudioStreamResult>> ExtractAsync(
        ProbeResult probe,
        string audioDirectory,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioDirectory);
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(audioDirectory);

        var ffmpeg = _locator.ResolveFfmpeg();
        var streams = probe.AudioStreams.OrderBy(s => s.AudioOrdinal).ToList();
        var total = streams.Count;
        var results = new List<AudioStreamResult>(total);
        var completed = 0;
        var videoName = Path.GetFileName(probe.VideoPath);

        foreach (var stream in streams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputPath = BuildOutputPath(audioDirectory, stream);
            var duration = stream.DurationSeconds is > 0 ? stream.DurationSeconds.Value : probe.DurationSeconds;
            var parser = new FfmpegProgressParser(duration);
            var message = $"{videoName} - stream {stream.AudioOrdinal}/{total}";

            var outputSampleRate = ResolveOutputSampleRate(options, stream);
            var outputChannels = ResolveOutputChannels(options, stream);
            var arguments = BuildArguments(probe.VideoPath, stream, options, outputSampleRate, outputChannels, outputPath);

            var request = new ProcessRequest
            {
                FileName = ffmpeg,
                Arguments = arguments,
            };

            Action<string> onProgressLine = line =>
            {
                var percent = parser.Update(line);
                if (progress is null)
                {
                    return;
                }

                var fraction = percent < 0 ? 0 : percent / 100.0;
                var overall = total == 0 ? 0 : (completed + fraction) / total * 100.0;

                progress.Report(new ProcessingProgress
                {
                    VideoPath = probe.VideoPath,
                    Stage = ProcessingStage.ExtractingAudio,
                    StagePercent = percent,
                    OverallPercent = overall,
                    Message = message,
                });
            };

            var result = await _runner
                .RunAsync(request, onStdOutLine: onProgressLine, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                throw new FfmpegExecutionException(
                    $"ffmpeg failed with exit code {result.ExitCode} extracting stream {stream.AudioOrdinal} from '{probe.VideoPath}'.",
                    result.ExitCode,
                    result.StandardError);
            }

            results.Add(new AudioStreamResult
            {
                AudioOrdinal = stream.AudioOrdinal,
                StreamIndex = stream.Index,
                OutputPath = Path.GetFullPath(outputPath),
                SizeBytes = GetFileSize(outputPath),
                Description = stream.Description,
                Stream = stream,
                OutputSampleRate = outputSampleRate,
                OutputChannels = outputChannels,
                OutputSampleFormat = PcmSampleFormat,
                SttOptimized = options.SttOptimizeAudio,
            });

            completed++;
        }

        await WriteDescriptionAsync(probe, audioDirectory, results, options, cancellationToken).ConfigureAwait(false);
        return results;
    }

    private static string BuildOutputPath(string audioDirectory, AudioStreamInfo stream)
    {
        var language = string.IsNullOrWhiteSpace(stream.Language)
            ? "und"
            : OutputLayout.SanitizeName(stream.Language!);

        var name = $"audio_{stream.AudioOrdinal:D2}_{language}";
        if (!string.IsNullOrWhiteSpace(stream.Title))
        {
            name += "_" + OutputLayout.SanitizeName(stream.Title!);
        }

        return Path.Combine(audioDirectory, name + ".wav");
    }

    private static IReadOnlyList<string> BuildArguments(
        string videoPath,
        AudioStreamInfo stream,
        ProcessingOptions options,
        int outputSampleRate,
        int outputChannels,
        string outputPath)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-i", videoPath,
            "-map", $"0:{stream.Index}",
            "-vn",
            "-sn",
            "-dn",
        };

        var filterChain = BuildFilterChain(options);
        if (filterChain is not null)
        {
            arguments.Add("-af");
            arguments.Add(filterChain);
        }

        if (options.SttOptimizeAudio)
        {
            arguments.Add("-ac");
            arguments.Add(outputChannels.ToString(CultureInfo.InvariantCulture));
            arguments.Add("-ar");
            arguments.Add(outputSampleRate.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-c:a");
        arguments.Add(PcmSampleFormat);
        arguments.Add("-f");
        arguments.Add("wav");
        arguments.Add("-progress");
        arguments.Add("pipe:1");
        arguments.Add("-nostats");
        arguments.Add(outputPath);

        return arguments;
    }

    private static string? BuildFilterChain(ProcessingOptions options)
    {
        var filters = new List<string>(2);

        if (options.AudioHighPassHz > 0)
        {
            filters.Add($"highpass=f={options.AudioHighPassHz.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.AudioNormalizeLoudness)
        {
            filters.Add(LoudNormFilter);
        }

        return filters.Count == 0 ? null : string.Join(",", filters);
    }

    private static int ResolveOutputSampleRate(ProcessingOptions options, AudioStreamInfo stream)
    {
        if (options.SttOptimizeAudio)
        {
            return ClampSampleRate(options.AudioSampleRate);
        }

        return stream.SampleRate > 0 ? stream.SampleRate : 0;
    }

    private static int ResolveOutputChannels(ProcessingOptions options, AudioStreamInfo stream)
    {
        if (options.SttOptimizeAudio)
        {
            return ClampChannels(options.AudioChannels);
        }

        return stream.Channels > 0 ? stream.Channels : 0;
    }

    private static int ClampSampleRate(int sampleRate) =>
        sampleRate is >= MinSampleRate and <= MaxSampleRate ? sampleRate : DefaultSampleRate;

    private static int ClampChannels(int channels) =>
        channels is >= MinChannels and <= MaxChannels ? channels : DefaultChannels;

    private static string DescribeOutput(AudioStreamResult result)
    {
        var sampleRate = result.OutputSampleRate > 0
            ? FormatKiloHertz(result.OutputSampleRate)
            : "source rate";

        var channels = result.OutputChannels switch
        {
            1 => "mono",
            2 => "stereo",
            > 2 => $"{result.OutputChannels.ToString(CultureInfo.InvariantCulture)} channels",
            _ => "source channels",
        };

        var description = $"{sampleRate}, {channels}, 16-bit PCM";
        return result.SttOptimized ? description + " (STT)" : description;
    }

    private static string FormatKiloHertz(int hertz) =>
        (hertz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";

    private static async Task WriteDescriptionAsync(
        ProbeResult probe,
        string audioDirectory,
        IReadOnlyList<AudioStreamResult> results,
        ProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var entries = results
            .Select(result => new AudioDescriptionEntry(
                Ordinal: result.AudioOrdinal,
                StreamIndex: result.StreamIndex,
                Codec: result.Stream.Codec,
                Channels: result.Stream.Channels,
                SampleRate: result.Stream.SampleRate,
                Language: result.Stream.Language,
                Title: result.Stream.Title,
                Description: result.Description,
                File: Path.GetFileName(result.OutputPath),
                SizeBytes: result.SizeBytes,
                OutputSampleRate: result.OutputSampleRate,
                OutputChannels: result.OutputChannels,
                OutputSampleFormat: result.OutputSampleFormat,
                SttOptimized: result.SttOptimized,
                OutputDescription: DescribeOutput(result)))
            .ToList();

        var target = new AudioTargetDescription(
            SampleRate: ClampSampleRate(options.AudioSampleRate),
            Channels: ClampChannels(options.AudioChannels),
            SampleFormat: PcmSampleFormat);

        var document = new AudioDescriptionDocument(
            Path.GetFullPath(probe.VideoPath),
            entries,
            SttOptimized: options.SttOptimizeAudio,
            Target: target);
        var json = JsonSerializer.Serialize(document, DescriptionSerializerOptions);

        var descriptionPath = Path.Combine(audioDirectory, OutputLayout.AudioDescriptionFileName);
        await File.WriteAllTextAsync(descriptionPath, json, cancellationToken).ConfigureAwait(false);
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

    private sealed record AudioDescriptionDocument(
        [property: JsonPropertyName("video")] string Video,
        [property: JsonPropertyName("streams")] IReadOnlyList<AudioDescriptionEntry> Streams,
        [property: JsonPropertyName("sttOptimized")] bool SttOptimized,
        [property: JsonPropertyName("target")] AudioTargetDescription Target);

    private sealed record AudioTargetDescription(
        [property: JsonPropertyName("sampleRate")] int SampleRate,
        [property: JsonPropertyName("channels")] int Channels,
        [property: JsonPropertyName("sampleFormat")] string SampleFormat);

    private sealed record AudioDescriptionEntry(
        [property: JsonPropertyName("ordinal")] int Ordinal,
        [property: JsonPropertyName("streamIndex")] int StreamIndex,
        [property: JsonPropertyName("codec")] string Codec,
        [property: JsonPropertyName("channels")] int Channels,
        [property: JsonPropertyName("sampleRate")] int SampleRate,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("outputSampleRate")] int OutputSampleRate,
        [property: JsonPropertyName("outputChannels")] int OutputChannels,
        [property: JsonPropertyName("outputSampleFormat")] string OutputSampleFormat,
        [property: JsonPropertyName("sttOptimized")] bool SttOptimized,
        [property: JsonPropertyName("outputDescription")] string OutputDescription);
}
