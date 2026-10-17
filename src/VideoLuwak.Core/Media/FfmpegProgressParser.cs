using System.Globalization;

namespace VideoLuwak.Core.Media;

/// <summary>
/// Parses the key=value lines emitted by <c>ffmpeg -progress pipe:1</c> and turns
/// them into a completion percentage. Pure and process-free so it can be unit
/// tested in isolation.
/// </summary>
public sealed class FfmpegProgressParser
{
    public FfmpegProgressParser(double durationSeconds)
    {
        DurationSeconds = durationSeconds > 0 ? durationSeconds : 0;
    }

    /// <summary>Known media duration in seconds (0 when unknown).</summary>
    public double DurationSeconds { get; }

    /// <summary>Last observed position within the media, in seconds.</summary>
    public double ElapsedSeconds { get; private set; }

    /// <summary><c>true</c> once a <c>progress=end</c> line has been seen.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// Feeds a single output line. Returns the completion percentage (0..100), or
    /// -1 when the duration is unknown and the job has not finished.
    /// </summary>
    public double Update(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return CurrentPercent();
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return CurrentPercent();
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        switch (key)
        {
            case "out_time_us":
            case "out_time_ms":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
                {
                    ElapsedSeconds = microseconds / 1_000_000.0;
                }

                break;

            case "out_time":
                if (TryParseTime(value, out var seconds))
                {
                    ElapsedSeconds = seconds;
                }

                break;

            case "progress":
                if (string.Equals(value, "end", StringComparison.OrdinalIgnoreCase))
                {
                    IsComplete = true;
                }

                break;
        }

        return CurrentPercent();
    }

    /// <summary>Current completion percentage (0..100) or -1 when unknown.</summary>
    public double CurrentPercent()
    {
        if (IsComplete)
        {
            return 100.0;
        }

        if (DurationSeconds <= 0)
        {
            return -1.0;
        }

        var percent = ElapsedSeconds / DurationSeconds * 100.0;
        return Math.Clamp(percent, 0.0, 100.0);
    }

    private static bool TryParseTime(string value, out double seconds)
    {
        seconds = 0;

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            seconds = parsed.TotalSeconds;
            return true;
        }

        return false;
    }
}
