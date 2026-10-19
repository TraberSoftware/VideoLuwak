using System.Globalization;
using System.Reflection;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Config;
using VideoLuwak.Core.Ffmpeg;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Cli;

/// <summary>
/// Executes the command-line workflow: parse -&gt; merge settings -&gt; process each video
/// sequentially -&gt; print per-file progress/summaries and an overall summary.
/// </summary>
public sealed class CliRunner
{
    private const int MaxDisplayNameLength = 32;

    private readonly IVideoProcessor _processor;
    private readonly SettingsStore _settings;
    private readonly IFfmpegSetupService _setup;

    public CliRunner(IVideoProcessor processor, SettingsStore settings, IFfmpegSetupService? setup = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(settings);

        _processor = processor;
        _settings = settings;
        _setup = setup ?? new FfmpegSetupService();
    }

    /// <summary>True when the arguments represent a CLI invocation (non-empty and no --gui).</summary>
    public static bool IsCliRequest(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return false;
        }

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--gui", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parse = CliParser.Parse(args);

        if (parse.Error is not null)
        {
            await error.WriteLineAsync(parse.Error).ConfigureAwait(false);
            await error.WriteLineAsync().ConfigureAwait(false);
            await error.WriteLineAsync(CliParser.GetUsage()).ConfigureAwait(false);
            return 2;
        }

        var cli = parse.Options;

        if (cli.ShowHelp)
        {
            await output.WriteLineAsync(CliParser.GetUsage()).ConfigureAwait(false);
            return 0;
        }

        if (cli.ShowVersion)
        {
            await output.WriteLineAsync(GetVersion()).ConfigureAwait(false);
            return 0;
        }

        if (cli.SetupFfmpeg)
        {
            return await RunSetupAsync(output, error, cancellationToken).ConfigureAwait(false) ? 0 : 1;
        }

        if (cli.InputPaths.Count == 0)
        {
            await output.WriteLineAsync(CliParser.GetUsage()).ConfigureAwait(false);
            return 2;
        }

        if (!_setup.IsAvailable)
        {
            if (!await RunSetupAsync(output, error, cancellationToken).ConfigureAwait(false))
            {
                return 1;
            }
        }

        var options = BuildOptions(cli);
        var succeeded = 0;
        var failed = 0;

        for (var i = 0; i < cli.InputPaths.Count; i++)
        {
            var result = await RunSingleAsync(
                cli.InputPaths[i],
                i + 1,
                cli.InputPaths.Count,
                options,
                output,
                error,
                cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync($"Done: {succeeded} succeeded, {failed} failed.").ConfigureAwait(false);

        return failed == 0 ? 0 : 1;
    }

    private async Task<bool> RunSetupAsync(TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var writeGate = new object();
        var progress = new InlineProgress<string>(line =>
        {
            lock (writeGate)
            {
                output.WriteLine(line);
            }
        });

        var result = await _setup.EnsureAvailableAsync(progress, cancellationToken).ConfigureAwait(false);
        if (result.IsReady)
        {
            await output.WriteLineAsync("FFmpeg and FFprobe are ready.").ConfigureAwait(false);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            await error.WriteLineAsync(result.Message).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<ProcessingResult> RunSingleAsync(
        string input,
        int index,
        int total,
        ProcessingOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var displayName = Truncate(SafeFileName(input), MaxDisplayNameLength);
        ProcessingStage? lastStage = null;
        var wroteProgress = false;

        var progress = new InlineProgress<ProcessingProgress>(report =>
        {
            if (lastStage != report.Stage)
            {
                if (wroteProgress)
                {
                    output.WriteLine();
                }

                lastStage = report.Stage;
            }

            var percent = double.IsNaN(report.OverallPercent)
                ? 0
                : Math.Clamp(report.OverallPercent, 0, 100);

            output.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"\r[{index}/{total}] {displayName}  {StageLabel(report.Stage)}  {percent,3:0}%"));
            wroteProgress = true;
        });

        ProcessingResult result;
        try
        {
            result = await _processor
                .ProcessAsync(input, options, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FfmpegNotFoundException ex)
        {
            if (wroteProgress)
            {
                output.WriteLine();
            }

            await error.WriteLineAsync($"FFmpeg/FFprobe could not be located: {ex.Message}").ConfigureAwait(false);
            return new ProcessingResult
            {
                VideoPath = input,
                Success = false,
                Error = ex.Message,
                CompletedAt = DateTimeOffset.Now,
            };
        }

        if (wroteProgress)
        {
            output.WriteLine();
        }

        await WriteSummaryAsync(result, input, output, error).ConfigureAwait(false);
        return result;
    }

    private static async Task WriteSummaryAsync(ProcessingResult result, string input, TextWriter output, TextWriter error)
    {
        var separator = new string('-', 60);
        await output.WriteLineAsync(separator).ConfigureAwait(false);
        await output.WriteLineAsync($"Source:            {input}").ConfigureAwait(false);
        await output.WriteLineAsync($"Output:            {result.OutputDirectory}").ConfigureAwait(false);
        await output.WriteLineAsync($"Status:            {(result.Success ? "OK" : "FAILED")}").ConfigureAwait(false);
        await output.WriteLineAsync($"Duration:          {FormatSeconds(result.DurationSeconds)}").ConfigureAwait(false);

        if (!result.Success)
        {
            await output.WriteLineAsync($"Error:             {result.Error}").ConfigureAwait(false);
            await output.WriteLineAsync(separator).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                await error.WriteLineAsync($"{SafeFileName(input)}: {result.Error}").ConfigureAwait(false);
            }

            return;
        }

        await output.WriteLineAsync($"Audio streams:     {result.AudioStreams.Count}").ConfigureAwait(false);
        foreach (var stream in result.AudioStreams)
        {
            await output.WriteLineAsync(
                $"  - {SafeFileName(stream.OutputPath)}  |  {DescribeOutput(stream)}  |  {stream.Description}").ConfigureAwait(false);
        }

        await output.WriteLineAsync(
            $"Keyframes:         {result.KeyframesExtracted} extracted, {result.KeyframesKept} kept, {result.KeyframesRemoved} removed").ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result.KeyframeManifestPath))
        {
            await output.WriteLineAsync($"Keyframe manifest:       {result.KeyframeManifestPath}").ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(result.FinalKeyframeManifestPath))
        {
            await output.WriteLineAsync($"Final keyframe manifest: {result.FinalKeyframeManifestPath}").ConfigureAwait(false);
        }

        await output.WriteLineAsync("Sizes:").ConfigureAwait(false);
        await output.WriteLineAsync($"  Original:        {FormatSize(result.OriginalSizeBytes)}").ConfigureAwait(false);
        await output.WriteLineAsync($"  Audio total:     {FormatSize(SumAudioSize(result.AudioStreams))}").ConfigureAwait(false);
        await output.WriteLineAsync($"  Final keyframes: {FormatSize(result.FinalKeyframesSizeBytes)}").ConfigureAwait(false);
        await output.WriteLineAsync($"  Total output:    {FormatSize(result.TotalOutputSizeBytes)}").ConfigureAwait(false);
        await output.WriteLineAsync($"  Size reduction:  {FormatPercent(result.SizeReductionPercent)}").ConfigureAwait(false);
        await output.WriteLineAsync(separator).ConfigureAwait(false);
    }

    private ProcessingOptions BuildOptions(CliOptions cli)
    {
        var settings = _settings.Load();
        var options = settings.ToProcessingOptions(cli.OutputBaseDirectory);

        return options with
        {
            PhashSimilarityThreshold = cli.PhashSimilarityThreshold ?? options.PhashSimilarityThreshold,
            KeyframeFormat = cli.KeyframeFormat ?? options.KeyframeFormat,
            JpegQuality = cli.JpegQuality ?? options.JpegQuality,
            DeduplicateKeyframes = cli.DeduplicateKeyframes ?? options.DeduplicateKeyframes,
            SttOptimizeAudio = cli.SttOptimizeAudio ?? options.SttOptimizeAudio,
            AudioSampleRate = cli.AudioSampleRate ?? options.AudioSampleRate,
            AudioChannels = cli.AudioChannels ?? options.AudioChannels,
            AudioHighPassHz = cli.AudioHighPassHz ?? options.AudioHighPassHz,
            AudioNormalizeLoudness = cli.AudioNormalizeLoudness ?? options.AudioNormalizeLoudness,
            FfmpegPath = cli.FfmpegPath,
            FfprobePath = cli.FfprobePath,
        };
    }

    private static string GetVersion()
    {
        var assembly = typeof(CliRunner).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return !string.IsNullOrWhiteSpace(informational)
            ? informational
            : assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    private static string StageLabel(ProcessingStage stage) => stage switch
    {
        ProcessingStage.Starting => "Starting",
        ProcessingStage.Probing => "Probing",
        ProcessingStage.ExtractingAudio => "Extracting audio",
        ProcessingStage.ExtractingKeyframes => "Extracting keyframes",
        ProcessingStage.DeduplicatingKeyframes => "Deduplicating keyframes",
        ProcessingStage.Completed => "Completed",
        ProcessingStage.Failed => "Failed",
        _ => stage.ToString(),
    };

    private static string DescribeOutput(AudioStreamResult stream)
    {
        var sampleRate = stream.OutputSampleRate > 0
            ? FormatKiloHertz(stream.OutputSampleRate)
            : "source rate";

        var channels = stream.OutputChannels switch
        {
            1 => "mono",
            2 => "stereo",
            > 2 => $"{stream.OutputChannels.ToString(CultureInfo.InvariantCulture)}ch",
            _ => "source ch",
        };

        var format = string.IsNullOrWhiteSpace(stream.OutputSampleFormat)
            ? "pcm_s16le"
            : stream.OutputSampleFormat;

        var description = $"{sampleRate} {channels} {format}";
        return stream.SttOptimized ? description + " (STT)" : description;
    }

    private static string FormatKiloHertz(int hertz) =>
        (hertz / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";

    private static long SumAudioSize(IReadOnlyList<AudioStreamResult> streams)
    {
        long total = 0;
        foreach (var stream in streams)
        {
            total += stream.SizeBytes;
        }

        return total;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;
        var culture = CultureInfo.InvariantCulture;

        if (bytes >= gb)
        {
            return (bytes / gb).ToString("0.00", culture) + " GB";
        }

        if (bytes >= mb)
        {
            return (bytes / mb).ToString("0.00", culture) + " MB";
        }

        if (bytes >= kb)
        {
            return (bytes / kb).ToString("0.00", culture) + " KB";
        }

        return bytes.ToString(culture) + " B";
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    private static string FormatPercent(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + " %";

    private static string SafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>An <see cref="IProgress{T}"/> that invokes its handler synchronously.</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
