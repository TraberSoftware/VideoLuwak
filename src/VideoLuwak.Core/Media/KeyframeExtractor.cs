using System.Globalization;
using System.Text;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Media;

/// <summary>
/// Extracts true video keyframes (I-frames) by running ffmpeg with
/// <c>-skip_frame nokey</c>. Output files use a zero-padded <c>kf_%06d</c> numbering
/// starting at 1 so that ascending ordinal filename order equals chronological order,
/// which the pHash deduplicator relies upon.
/// </summary>
public sealed class KeyframeExtractor : IKeyframeExtractor
{
    private const string FileNameFormat = "kf_%06d";
    private const int MaxCapturedStderrChars = 16 * 1024;

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public KeyframeExtractor(IFfmpegLocator locator, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);

        _locator = locator;
        _runner = runner;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ExtractAsync(
        string videoPath,
        string keyframesDirectory,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyframesDirectory);
        ArgumentNullException.ThrowIfNull(options);

        Directory.CreateDirectory(keyframesDirectory);

        string format = NormalizeFormat(options.KeyframeFormat);
        string outputPattern = Path.Combine(keyframesDirectory, $"{FileNameFormat}.{format}");

        // Propagates FfmpegNotFoundException when ffmpeg cannot be located.
        string ffmpeg = _locator.ResolveFfmpeg();

        var arguments = BuildArguments(videoPath, outputPattern, format, options.JpegQuality);

        int frames = 0;
        double outTimeSeconds = 0.0;
        double durationSeconds = 0.0;
        var stderr = new StringBuilder();

        void ReportProgress()
        {
            if (progress is null)
            {
                return;
            }

            double stagePercent = durationSeconds > 0.0
                ? Math.Clamp(outTimeSeconds / durationSeconds * 100.0, 0.0, 100.0)
                : -1.0;

            progress.Report(new ProcessingProgress
            {
                VideoPath = videoPath,
                Stage = ProcessingStage.ExtractingKeyframes,
                StagePercent = stagePercent,
                OverallPercent = stagePercent,
                Message = $"Extracting keyframes ({frames} so far)",
            });
        }

        // ffmpeg's -progress stream is newline-delimited key=value pairs on stdout.
        void OnStdOutLine(string line)
        {
            ReadOnlySpan<char> span = line.AsSpan();
            int separator = span.IndexOf('=');
            if (separator <= 0)
            {
                return;
            }

            ReadOnlySpan<char> key = span[..separator];
            ReadOnlySpan<char> value = span[(separator + 1)..];

            if (key.SequenceEqual("frame"))
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFrames))
                {
                    frames = parsedFrames;
                }

                ReportProgress();
            }
            else if (key.SequenceEqual("out_time_us") || key.SequenceEqual("out_time_ms"))
            {
                // ffmpeg reports both keys in microseconds.
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds) &&
                    microseconds >= 0)
                {
                    outTimeSeconds = microseconds / 1_000_000.0;
                }

                ReportProgress();
            }
            else if (key.SequenceEqual("out_time"))
            {
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsedTime) &&
                    parsedTime > TimeSpan.Zero)
                {
                    outTimeSeconds = parsedTime.TotalSeconds;
                }

                ReportProgress();
            }
            else if (key.SequenceEqual("progress"))
            {
                if (value.SequenceEqual("end") && outTimeSeconds <= 0.0 && durationSeconds > 0.0)
                {
                    outTimeSeconds = durationSeconds; // Guarantee a final 100% report.
                }

                ReportProgress();
            }
        }

        // stderr carries the container duration line and any error diagnostics.
        void OnStdErrLine(string line)
        {
            if (durationSeconds <= 0.0)
            {
                double parsed = ParseDurationSeconds(line);
                if (parsed > 0.0)
                {
                    durationSeconds = parsed;
                }
            }

            if (stderr.Length < MaxCapturedStderrChars)
            {
                stderr.AppendLine(line);
            }
        }

        var request = new ProcessRequest
        {
            FileName = ffmpeg,
            Arguments = arguments,
        };

        ProcessResult result = await _runner
            .RunAsync(request, OnStdOutLine, OnStdErrLine, cancellationToken)
            .ConfigureAwait(false);

        string[] produced = Directory.GetFiles(keyframesDirectory);
        Array.Sort(produced, StringComparer.Ordinal);

        if (!result.Succeeded)
        {
            // Some ffmpeg builds exit non-zero when no frame could be written at all.
            // Treat "no output files and explicit no-frames diagnostics" as empty success.
            if (produced.Length == 0 && IndicatesNoFrames(stderr.ToString()))
            {
                return [];
            }

            throw new FfmpegExecutionException(
                $"ffmpeg keyframe extraction failed for '{videoPath}' (exit code {result.ExitCode}).",
                result.ExitCode,
                result.StandardError);
        }

        return produced;
    }

    private static IReadOnlyList<string> BuildArguments(
        string videoPath,
        string outputPattern,
        string format,
        int jpegQuality)
    {
        var arguments = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-skip_frame", "nokey",
            "-i", videoPath,
            "-an",
            "-sn",
            "-dn",
            "-map", "0:v:0",
            "-fps_mode", "passthrough",
            "-f", "image2",
        };

        if (format == "png")
        {
            arguments.Add("-compression_level");
            arguments.Add("3");
        }
        else
        {
            arguments.Add("-q:v");
            arguments.Add(jpegQuality.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add("-start_number");
        arguments.Add("1");
        arguments.Add(outputPattern);
        arguments.Add("-progress");
        arguments.Add("pipe:1");
        arguments.Add("-nostats");

        return arguments;
    }

    /// <summary>Maps an arbitrary configured format to a supported image extension.</summary>
    private static string NormalizeFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format) && format.Trim().Equals("png", StringComparison.OrdinalIgnoreCase)
            ? "png"
            : "jpg";

    /// <summary>
    /// Extracts the container duration from an ffmpeg stderr header line such as
    /// <c>  Duration: 00:00:08.00, start: 0.000000, bitrate: 61 kb/s</c>.
    /// </summary>
    private static double ParseDurationSeconds(string line)
    {
        const string marker = "Duration:";
        int index = line.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return 0.0;
        }

        ReadOnlySpan<char> remainder = line.AsSpan(index + marker.Length).Trim();
        int comma = remainder.IndexOf(',');
        if (comma >= 0)
        {
            remainder = remainder[..comma];
        }

        remainder = remainder.Trim();
        return TimeSpan.TryParse(remainder, CultureInfo.InvariantCulture, out TimeSpan duration) &&
               duration > TimeSpan.Zero
            ? duration.TotalSeconds
            : 0.0;
    }

    /// <summary>Detects ffmpeg diagnostics signalling that no frames were written.</summary>
    private static bool IndicatesNoFrames(string stderr) =>
        stderr.Contains("nothing was encoded", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("Output file is empty", StringComparison.OrdinalIgnoreCase) ||
        stderr.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase);
}
