using System.Text;

namespace VideoLuwak.Core.Pipeline;

/// <summary>
/// Single source of truth for the on-disk output layout produced for a video:
/// <code>
/// &lt;baseDir or video folder&gt;/&lt;video-name&gt;/
///     audio/                       (one .wav per audio stream + audio_streams.json)
///     keyframes/                   (all extracted I-frames)
///     final_keyframes/             (keyframes kept after pHash deduplication)
///     processing_result.json       (summary)
/// </code>
/// </summary>
public static class OutputLayout
{
    public const string AudioFolderName = "audio";
    public const string KeyframesFolderName = "keyframes";
    public const string FinalKeyframesFolderName = "final_keyframes";
    public const string AudioDescriptionFileName = "audio_streams.json";
    public const string ResultFileName = "processing_result.json";
    private const string EscapedNameSuffix = " (output)";

    private static readonly string[] MacOsPackageExtensions =
    [
        ".app",
        ".bundle",
        ".framework",
        ".plugin",
        ".kext",
        ".prefpane",
        ".qlgenerator",
        ".mdimporter",
        ".saver",
        ".xcodeproj",
        ".xcworkspace",
        ".playground",
        ".docset",
        ".rtfd",
        ".sparsebundle",
        ".fcpbundle",
        ".logicx",
        ".band",
        ".imovielibrary",
        ".photoslibrary",
        ".musiclibrary",
        ".tvlibrary",
        ".aplibrary",
    ];

    /// <summary>Root directory where this video's output folder lives.</summary>
    public static string GetOutputDirectory(string videoPath, string? outputBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);

        var videoFullPath = Path.GetFullPath(videoPath);
        var baseRoot = string.IsNullOrWhiteSpace(outputBaseDirectory)
            ? Path.GetDirectoryName(videoFullPath)!
            : Path.GetFullPath(outputBaseDirectory);

        var name = EscapeMacOsPackageName(SanitizeName(Path.GetFileNameWithoutExtension(videoFullPath)));
        return Path.Combine(baseRoot, name);
    }

    public static string GetAudioDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, AudioFolderName);

    public static string GetKeyframesDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, KeyframesFolderName);

    public static string GetFinalKeyframesDirectory(string outputDirectory) =>
        Path.Combine(outputDirectory, FinalKeyframesFolderName);

    public static string GetAudioDescriptionPath(string outputDirectory) =>
        Path.Combine(outputDirectory, AudioFolderName, AudioDescriptionFileName);

    public static string GetResultPath(string outputDirectory) =>
        Path.Combine(outputDirectory, ResultFileName);

    /// <summary>Removes characters that are invalid in file/folder names.</summary>
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "video";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        var result = sb.ToString().Trim().TrimEnd('.');
        return result.Length == 0 ? "video" : result;
    }

    /// <summary>
    /// Keeps the generated output directory from being treated as a macOS bundle.
    /// Finder displays any folder whose name ends with an application/package
    /// extension (notably <c>.app</c>) as an opaque app instead of a folder.
    /// </summary>
    private static string EscapeMacOsPackageName(string name)
    {
        foreach (var extension in MacOsPackageExtensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name + EscapedNameSuffix;
            }
        }

        return name;
    }
}
