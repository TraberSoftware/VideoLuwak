using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoLuwak.App.ViewModels;

/// <summary>Visual state of one pipeline stage in the run strip.</summary>
public enum StageState
{
    Pending,
    Active,
    Complete,
    Failed,
}

/// <summary>
/// One stage of the extraction pipeline (audio, keyframes, dedupe). The run strip
/// binds directly to these so stage state never has to be inferred in XAML.
/// </summary>
public sealed partial class StageStepViewModel : ObservableObject
{
    public StageStepViewModel(string label)
    {
        Label = label;
    }

    public string Label { get; }

    [ObservableProperty]
    private StageState _state;

    [ObservableProperty]
    private double _percent;

    public bool IsPending => State == StageState.Pending;

    public bool IsActive => State == StageState.Active;

    public bool IsComplete => State == StageState.Complete;

    public bool IsFailed => State == StageState.Failed;

    public string Glyph => State switch
    {
        StageState.Pending => "○",
        StageState.Active => "●",
        StageState.Complete => "✓",
        StageState.Failed => "!",
        _ => "○",
    };

    public string StateText => State switch
    {
        StageState.Pending => "Pending",
        StageState.Active => "In progress",
        StageState.Complete => string.Empty,
        StageState.Failed => "Failed",
        _ => "Pending",
    };

    public string PercentText => $"{Percent:0}%";

    public string DetailText => State switch
    {
        StageState.Pending => string.Empty,
        StageState.Active => PercentText,
        StageState.Complete => string.Empty,
        StageState.Failed => "Failed",
        _ => string.Empty,
    };

    public void Set(StageState state, double percent)
    {
        Percent = Math.Clamp(percent, 0, 100);
        State = state;
    }

    partial void OnStateChanged(StageState value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(DetailText));
    }

    partial void OnPercentChanged(double value)
    {
        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(DetailText));
    }
}
