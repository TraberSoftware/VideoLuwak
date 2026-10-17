using System.Globalization;

namespace VideoLuwak.Core.Cli;

/// <summary>
/// Parses raw command-line arguments into <see cref="CliOptions"/>.
/// Never throws on bad input; instead returns a <see cref="CliParseResult"/> with
/// default options and a human-readable <see cref="CliParseResult.Error"/>.
/// </summary>
public static class CliParser
{
    private const int MinJpegQuality = 1;
    private const int MaxJpegQuality = 31;

    /// <summary>Parses <paramref name="args"/> into options or an actionable error.</summary>
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var inputs = new List<string>();
        string? output = null;
        string? ffmpeg = null;
        string? ffprobe = null;
        int? threshold = null;
        string? format = null;
        int? quality = null;
        bool? deduplicate = null;
        bool? sttOptimize = null;
        int? audioRate = null;
        int? audioChannels = null;
        int? highPass = null;
        bool? loudNorm = null;
        var showHelp = false;
        var showVersion = false;
        var positionalOnly = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (positionalOnly || !IsOption(arg))
            {
                inputs.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                positionalOnly = true;
                continue;
            }

            var name = arg;
            string? inlineValue = null;
            var equalsIndex = arg.IndexOf('=');
            if (equalsIndex >= 0)
            {
                name = arg[..equalsIndex];
                inlineValue = arg[(equalsIndex + 1)..];
            }

            switch (name)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "--version":
                    showVersion = true;
                    break;

                case "-o":
                case "--output":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var outputValue, out var outputError))
                    {
                        return Fail(outputError!);
                    }

                    output = outputValue;
                    break;

                case "-t":
                case "--threshold":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var thresholdValue, out var thresholdError))
                    {
                        return Fail(thresholdError!);
                    }

                    if (!int.TryParse(thresholdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedThreshold)
                        || parsedThreshold < 0)
                    {
                        return Fail($"Option '{name}' expects a non-negative integer (got '{thresholdValue}').");
                    }

                    threshold = parsedThreshold;
                    break;

                case "--format":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var formatValue, out var formatError))
                    {
                        return Fail(formatError!);
                    }

                    var normalizedFormat = formatValue.Trim().ToLowerInvariant();
                    if (normalizedFormat is not ("jpg" or "png"))
                    {
                        return Fail($"Option '--format' expects 'jpg' or 'png' (got '{formatValue}').");
                    }

                    format = normalizedFormat;
                    break;

                case "--quality":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var qualityValue, out var qualityError))
                    {
                        return Fail(qualityError!);
                    }

                    if (!int.TryParse(qualityValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuality)
                        || parsedQuality < MinJpegQuality
                        || parsedQuality > MaxJpegQuality)
                    {
                        return Fail($"Option '--quality' expects an integer between {MinJpegQuality} and {MaxJpegQuality} (got '{qualityValue}').");
                    }

                    quality = parsedQuality;
                    break;

                case "--no-dedup":
                    deduplicate = false;
                    break;

                case "--dedup":
                    deduplicate = true;
                    break;

                case "--stt-audio":
                    sttOptimize = true;
                    break;

                case "--no-stt-audio":
                    sttOptimize = false;
                    break;

                case "--audio-rate":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var rateValue, out var rateError))
                    {
                        return Fail(rateError!);
                    }

                    if (!int.TryParse(rateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRate)
                        || parsedRate <= 0)
                    {
                        return Fail($"Option '--audio-rate' expects a positive integer in Hz (got '{rateValue}').");
                    }

                    audioRate = parsedRate;
                    break;

                case "--audio-channels":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var channelsValue, out var channelsError))
                    {
                        return Fail(channelsError!);
                    }

                    if (!int.TryParse(channelsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedChannels)
                        || parsedChannels < 1)
                    {
                        return Fail($"Option '--audio-channels' expects an integer >= 1 (got '{channelsValue}').");
                    }

                    audioChannels = parsedChannels;
                    break;

                case "--mono":
                    audioChannels = 1;
                    break;

                case "--stereo":
                    audioChannels = 2;
                    break;

                case "--highpass":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var highPassValue, out var highPassError))
                    {
                        return Fail(highPassError!);
                    }

                    if (!int.TryParse(highPassValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedHighPass)
                        || parsedHighPass < 0)
                    {
                        return Fail($"Option '--highpass' expects a non-negative integer in Hz (got '{highPassValue}').");
                    }

                    highPass = parsedHighPass;
                    break;

                case "--loudnorm":
                    loudNorm = true;
                    break;

                case "--no-loudnorm":
                    loudNorm = false;
                    break;

                case "--ffmpeg":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var ffmpegValue, out var ffmpegError))
                    {
                        return Fail(ffmpegError!);
                    }

                    ffmpeg = ffmpegValue;
                    break;

                case "--ffprobe":
                    if (!TryReadValue(args, ref i, name, inlineValue, out var ffprobeValue, out var ffprobeError))
                    {
                        return Fail(ffprobeError!);
                    }

                    ffprobe = ffprobeValue;
                    break;

                default:
                    return Fail($"Unknown option '{arg}'. Run 'VideoLuwak --help' for usage.");
            }
        }

        var options = new CliOptions
        {
            InputPaths = inputs,
            OutputBaseDirectory = output,
            FfmpegPath = ffmpeg,
            FfprobePath = ffprobe,
            PhashSimilarityThreshold = threshold,
            KeyframeFormat = format,
            JpegQuality = quality,
            DeduplicateKeyframes = deduplicate,
            SttOptimizeAudio = sttOptimize,
            AudioSampleRate = audioRate,
            AudioChannels = audioChannels,
            AudioHighPassHz = highPass,
            AudioNormalizeLoudness = loudNorm,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
        };

        return new CliParseResult(options, null);
    }

    /// <summary>Returns the multi-line help / usage text.</summary>
    public static string GetUsage()
    {
        return
"""
VideoLuwak - portable ffmpeg-based video keyframe & audio extractor

Usage:
  VideoLuwak [options] <video> [<video> ...]
  VideoLuwak --gui

Options:
  -o, --output <dir>        Root directory for output folders (default: alongside each video).
  -t, --threshold <int>     pHash Hamming distance for duplicate keyframes, >= 0 (default 8).
      --format <jpg|png>    Keyframe image format (default jpg).
      --quality <int>       JPEG quality 1..31, lower is better (default 2).
      --no-dedup            Skip the deduplicated final_keyframes folder.
      --dedup               Enable keyframe deduplication (default).
      --no-stt-audio        Keep the source channel layout and sample rate.
      --stt-audio           Downmix/resample audio for speech-to-text (default).
      --audio-rate <hz>     STT target sample rate (default 16000).
      --audio-channels <n>  STT target channel count, >= 1 (default 1).
      --mono                Shorthand for --audio-channels 1.
      --stereo              Shorthand for --audio-channels 2.
      --highpass <hz>       High-pass filter cutoff, >= 0; 0 disables (default 0).
      --loudnorm            Apply EBU R128 loudness normalization (default off).
      --no-loudnorm         Disable loudness normalization (default).
      --ffmpeg <path>       Explicit ffmpeg executable path.
      --ffprobe <path>      Explicit ffprobe executable path.
  -h, --help                Show this help and exit.
      --version             Show version information and exit.

Default audio output is mono 16 kHz 16-bit PCM (pcm_s16le) for speech-to-text.

Examples:
  VideoLuwak movie.mkv
  VideoLuwak movie.mkv -o D:\Extracts --threshold 6
  VideoLuwak a.mp4 b.mp4 --format png --no-dedup
""";
    }

    private static bool IsOption(string arg) =>
        arg.Length > 1 && arg[0] == '-';

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option,
        string? inlineValue,
        out string value,
        out string? error)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            error = null;
            return true;
        }

        if (index + 1 >= args.Count)
        {
            value = string.Empty;
            error = $"Option '{option}' requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static CliParseResult Fail(string error) => new(new CliOptions(), error);
}
