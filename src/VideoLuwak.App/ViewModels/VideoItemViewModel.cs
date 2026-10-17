using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoLuwak.Core.Models;

namespace VideoLuwak.App.ViewModels;

/// <summary>Lifecycle of a queued file as shown in the queue list.</summary>
public enum VideoItemStatus
{
    Queued,
    Processing,
    Succeeded,
    Failed,
}

/// <summary>One queued video file, with its live processing state.</summary>
public sealed partial class VideoItemViewModel : ObservableObject
{
    public VideoItemViewModel(string filePath, long sizeBytes)
    {
        FilePath = filePath;
        FileName = SafeFileName(filePath);
        SizeBytes = sizeBytes;
        ExtensionLabel = BuildExtensionLabel(filePath);
    }

    /// <summary>Absolute path of the queued video.</summary>
    public string FilePath { get; }

    /// <summary>File name (without directory) shown in the queue.</summary>
    public string FileName { get; }

    /// <summary>Size on disk when the file was added, in bytes.</summary>
    public long SizeBytes { get; }

    /// <summary>Short uppercase extension shown in the file tile, e.g. "MP4".</summary>
    public string ExtensionLabel { get; }

    /// <summary>Human-readable size, e.g. "1.42 GB".</summary>
    public string SizeText => DisplayFormat.Size(SizeBytes);

    /// <summary>Size plus duration once the file has been processed.</summary>
    public string MetaText
    {
        get
        {
            if (Result is { DurationSeconds: > 0 } result)
            {
                return $"{SizeText} · {DisplayFormat.Duration(result.DurationSeconds)}";
            }

            return SizeText;
        }
    }

    /// <summary>Invoked when the queue "remove" affordance is clicked.</summary>
    public IRelayCommand? RemoveCommand { get; set; }

    [ObservableProperty]
    private VideoItemStatus _status = VideoItemStatus.Queued;

    [ObservableProperty]
    private string _statusText = "Queued";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _stageMessage = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ProcessingResult? _result;

    [ObservableProperty]
    private bool _isQueueLocked;

    public bool IsQueued => Status == VideoItemStatus.Queued;

    public bool IsProcessing => Status == VideoItemStatus.Processing;

    public bool IsSucceeded => Status == VideoItemStatus.Succeeded;

    public bool IsFailed => Status == VideoItemStatus.Failed;

    public bool IsFinished => Status is VideoItemStatus.Succeeded or VideoItemStatus.Failed;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanRemove => !IsQueueLocked;

    public string StatusGlyph => Status switch
    {
        VideoItemStatus.Queued => "•",
        VideoItemStatus.Processing => "◔",
        VideoItemStatus.Succeeded => "✓",
        VideoItemStatus.Failed => "!",
        _ => "•",
    };

    /// <summary>Compact pill label; processing shows the live percentage instead of a word.</summary>
    public string StatusPillText => IsProcessing ? $"{Progress:0}%" : StatusText;

    partial void OnStatusChanged(VideoItemStatus value)
    {
        StatusText = value switch
        {
            VideoItemStatus.Queued => "Queued",
            VideoItemStatus.Processing => "Processing",
            VideoItemStatus.Succeeded => "Done",
            VideoItemStatus.Failed => "Failed",
            _ => value.ToString(),
        };

        OnPropertyChanged(nameof(IsQueued));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSucceeded));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusPillText));
        OnPropertyChanged(nameof(CanRemove));
    }

    partial void OnProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StatusPillText));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnResultChanged(ProcessingResult? value)
    {
        OnPropertyChanged(nameof(MetaText));
    }

    partial void OnIsQueueLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRemove));
    }

    private static string BuildExtensionLabel(string path)
    {
        try
        {
            var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            return string.IsNullOrEmpty(extension) ? "VIDEO" : extension;
        }
        catch (ArgumentException)
        {
            return "VIDEO";
        }
    }

    private static string SafeFileName(string path)
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
