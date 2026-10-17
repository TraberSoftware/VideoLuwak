using System.Reflection;

namespace VideoLuwak.Core.Ffmpeg;

/// <summary>
/// Extracts the ffmpeg / ffprobe binaries embedded into the application assembly.
/// <para>
/// The binaries are embedded by the App project (only for Windows release builds), while
/// this provider lives in the Core assembly, so the resource lookup must target the
/// entry (application) assembly rather than <c>typeof(EmbeddedFfmpegProvider).Assembly</c>.
/// On non-Windows builds there is no resource and the caller falls back to the system
/// executables on <c>PATH</c>.
/// </para>
/// </summary>
internal static class EmbeddedFfmpegProvider
{
    private static readonly object ExtractionGate = new();

    /// <summary>
    /// Attempts to materialise <paramref name="resourceName"/> into the per-user
    /// runtime cache and, on success, returns its path.
    /// </summary>
    public static bool TryExtract(string resourceName, string fileName, out string path)
    {
        path = string.Empty;

        try
        {
            var assembly = GetApplicationAssembly();
            using var resource = assembly.GetManifestResourceStream(resourceName);
            if (resource is null)
            {
                return false;
            }

            var cacheDirectory = GetCacheDirectory();
            var targetPath = Path.Combine(cacheDirectory, fileName);

            lock (ExtractionGate)
            {
                Directory.CreateDirectory(cacheDirectory);

                if (!IsCurrent(targetPath, resource))
                {
                    WriteAtomically(resource, targetPath);
                    MakeExecutable(targetPath);
                }

                if (!File.Exists(targetPath))
                {
                    return false;
                }

                path = targetPath;
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            path = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// The assembly that contains the embedded resources: the executable that started the
    /// process (VideoLuwak.App). Falls back to the Core assembly when no entry assembly is
    /// available (for example in some hosts/tests), where the lookup simply finds nothing.
    /// </summary>
    private static Assembly GetApplicationAssembly() =>
        Assembly.GetEntryAssembly() ?? typeof(EmbeddedFfmpegProvider).Assembly;

    private static bool IsCurrent(string targetPath, Stream resource)
    {
        try
        {
            if (!resource.CanSeek || !File.Exists(targetPath))
            {
                return false;
            }

            return new FileInfo(targetPath).Length == resource.Length;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void WriteAtomically(Stream resource, string targetPath)
    {
        var temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            resource.Position = 0;
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                resource.CopyTo(output);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Leftover temp files are harmless and will be overwritten.
                }
                catch (UnauthorizedAccessException)
                {
                    // Nothing else we can do here.
                }
            }
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            const UnixFileMode mode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort: the binary may still be runnable if permissions permit.
        }
    }

    private static string GetCacheDirectory()
    {
        var version = GetApplicationAssembly().GetName().Version?.ToString() ?? "0.0.0";

        string root;
        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData",
                    "Local");
            }
        }
        else
        {
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            root = !string.IsNullOrWhiteSpace(xdgCache)
                ? xdgCache
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        }

        return Path.Combine(root, "VideoLuwak", "runtime", version);
    }
}
