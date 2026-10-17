using VideoLuwak.Core.Abstractions;

namespace VideoLuwak.Core.Hashing;

/// <summary>
/// Computes 64-bit perceptual hashes by asking ffmpeg to decode an image down to a raw
/// 32x32 grayscale frame, then applying the pure <see cref="PerceptualHash64"/> DCT.
/// The raw frame is written to a uniquely named temporary file (rather than stdout) to
/// avoid binary-stream encoding issues, and is always deleted.
/// </summary>
public sealed class FfmpegPerceptualHasher : IPerceptualHasher
{
    private const string TempFolderName = "VideoLuwak";

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public FfmpegPerceptualHasher(IFfmpegLocator locator, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);

        _locator = locator;
        _runner = runner;
    }

    /// <inheritdoc />
    public ulong ComputeHash(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException($"Image to hash was not found: '{imagePath}'.", imagePath);
        }

        // Propagates FfmpegNotFoundException when ffmpeg cannot be located.
        string ffmpeg = _locator.ResolveFfmpeg();

        string tempDirectory = Path.Combine(Path.GetTempPath(), TempFolderName);
        Directory.CreateDirectory(tempDirectory);

        string tempGrayFile = Path.Combine(tempDirectory, $"frame_{Guid.NewGuid():N}.gray");

        try
        {
            var request = new ProcessRequest
            {
                FileName = ffmpeg,
                Arguments =
                [
                    "-v", "error",
                    "-nostdin",
                    "-y",
                    "-i", imagePath,
                    "-vf", "scale=32:32",
                    "-pix_fmt", "gray",
                    "-f", "rawvideo",
                    tempGrayFile,
                ],
            };

            ProcessResult result = _runner.RunAsync(request).GetAwaiter().GetResult();

            if (!result.Succeeded)
            {
                throw new FfmpegExecutionException(
                    $"ffmpeg failed to decode '{imagePath}' into a 32x32 grayscale frame (exit code {result.ExitCode}).",
                    result.ExitCode,
                    result.StandardError);
            }

            byte[] raw = File.ReadAllBytes(tempGrayFile);
            if (raw.Length != PerceptualHash64.SampleCount)
            {
                throw new InvalidOperationException(
                    $"Expected ffmpeg to emit exactly {PerceptualHash64.SampleCount} raw grayscale bytes for '{imagePath}', " +
                    $"but it produced {raw.Length}.");
            }

            return PerceptualHash64.FromGrayscale(raw);
        }
        finally
        {
            TryDelete(tempGrayFile);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort: a leftover temp file must not fail hashing.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a leftover temp file must not fail hashing.
        }
    }
}
