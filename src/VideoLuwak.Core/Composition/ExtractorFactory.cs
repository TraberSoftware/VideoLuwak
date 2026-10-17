using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Ffmpeg;
using VideoLuwak.Core.Hashing;
using VideoLuwak.Core.Media;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Composition;

/// <summary>
/// Composition root for the extraction pipeline. Builds the concrete ffmpeg-backed
/// services and wires them into a <see cref="VideoProcessor"/>.
/// </summary>
public static class ExtractorFactory
{
    /// <summary>Creates an FFmpeg / FFprobe locator with optional explicit overrides.</summary>
    public static IFfmpegLocator CreateLocator(string? ffmpegPath = null, string? ffprobePath = null) =>
        new FfmpegLocator(ffmpegPath, ffprobePath);

    /// <summary>
    /// Creates a fully wired <see cref="IVideoProcessor"/> using system FFmpeg
    /// (or the supplied explicit executable paths).
    /// </summary>
    public static IVideoProcessor CreateVideoProcessor(string? ffmpegPath = null, string? ffprobePath = null) =>
        CreateVideoProcessor(CreateLocator(ffmpegPath, ffprobePath));

    /// <summary>
    /// Creates a fully wired <see cref="IVideoProcessor"/> using an existing locator. Callers that
    /// also perform FFmpeg setup can share the same locator so explicit overrides stay consistent.
    /// </summary>
    public static IVideoProcessor CreateVideoProcessor(IFfmpegLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);

        var runner = new ProcessRunner();

        var ffprobe = new FfprobeService(locator, runner);
        var audioExtractor = new AudioExtractor(locator, runner);
        var keyframeExtractor = new KeyframeExtractor(locator, runner);
        var hasher = new FfmpegPerceptualHasher(locator, runner);
        var deduplicator = new KeyframeDeduplicator();

        return new VideoProcessor(ffprobe, audioExtractor, keyframeExtractor, deduplicator, hasher);
    }
}
