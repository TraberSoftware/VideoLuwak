using System.Globalization;
using System.Text;
using System.Text.Json;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Ffmpeg;

/// <summary>
/// Inspects media containers by invoking <c>ffprobe -print_format json</c> and
/// mapping the JSON into <see cref="ProbeResult"/>.
/// </summary>
public sealed class FfprobeService : IFfprobeService
{
    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public FfprobeService(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<ProbeResult> ProbeAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var ffprobe = _locator.ResolveFfprobe();
        var request = new ProcessRequest
        {
            FileName = ffprobe,
            Arguments =
            [
                "-v", "quiet",
                "-print_format", "json",
                "-show_format",
                "-show_streams",
                videoPath,
            ],
        };

        var result = await _runner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new FfmpegExecutionException(
                $"FFprobe failed with exit code {result.ExitCode} while probing '{videoPath}'.",
                result.ExitCode,
                result.StandardError);
        }

        return Parse(videoPath, result.StandardOutput);
    }

    public async Task<IReadOnlyList<double>> ProbeKeyframeTimestampsAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var ffprobe = _locator.ResolveFfprobe();
        var request = new ProcessRequest
        {
            FileName = ffprobe,
            Arguments =
            [
                "-v", "quiet",
                "-select_streams", "v:0",
                "-skip_frame", "nokey",
                "-show_frames",
                "-show_entries", "frame=key_frame,best_effort_timestamp_time,pts_time,pkt_dts_time:format=start_time",
                "-of", "json",
                videoPath,
            ],
        };

        var result = await _runner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new FfmpegExecutionException(
                $"FFprobe failed with exit code {result.ExitCode} while probing keyframe timestamps for '{videoPath}'.",
                result.ExitCode,
                result.StandardError);
        }

        return ParseKeyframeTimestamps(result.StandardOutput);
    }

    internal static IReadOnlyList<double> ParseKeyframeTimestamps(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("frames", out var frames) ||
            frames.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        double startTimeSeconds = 0.0;
        if (document.RootElement.TryGetProperty("format", out var format) &&
            format.ValueKind == JsonValueKind.Object)
        {
            startTimeSeconds = GetDouble(format, "start_time") ?? 0.0;
        }

        var timestamps = new List<double>();
        foreach (var frame in frames.EnumerateArray())
        {
            if (GetLong(frame, "key_frame") != 1)
            {
                continue;
            }

            var timestamp = GetDouble(frame, "best_effort_timestamp_time") ??
                            GetDouble(frame, "pts_time") ??
                            GetDouble(frame, "pkt_dts_time");

            if (timestamp.HasValue)
            {
                timestamps.Add(Math.Max(0.0, timestamp.Value - startTimeSeconds));
            }
        }

        return timestamps;
    }

    private static ProbeResult Parse(string videoPath, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var hasStreams = root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array;
        var streamCount = hasStreams ? streams.GetArrayLength() : 0;

        var duration = GetFormatDuration(root);
        var fileSize = GetFileSize(videoPath);

        string? videoCodec = null;
        int? width = null;
        int? height = null;
        double? frameRate = null;
        var audioStreams = new List<AudioStreamInfo>();

        if (hasStreams)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = GetString(stream, "codec_type");
                if (videoCodec is null && string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
                {
                    videoCodec = GetString(stream, "codec_name");
                    width = GetInt(stream, "width");
                    height = GetInt(stream, "height");
                    frameRate = ParseFrameRate(GetString(stream, "avg_frame_rate"));
                    continue;
                }

                if (string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    audioStreams.Add(BuildAudioStream(stream, audioStreams.Count + 1));
                }
            }

            if (duration <= 0)
            {
                duration = GetFallbackDuration(streams);
            }
        }

        return new ProbeResult
        {
            VideoPath = Path.GetFullPath(videoPath),
            FileSizeBytes = fileSize,
            DurationSeconds = duration,
            VideoCodec = videoCodec,
            Width = width,
            Height = height,
            FrameRate = frameRate,
            AudioStreams = audioStreams,
            StreamCount = streamCount,
        };
    }

    private static AudioStreamInfo BuildAudioStream(JsonElement stream, int ordinal)
    {
        var index = GetInt(stream, "index") ?? ordinal - 1;
        var codec = GetString(stream, "codec_name") ?? "unknown";
        var channels = GetInt(stream, "channels") ?? 0;
        var sampleRate = GetInt(stream, "sample_rate") ?? 0;
        var bitRate = GetLong(stream, "bit_rate");
        var streamDuration = GetDouble(stream, "duration");
        var language = GetTag(stream, "language");
        var title = GetTag(stream, "title");

        return new AudioStreamInfo
        {
            Index = index,
            AudioOrdinal = ordinal,
            Codec = codec,
            Channels = channels,
            SampleRate = sampleRate,
            BitRate = bitRate,
            DurationSeconds = streamDuration,
            Language = language,
            Title = title,
            Description = BuildDescription(ordinal, index, codec, channels, sampleRate, language, title),
        };
    }

    private static string BuildDescription(
        int ordinal,
        int index,
        string codec,
        int channels,
        int sampleRate,
        string? language,
        string? title)
    {
        var languagePart = string.IsNullOrWhiteSpace(language) ? "und" : language;
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"Audio #{ordinal} (stream {index}) - {languagePart} - {codec}, {channels}ch, {sampleRate} Hz");

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append(CultureInfo.InvariantCulture, $" - \"{title}\"");
        }

        return builder.ToString();
    }

    private static double GetFormatDuration(JsonElement root)
    {
        if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
        {
            return GetDouble(format, "duration") ?? 0;
        }

        return 0;
    }

    private static double GetFallbackDuration(JsonElement streams)
    {
        foreach (var stream in streams.EnumerateArray())
        {
            var duration = GetDouble(stream, "duration");
            if (duration is > 0)
            {
                return duration.Value;
            }
        }

        return 0;
    }

    private static long GetFileSize(string videoPath)
    {
        try
        {
            return File.Exists(videoPath) ? new FileInfo(videoPath).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static double? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf('/');
        if (separator < 0)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) && single > 0
                ? single
                : null;
        }

        if (!double.TryParse(value.AsSpan(0, separator), NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(value.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return null;
        }

        var rate = numerator / denominator;
        return rate > 0 ? rate : null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static int? GetInt(JsonElement element, string property)
    {
        var value = GetLong(element, property);
        return value.HasValue ? (int)value.Value : null;
    }

    private static long? GetLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? GetTag(JsonElement stream, string tagName)
    {
        if (!stream.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in tags.EnumerateObject())
        {
            if (!string.Equals(property.Name, tagName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null,
            };
        }

        return null;
    }
}
