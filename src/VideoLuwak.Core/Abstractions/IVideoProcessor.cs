using VideoLuwak.Core.Models;

namespace VideoLuwak.Core.Abstractions;

/// <summary>
/// Orchestrates the full extraction pipeline for a single video file:
/// probe -> audio streams -> keyframes -> deduplicate -> summary.
/// </summary>
public interface IVideoProcessor
{
    Task<ProcessingResult> ProcessAsync(
        string videoPath,
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
