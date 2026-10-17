using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.App.ViewModels;

/// <summary>One extracted audio stream shown inside a result card.</summary>
public sealed class AudioStreamRowViewModel
{
    public AudioStreamRowViewModel(AudioStreamResult stream)
    {
        Name = DisplayName(stream.OutputPath);
        Description = string.IsNullOrWhiteSpace(stream.Description) ? "Audio stream" : stream.Description;
        SizeText = DisplayFormat.Size(stream.SizeBytes);
        FormatText = BuildFormatText(stream);
        IsSttOptimized = stream.SttOptimized;
    }

    public string Name { get; }

    public string Description { get; }

    public string SizeText { get; }

    /// <summary>Produced WAV format, e.g. <c>16 kHz · mono · pcm_s16le</c>.</summary>
    public string FormatText { get; }

    public bool IsSttOptimized { get; }

    private static string BuildFormatText(AudioStreamResult stream)
    {
        var parts = new List<string>(3);

        if (stream.OutputSampleRate > 0)
        {
            var khz = stream.OutputSampleRate / 1000d;
            parts.Add(khz.ToString("0.###", CultureInfo.InvariantCulture) + " kHz");
        }

        parts.Add(FormatChannels(stream.OutputChannels));

        parts.Add(string.IsNullOrWhiteSpace(stream.OutputSampleFormat)
            ? "pcm_s16le"
            : stream.OutputSampleFormat);

        return string.Join(" · ", parts);
    }

    private static string FormatChannels(int channels) => channels switch
    {
        1 => "mono",
        2 => "stereo",
        > 2 => channels.ToString(CultureInfo.InvariantCulture) + " ch",
        _ => "mono",
    };

    private static string DisplayName(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}

/// <summary>
/// Read-only projection of a single <see cref="ProcessingResult"/> for the results
/// list. Every displayed value is derived here so the view stays declarative.
/// </summary>
public sealed partial class ResultRowViewModel : ObservableObject
{
    public ResultRowViewModel(string fileName, ProcessingResult result)
    {
        FileName = fileName;
        VideoPath = result.VideoPath;
        OutputDirectory = result.OutputDirectory;
        DurationText = DisplayFormat.Duration(result.DurationSeconds);
        ElapsedText = result.ElapsedSeconds > 0
            ? result.ElapsedSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
            : "—";
        ProcessedText = result.ElapsedSeconds > 0
            ? $"processed in {ElapsedText}"
            : "processing time unavailable";

        OriginalSizeText = DisplayFormat.Size(result.OriginalSizeBytes);
        OutputSizeText = DisplayFormat.Size(result.TotalOutputSizeBytes);
        FinalKeyframesSizeText = DisplayFormat.Size(result.FinalKeyframesSizeBytes);
        SavedBytes = Math.Max(0, result.OriginalSizeBytes - result.TotalOutputSizeBytes);
        SavedText = DisplayFormat.Size(SavedBytes);

        KeyframesExtracted = result.KeyframesExtracted;
        KeyframesKept = result.KeyframesKept;
        KeyframesRemoved = result.KeyframesRemoved;
        KeyframesKeptPercent = result.KeyframesExtracted > 0
            ? Math.Clamp((double)result.KeyframesKept / result.KeyframesExtracted * 100d, 0d, 100d)
            : 0d;
        KeyframesKeptPercentText = result.KeyframesExtracted > 0
            ? DisplayFormat.Percent(KeyframesKeptPercent)
            : "—";

        SizeReductionPercent = double.IsNaN(result.SizeReductionPercent)
            ? 0
            : Math.Clamp(result.SizeReductionPercent, 0, 100);
        SizeReductionText = DisplayFormat.Percent(SizeReductionPercent);
        SizeReductionBadgeText = "−" + SizeReductionText;

        Success = result.Success;
        Error = result.Error;

        var streams = new List<AudioStreamRowViewModel>(result.AudioStreams.Count);
        long audioBytes = 0;
        foreach (var stream in result.AudioStreams)
        {
            streams.Add(new AudioStreamRowViewModel(stream));
            audioBytes += stream.SizeBytes;
        }

        AudioStreams = streams;
        AudioSizeText = DisplayFormat.Size(audioBytes);
    }

    public string FileName { get; }

    public string VideoPath { get; }

    public string OutputDirectory { get; }

    public string OutputFolderName
    {
        get
        {
            try
            {
                var name = Path.GetFileName(OutputDirectory);
                return string.IsNullOrEmpty(name) ? OutputDirectory : name;
            }
            catch (ArgumentException)
            {
                return OutputDirectory;
            }
        }
    }

    public string AudioDirectory => string.IsNullOrWhiteSpace(OutputDirectory)
        ? string.Empty
        : OutputLayout.GetAudioDirectory(OutputDirectory);

    public string FinalKeyframesDirectory => string.IsNullOrWhiteSpace(OutputDirectory)
        ? string.Empty
        : OutputLayout.GetFinalKeyframesDirectory(OutputDirectory);

    public bool Success { get; }

    public bool Failed => !Success;

    public string StatusLabel => Success ? "Done" : "Failed";

    public string StatusGlyph => Success ? "✓" : "!";

    public string? Error { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public bool HasMetrics => Success;

    [ObservableProperty]
    private bool _isExpanded;

    public IReadOnlyList<AudioStreamRowViewModel> AudioStreams { get; }

    public int AudioStreamCount => AudioStreams.Count;

    public bool HasAudioStreams => AudioStreams.Count > 0;

    public string AudioStreamsNote => AudioStreams.Count switch
    {
        0 => "No audio streams",
        1 => $"1 stream · {AudioSizeText}",
        _ => $"{AudioStreams.Count} streams · {AudioSizeText}",
    };

    public string AudioSizeText { get; }

    public string OriginalSizeText { get; }

    public string OutputSizeText { get; }

    public string FinalKeyframesSizeText { get; }

    public long SavedBytes { get; }

    public string SavedText { get; }

    public double SizeReductionPercent { get; }

    public string SizeReductionText { get; }

    public string SizeReductionBadgeText { get; }

    public int KeyframesExtracted { get; }

    public int KeyframesKept { get; }

    public int KeyframesRemoved { get; }

    public double KeyframesKeptPercent { get; }

    public string KeyframesKeptPercentText { get; }

    public bool HasKeyframes => KeyframesExtracted > 0;

    public string KeyframesSummaryText => KeyframesExtracted > 0
        ? $"{KeyframesKept} of {KeyframesExtracted} kept"
        : "No keyframes extracted";

    public string KeyframesRemovedText => $"{KeyframesRemoved} removed";

    public string DurationText { get; }

    public string ElapsedText { get; }

    public string ProcessedText { get; }

    public string MetaText => DurationText == "—"
        ? ProcessedText
        : $"{DurationText} · {ProcessedText}";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>
/// Aggregate across a completed batch. Savings are calculated from successful
/// files only; failed files remain in the counts but cannot inflate the total.
/// </summary>
public sealed class BatchSummary
{
    public static BatchSummary Empty { get; } = new();

    public int TotalFiles { get; private init; }

    public int Succeeded { get; private init; }

    public int Failed { get; private init; }

    public bool HasFailures => Failed > 0;

    public bool AllSucceeded => TotalFiles > 0 && Failed == 0;

    public bool HasSavings => Succeeded > 0 && TotalOriginalBytes > 0;

    public long TotalOriginalBytes { get; private init; }

    public long TotalOutputBytes { get; private init; }

    public long SavedBytes { get; private init; }

    public string TotalOriginalText => DisplayFormat.Size(TotalOriginalBytes);

    public string TotalOutputText => DisplayFormat.Size(TotalOutputBytes);

    public string SavedText => DisplayFormat.Size(SavedBytes);

    public double OverallReductionPercent { get; private init; }

    public string OverallReductionText => DisplayFormat.Percent(OverallReductionPercent);

    public string HeroText => HasSavings ? OverallReductionText : "—";

    public string SavingsSubText => HasSavings
        ? $"{SavedText} saved"
        : "No completed output";

    public double KeptPercent { get; private init; }

    public string KeptPercentText => HasSavings ? DisplayFormat.Percent(KeptPercent) : "—";

    public string RemovedPercentText => HasSavings ? OverallReductionText : "—";

    public string KeptSummaryText => HasSavings
        ? $"{TotalOutputText} · {KeptPercentText}"
        : "—";

    public string RemovedSummaryText => HasSavings
        ? $"{SavedText} · {RemovedPercentText}"
        : "—";

    public int TotalAudioStreams { get; private init; }

    public int TotalKeyframesExtracted { get; private init; }

    public int TotalKeyframesKept { get; private init; }

    public int TotalKeyframesRemoved { get; private init; }

    public string AudioNote => TotalAudioStreams switch
    {
        0 => "No audio streams",
        1 => "1 audio stream",
        _ => $"{TotalAudioStreams} audio streams",
    };

    public string KeyframesNote => TotalKeyframesExtracted > 0
        ? $"{TotalKeyframesKept} of {TotalKeyframesExtracted} keyframes kept"
        : "No keyframes extracted";

    public string CompletionNote
    {
        get
        {
            if (TotalFiles == 0)
            {
                return string.Empty;
            }

            if (Failed == 0)
            {
                return TotalFiles == 1 ? "1 file processed" : $"{TotalFiles} files processed";
            }

            return $"Based on {Succeeded} of {TotalFiles} completed";
        }
    }

    public string StatusSummaryText
    {
        get
        {
            if (TotalFiles == 0)
            {
                return string.Empty;
            }

            if (Failed == 0)
            {
                return TotalFiles == 1 ? "1 completed" : $"{TotalFiles} completed";
            }

            return $"{Succeeded} completed · {Failed} failed";
        }
    }

    public string Headline { get; private init; } = string.Empty;

    public static BatchSummary From(IReadOnlyList<ProcessingResult> results)
    {
        if (results.Count == 0)
        {
            return Empty;
        }

        var succeeded = 0;
        var failed = 0;
        long original = 0;
        long output = 0;
        var audioStreams = 0;
        var keyframesExtracted = 0;
        var keyframesKept = 0;
        var keyframesRemoved = 0;

        foreach (var result in results)
        {
            if (result.Success)
            {
                succeeded++;
                original += result.OriginalSizeBytes;
                output += result.TotalOutputSizeBytes;
                audioStreams += result.AudioStreams.Count;
                keyframesExtracted += result.KeyframesExtracted;
                keyframesKept += result.KeyframesKept;
                keyframesRemoved += result.KeyframesRemoved;
            }
            else
            {
                failed++;
            }
        }

        var saved = Math.Max(0, original - output);
        var reduction = original > 0
            ? Math.Clamp((double)saved / original * 100d, 0d, 100d)
            : 0d;
        var kept = original > 0
            ? Math.Clamp((double)output / original * 100d, 0d, 100d)
            : 0d;

        var headline = $"{results.Count} file{(results.Count == 1 ? "" : "s")} processed — {succeeded} completed, {failed} failed.";

        return new BatchSummary
        {
            TotalFiles = results.Count,
            Succeeded = succeeded,
            Failed = failed,
            TotalOriginalBytes = original,
            TotalOutputBytes = output,
            SavedBytes = saved,
            OverallReductionPercent = reduction,
            KeptPercent = kept,
            TotalAudioStreams = audioStreams,
            TotalKeyframesExtracted = keyframesExtracted,
            TotalKeyframesKept = keyframesKept,
            TotalKeyframesRemoved = keyframesRemoved,
            Headline = headline,
        };
    }
}
