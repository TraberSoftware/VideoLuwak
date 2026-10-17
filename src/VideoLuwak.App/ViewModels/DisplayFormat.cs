using System.Globalization;
using VideoLuwak.Core.Models;

namespace VideoLuwak.App.ViewModels;

/// <summary>
/// Presentation-only formatting helpers shared by the view models. Contains no
/// business logic; it only turns raw Core values into human-readable strings.
/// </summary>
internal static class DisplayFormat
{
    public static string Size(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;
        const double tb = gb * 1024d;
        var c = CultureInfo.InvariantCulture;

        return bytes switch
        {
            _ when bytes >= tb => (bytes / tb).ToString("0.00", c) + " TB",
            _ when bytes >= gb => (bytes / gb).ToString("0.00", c) + " GB",
            _ when bytes >= mb => (bytes / mb).ToString("0.00", c) + " MB",
            _ when bytes >= kb => (bytes / kb).ToString("0.00", c) + " KB",
            _ => bytes.ToString(c) + " B",
        };
    }

    public static string Percent(double value) =>
        double.IsNaN(value) ? "—" : value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public static string Duration(double seconds)
    {
        if (double.IsNaN(seconds) || seconds <= 0)
        {
            return "—";
        }

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    public static string Stage(ProcessingStage stage) => stage switch
    {
        ProcessingStage.Starting => "Starting",
        ProcessingStage.Probing => "Probing media",
        ProcessingStage.ExtractingAudio => "Extracting audio",
        ProcessingStage.ExtractingKeyframes => "Extracting keyframes",
        ProcessingStage.DeduplicatingKeyframes => "Deduplicating keyframes",
        ProcessingStage.Completed => "Completed",
        ProcessingStage.Failed => "Failed",
        _ => stage.ToString(),
    };
}
