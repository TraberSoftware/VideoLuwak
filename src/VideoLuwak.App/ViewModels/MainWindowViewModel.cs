using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Composition;
using VideoLuwak.Core.Config;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.App.ViewModels;

/// <summary>
/// Backing view model for the main window. Owns the queue, processing run,
/// persisted settings and the batch result summary. Contains no extraction
/// logic itself — everything is delegated to the Core <see cref="IVideoProcessor"/>.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv",
        ".flv", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".3gp",
        ".vob", ".ogv",
    ];

    private readonly SettingsStore _settingsStore;
    private readonly IVideoProcessor _processor;
    private readonly AppSettings _settings;

    private CancellationTokenSource? _cts;
    private VideoItemViewModel? _activeItem;
    private int _batchTotal;
    private int _batchCompleted;
    private bool _suppressSave;
    private ProcessingStage? _lastStage;

    /// <summary>Parameterless constructor for the XAML previewer.</summary>
    public MainWindowViewModel()
        : this(new SettingsStore(), null)
    {
    }

    /// <param name="settingsStore">Settings store to load/save user preferences.</param>
    /// <param name="processor">Optional processor override (defaults to the ffmpeg pipeline).</param>
    public MainWindowViewModel(SettingsStore? settingsStore, IVideoProcessor? processor)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _processor = processor ?? ExtractorFactory.CreateVideoProcessor();
        _settings = _settingsStore.Load();

        _suppressSave = true;
        OutputDirectory = _settings.OutputBaseDirectory ?? string.Empty;
        PhashThreshold = _settings.PhashSimilarityThreshold;
        KeyframeFormat = string.Equals(_settings.KeyframeFormat, "png", StringComparison.OrdinalIgnoreCase)
            ? "png"
            : "jpg";
        JpegQuality = _settings.JpegQuality;
        DeduplicateKeyframes = _settings.DeduplicateKeyframes;
        SttOptimizeAudio = _settings.SttOptimizeAudio;
        AudioSampleRate = _settings.AudioSampleRate;
        AudioChannels = _settings.AudioChannels;
        AudioHighPassHz = _settings.AudioHighPassHz;
        AudioNormalizeLoudness = _settings.AudioNormalizeLoudness;
        _suppressSave = false;

        StageSteps = [AudioStage, KeyframesStage, DedupeStage];

        Files.CollectionChanged += (_, _) => OnQueueChanged();
        Results.CollectionChanged += (_, _) =>
        {
            HasResults = Results.Count > 0;
            ClearResultsCommand.NotifyCanExecuteChanged();
        };

        OnQueueChanged();
    }

    /// <summary>Files awaiting or undergoing processing.</summary>
    public ObservableCollection<VideoItemViewModel> Files { get; } = [];

    /// <summary>Result rows for the most recent batch.</summary>
    public ObservableCollection<ResultRowViewModel> Results { get; } = [];

    /// <summary>Stages shown in the run strip.</summary>
    public IReadOnlyList<StageStepViewModel> StageSteps { get; }

    public StageStepViewModel AudioStage { get; } = new("Audio");

    public StageStepViewModel KeyframesStage { get; } = new("Keyframes");

    public StageStepViewModel DedupeStage { get; } = new("Dedupe");

    /// <summary>Comma-separated list of accepted extensions, for display.</summary>
    public string SupportedExtensionsText { get; } =
        string.Join(" · ", VideoExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));

    /// <summary>Keyframe image formats offered in settings.</summary>
    public IReadOnlyList<string> KeyframeFormats { get; } = ["jpg", "png"];

    /// <summary>Sample rates offered for extracted audio; 16 kHz is the STT default.</summary>
    public IReadOnlyList<int> SampleRates { get; } = [8000, 16000, 22050, 44100, 48000];

    /// <summary>Channel layouts offered for extracted audio.</summary>
    public IReadOnlyList<AudioChannelChoice> AudioChannelChoices { get; } =
    [
        new AudioChannelChoice(1, "Mono"),
        new AudioChannelChoice(2, "Stereo"),
    ];

    // ----- Persisted settings ------------------------------------------------

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private int _phashThreshold = 8;

    [ObservableProperty]
    private string _keyframeFormat = "jpg";

    [ObservableProperty]
    private int _jpegQuality = 2;

    [ObservableProperty]
    private bool _deduplicateKeyframes = true;

    [ObservableProperty]
    private bool _sttOptimizeAudio = true;

    [ObservableProperty]
    private int _audioSampleRate = 16000;

    [ObservableProperty]
    private int _audioChannels = 1;

    [ObservableProperty]
    private int _audioHighPassHz;

    [ObservableProperty]
    private bool _audioNormalizeLoudness;

    /// <summary>Two-way bridge for the Mono/Stereo selector backed by <see cref="AudioChannels"/>.</summary>
    public AudioChannelChoice SelectedAudioChannel
    {
        get => AudioChannelChoices.FirstOrDefault(c => c.Channels == AudioChannels) ?? AudioChannelChoices[0];
        set
        {
            if (value is not null)
            {
                AudioChannels = value.Channels;
            }
        }
    }

    public bool HasOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public string OutputDirectoryDisplay => HasOutputDirectory
        ? OutputDirectory.Trim()
        : "Next to each video";

    // ----- Transient state ---------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFilesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearResultsCommand))]
    [NotifyPropertyChangedFor(nameof(ShowProcessButton))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowRunActions))]
    [NotifyPropertyChangedFor(nameof(ShowRunAgain))]
    private bool _isProcessing;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _currentStageMessage = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProcessButton))]

    [NotifyPropertyChangedFor(nameof(ShowRunActions))]
    private bool _hasFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProcessButton))]
    [NotifyPropertyChangedFor(nameof(ShowRunActions))]
    [NotifyPropertyChangedFor(nameof(ShowRunAgain))]
    private bool _hasResults;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string _queueCountText = "No videos";

    [ObservableProperty]
    private string _queueSizeText = string.Empty;

    [ObservableProperty]
    private string _processButtonText = "Process";

    [ObservableProperty]
    private string _runStatusText = "Ready";

    [ObservableProperty]
    private string _runSubText = "Drop videos or add files to begin.";

    [ObservableProperty]
    private string _statusLine = "Ready.";

    [ObservableProperty]
    private BatchSummary _summary = BatchSummary.Empty;

    [ObservableProperty]
    private string _summaryScopeText = string.Empty;

    public bool ShowProcessButton => !IsProcessing && !HasResults && HasFiles;

    public bool ShowCancelButton => IsProcessing;

    public bool ShowRunActions => !IsProcessing && HasResults;

    public bool CanRunAgain => HasFiles;

    public bool ShowRunAgain => ShowRunActions && CanRunAgain;

    public bool IsJpegFormat => string.Equals(KeyframeFormat, "jpg", StringComparison.OrdinalIgnoreCase);

    // ----- Settings persistence ---------------------------------------------

    partial void OnOutputDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(OutputDirectoryDisplay));
        OnPropertyChanged(nameof(HasOutputDirectory));
        PersistSettings();
    }

    partial void OnPhashThresholdChanged(int value) => PersistSettings();

    partial void OnKeyframeFormatChanged(string value)
    {
        OnPropertyChanged(nameof(IsJpegFormat));
        PersistSettings();
    }

    partial void OnJpegQualityChanged(int value) => PersistSettings();

    partial void OnDeduplicateKeyframesChanged(bool value) => PersistSettings();

    partial void OnSttOptimizeAudioChanged(bool value) => PersistSettings();

    partial void OnAudioSampleRateChanged(int value) => PersistSettings();

    partial void OnAudioChannelsChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedAudioChannel));
        PersistSettings();
    }

    partial void OnAudioHighPassHzChanged(int value) => PersistSettings();

    partial void OnAudioNormalizeLoudnessChanged(bool value) => PersistSettings();

    partial void OnIsProcessingChanged(bool value)
    {
        ClearFilesCommand.NotifyCanExecuteChanged();
        ClearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultsChanged(bool value)
    {
        ClearResultsCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasFilesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunAgain));
        ProcessCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Forces a settings flush (e.g. on window close).</summary>
    public void SaveSettingsNow() => PersistSettings();

    private void PersistSettings()
    {
        if (_suppressSave)
        {
            return;
        }

        _settings.OutputBaseDirectory = NormalizeOutputDirectory();
        _settings.PhashSimilarityThreshold = PhashThreshold;
        _settings.KeyframeFormat = string.Equals(KeyframeFormat, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        _settings.JpegQuality = JpegQuality;
        _settings.DeduplicateKeyframes = DeduplicateKeyframes;
        _settings.SttOptimizeAudio = SttOptimizeAudio;
        _settings.AudioSampleRate = AudioSampleRate;
        _settings.AudioChannels = AudioChannels;
        _settings.AudioHighPassHz = AudioHighPassHz;
        _settings.AudioNormalizeLoudness = AudioNormalizeLoudness;

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLine = $"Could not save settings: {ex.Message}";
        }
    }

    private string? NormalizeOutputDirectory() =>
        string.IsNullOrWhiteSpace(OutputDirectory) ? null : OutputDirectory.Trim();

    [RelayCommand]
    private void ResetSettings()
    {
        _suppressSave = true;
        OutputDirectory = string.Empty;
        PhashThreshold = 8;
        KeyframeFormat = "jpg";
        JpegQuality = 2;
        DeduplicateKeyframes = true;
        SttOptimizeAudio = true;
        AudioSampleRate = 16000;
        AudioChannels = 1;
        AudioHighPassHz = 0;
        AudioNormalizeLoudness = false;
        _suppressSave = false;

        PersistSettings();
        StatusLine = "Settings restored to defaults.";
    }

    // ----- Queue operations --------------------------------------------------

    /// <summary>Sets the output directory (used by the folder picker).</summary>
    public void SetOutputDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            OutputDirectory = directory.Trim();
        }
    }

    /// <summary>
    /// Adds files to the queue, filtering unsupported extensions and duplicates.
    /// Returns the number of files actually added.
    /// </summary>
    public int AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (IsProcessing)
        {
            StatusLine = "Wait for the current run to finish before adding files.";
            return 0;
        }

        var added = 0;
        var skipped = 0;

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsVideoFile(full))
            {
                skipped++;
                continue;
            }

            if (Files.Any(f => string.Equals(f.FilePath, full, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            long size = 0;
            try
            {
                var info = new FileInfo(full);
                if (info.Exists)
                {
                    size = info.Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                size = 0;
            }

            var item = new VideoItemViewModel(full, size);
            item.RemoveCommand = new RelayCommand(() => RemoveFile(item));
            Files.Add(item);
            added++;
        }

        if (added > 0)
        {
            StatusLine = skipped > 0
                ? $"Added {added} file{(added == 1 ? "" : "s")}; skipped {skipped} unsupported."
                : $"Added {added} file{(added == 1 ? "" : "s")}.";
        }
        else if (skipped > 0)
        {
            StatusLine = $"No supported video files were added ({skipped} skipped).";
        }

        return added;
    }

    /// <summary>Removes a single item from the queue.</summary>
    public void RemoveFile(VideoItemViewModel item)
    {
        if (item is null || IsProcessing)
        {
            return;
        }

        Files.Remove(item);
    }

    private bool CanClearFiles() => !IsProcessing && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearFiles))]
    private void ClearFiles()
    {
        Files.Clear();
        StatusLine = "Queue cleared.";
    }

    [RelayCommand(CanExecute = nameof(CanClearResults))]
    private void ClearResults()
    {
        if (IsProcessing)
        {
            return;
        }

        Results.Clear();
        Summary = BatchSummary.Empty;
        SummaryScopeText = string.Empty;
        ResetStages();
        OverallProgress = 0;
        CurrentFileName = string.Empty;
        CurrentStageMessage = "Idle";

        foreach (var item in Files)
        {
            item.Status = VideoItemStatus.Queued;
            item.Progress = 0;
            item.StageMessage = string.Empty;
            item.ErrorMessage = null;
            item.Result = null;
        }

        RunStatusText = "Ready";
        RunSubText = HasFiles
            ? $"{QueueCountText} · {QueueSizeText} ready to process."
            : "Drop videos or add files to begin.";
        StatusLine = "Results cleared.";
    }

    private bool CanClearResults() => !IsProcessing && Results.Count > 0;

    private void OnQueueChanged()
    {
        HasFiles = Files.Count > 0;

        long totalBytes = 0;
        foreach (var file in Files)
        {
            totalBytes += file.SizeBytes;
        }

        QueueCountText = Files.Count switch
        {
            0 => "No videos",
            1 => "1 video",
            _ => $"{Files.Count} videos",
        };

        QueueSizeText = Files.Count == 0 ? string.Empty : DisplayFormat.Size(totalBytes);
        ProcessButtonText = Files.Count > 1 ? $"Process {Files.Count} videos" : "Process";

        if (!IsProcessing && !HasResults)
        {
            RunStatusText = "Ready";
            RunSubText = HasFiles
                ? $"{QueueCountText} · {QueueSizeText} ready to process."
                : "Drop videos or add files to begin.";
            CurrentStageMessage = "Idle";
        }

        ClearFilesCommand.NotifyCanExecuteChanged();
        ProcessCommand.NotifyCanExecuteChanged();
    }

    private static bool IsVideoFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        foreach (var supported in VideoExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ----- Processing --------------------------------------------------------

    private bool CanProcess() => !IsProcessing && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task ProcessAsync()
    {
        if (IsProcessing || Files.Count == 0)
        {
            return;
        }

        IsProcessing = true;
        OverallProgress = 0;
        Results.Clear();
        Summary = BatchSummary.Empty;
        SummaryScopeText = string.Empty;
        ResetStages();
        CurrentFileName = string.Empty;
        CurrentStageMessage = "Starting";

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var options = BuildProcessingOptions();
        var progress = new Progress<ProcessingProgress>(OnProgress);

        var results = new List<ProcessingResult>();
        var snapshot = Files.ToList();
        _batchTotal = snapshot.Count;
        _batchCompleted = 0;

        foreach (var item in snapshot)
        {
            item.IsQueueLocked = true;
            item.Status = VideoItemStatus.Queued;
            item.Progress = 0;
            item.StageMessage = string.Empty;
            item.ErrorMessage = null;
            item.Result = null;
        }

        var cancelled = false;
        Exception? fatalError = null;

        RunStatusText = snapshot.Count == 1 ? "Processing 1 of 1" : $"Processing 1 of {snapshot.Count}";
        RunSubText = "Preparing the first file…";

        try
        {
            for (var i = 0; i < snapshot.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var item = snapshot[i];
                ResetStages();
                _activeItem = item;
                item.Status = VideoItemStatus.Processing;
                item.Progress = 0;
                item.StageMessage = "Starting";
                CurrentFileName = item.FileName;
                CurrentStageMessage = "Starting";
                RunStatusText = $"Processing {i + 1} of {snapshot.Count}";
                RunSubText = $"{item.FileName} · Starting";
                StatusLine = $"Processing {i + 1} of {snapshot.Count}: {item.FileName}";

                var result = await _processor
                    .ProcessAsync(item.FilePath, options, progress, token)
                    .ConfigureAwait(true);

                item.Result = result;
                if (result.Success)
                {
                    item.Status = VideoItemStatus.Succeeded;
                    item.Progress = 100;
                    item.StageMessage = "Completed";
                    MarkStagesComplete();
                }
                else
                {
                    item.Status = VideoItemStatus.Failed;
                    item.ErrorMessage = result.Error;
                    item.StageMessage = "Failed";
                    MarkCurrentStageFailed();
                }

                results.Add(result);
                Results.Add(new ResultRowViewModel(item.FileName, result)
                {
                    IsExpanded = Results.Count == 0,
                });
                Summary = BatchSummary.From(results);
                SummaryScopeText = $"{Summary.Succeeded} of {snapshot.Count} completed so far";
                _batchCompleted++;
                OverallProgress = ClampPercent((double)_batchCompleted / snapshot.Count * 100d);
                RunSubText = SummaryScopeText;
            }

            OverallProgress = 100;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            if (_activeItem is { } active && active.Status == VideoItemStatus.Processing)
            {
                active.Status = VideoItemStatus.Queued;
                active.Progress = 0;
                active.StageMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            fatalError = ex;
            if (_activeItem is { } active)
            {
                active.Status = VideoItemStatus.Failed;
                active.ErrorMessage = ex.Message;
                active.StageMessage = "Failed";
                MarkCurrentStageFailed();

                if (results.All(r => !PathsEqual(r.VideoPath, active.FilePath)))
                {
                    var failedResult = new ProcessingResult
                    {
                        VideoPath = active.FilePath,
                        OutputDirectory = OutputLayout.GetOutputDirectory(active.FilePath, NormalizeOutputDirectory()),
                        OriginalSizeBytes = active.SizeBytes,
                        Success = false,
                        Error = ex.Message,
                        CompletedAt = DateTimeOffset.Now,
                    };

                    results.Add(failedResult);
                    Results.Add(new ResultRowViewModel(active.FileName, failedResult) { IsExpanded = false });
                    Summary = BatchSummary.From(results);
                }
            }

            StatusLine = $"Processing failed: {ex.Message}";
        }
        finally
        {
            _activeItem = null;
            _batchTotal = 0;
            _batchCompleted = 0;
            _cts?.Dispose();
            _cts = null;

            foreach (var item in Files)
            {
                item.IsQueueLocked = false;
            }

            IsProcessing = false;
            Summary = BatchSummary.From(results);

            if (cancelled)
            {
                ResetStages();
                CurrentStageMessage = "Cancelled";
                CurrentFileName = string.Empty;
                RunStatusText = "Cancelled";
                RunSubText = "Processing was cancelled. Finished files are still listed below.";
                SummaryScopeText = Summary.TotalFiles > 0 ? Summary.CompletionNote : string.Empty;
                StatusLine = "Processing cancelled.";
            }
            else if (fatalError is not null)
            {
                CurrentStageMessage = "Failed";
                RunStatusText = "Processing failed";
                RunSubText = fatalError.Message;
                SummaryScopeText = Summary.TotalFiles > 0 ? Summary.CompletionNote : string.Empty;
            }
            else if (results.Count > 0)
            {
                var failed = results.Count(r => !r.Success);
                CurrentStageMessage = failed == 0 ? "All done" : "Finished with errors";
                RunStatusText = failed == 0
                    ? $"Complete · {results.Count} of {snapshot.Count}"
                    : $"Finished · {results.Count - failed} of {snapshot.Count} completed";
                RunSubText = failed == 0
                    ? $"{Summary.SavedText} saved"
                    : $"{Summary.SavedText} saved · {failed} file{(failed == 1 ? "" : "s")} failed";
                SummaryScopeText = Summary.CompletionNote;
                StatusLine = failed == 0
                    ? $"Finished. {results.Count} file{(results.Count == 1 ? "" : "s")} processed successfully."
                    : $"Finished. {results.Count - failed} succeeded, {failed} failed.";
            }
            else
            {
                CurrentStageMessage = "Idle";
                RunStatusText = "Ready";
                RunSubText = HasFiles
                    ? $"{QueueCountText} · {QueueSizeText} ready to process."
                    : "Drop videos or add files to begin.";
                SummaryScopeText = string.Empty;
            }

            ClearResultsCommand.NotifyCanExecuteChanged();
            ClearFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCancel() => IsProcessing;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusLine = "Cancelling…";
    }

    private ProcessingOptions BuildProcessingOptions()
    {
        var settings = new AppSettings
        {
            OutputBaseDirectory = NormalizeOutputDirectory(),
            PhashSimilarityThreshold = PhashThreshold,
            KeyframeFormat = string.Equals(KeyframeFormat, "png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg",
            JpegQuality = JpegQuality,
            DeduplicateKeyframes = DeduplicateKeyframes,
            SttOptimizeAudio = SttOptimizeAudio,
            AudioSampleRate = AudioSampleRate,
            AudioChannels = AudioChannels,
            AudioHighPassHz = AudioHighPassHz,
            AudioNormalizeLoudness = AudioNormalizeLoudness,
        };

        return settings.ToProcessingOptions();
    }

    private void OnProgress(ProcessingProgress report)
    {
        var stage = DisplayFormat.Stage(report.Stage);

        CurrentFileName = SafeFileName(report.VideoPath);
        CurrentStageMessage = string.IsNullOrWhiteSpace(report.Message)
            ? stage
            : report.Message.StartsWith(stage, StringComparison.OrdinalIgnoreCase)
                ? report.Message
                : $"{stage} — {report.Message}";
        RunSubText = $"{CurrentFileName} · {CurrentStageMessage}";

        var current = ClampPercent(report.OverallPercent);

        // The deduplicator reports the keyframes directory rather than the source video,
        // so fall back to the active item for that stage.
        var reportIsForActiveItem = _activeItem is { } active &&
            (PathsEqual(active.FilePath, report.VideoPath) ||
             report.Stage == ProcessingStage.DeduplicatingKeyframes);

        if (reportIsForActiveItem && _activeItem is { } activeItem)
        {
            activeItem.Progress = current;
            activeItem.StageMessage = stage;
        }

        UpdateStages(report.Stage, report.StagePercent);

        var batch = _batchTotal > 0
            ? ((_batchCompleted + (current / 100d)) / _batchTotal) * 100d
            : current;

        OverallProgress = ClampPercent(batch);
    }

    private void UpdateStages(ProcessingStage stage, double stagePercent)
    {
        var percent = ClampPercent(stagePercent);
        _lastStage = stage;

        switch (stage)
        {
            case ProcessingStage.Starting:
            case ProcessingStage.Probing:
                AudioStage.Set(StageState.Active, 0);
                KeyframesStage.Set(StageState.Pending, 0);
                DedupeStage.Set(StageState.Pending, 0);
                break;

            case ProcessingStage.ExtractingAudio:
                AudioStage.Set(StageState.Active, percent);
                KeyframesStage.Set(StageState.Pending, 0);
                DedupeStage.Set(StageState.Pending, 0);
                break;

            case ProcessingStage.ExtractingKeyframes:
                AudioStage.Set(StageState.Complete, 100);
                KeyframesStage.Set(StageState.Active, percent);
                DedupeStage.Set(StageState.Pending, 0);
                break;

            case ProcessingStage.DeduplicatingKeyframes:
                AudioStage.Set(StageState.Complete, 100);
                KeyframesStage.Set(StageState.Complete, 100);
                DedupeStage.Set(StageState.Active, percent);
                break;

            case ProcessingStage.Completed:
                MarkStagesComplete();
                break;

            case ProcessingStage.Failed:
                MarkCurrentStageFailed();
                break;
        }
    }

    private void MarkStagesComplete()
    {
        AudioStage.Set(StageState.Complete, 100);
        KeyframesStage.Set(StageState.Complete, 100);
        DedupeStage.Set(StageState.Complete, 100);
    }

    private void MarkCurrentStageFailed()
    {
        var active = StageSteps.FirstOrDefault(s => s.State == StageState.Active);

        active ??= _lastStage switch
        {
            ProcessingStage.ExtractingKeyframes => KeyframesStage,
            ProcessingStage.DeduplicatingKeyframes => DedupeStage,
            _ => AudioStage,
        };

        active.Set(StageState.Failed, 100);
    }

    private void ResetStages()
    {
        _lastStage = null;
        AudioStage.Set(StageState.Pending, 0);
        KeyframesStage.Set(StageState.Pending, 0);
        DedupeStage.Set(StageState.Pending, 0);
    }

    private static double ClampPercent(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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

/// <summary>Selectable channel layout for extracted audio.</summary>
public sealed class AudioChannelChoice
{
    public AudioChannelChoice(int channels, string label)
    {
        Channels = channels;
        Label = label;
    }

    public int Channels { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
