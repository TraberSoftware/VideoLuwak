using Avalonia.Controls;
using Avalonia.Platform;

namespace VideoLuwak.App.Services;

/// <summary>Loads the embedded brand assets used by windows and the shell.</summary>
public static class BrandAssets
{
    private const string WindowsIconUri = "avares://VideoLuwak/Assets/logo/logo.ico";
    private const string FallbackIconUri = "avares://VideoLuwak/Assets/logo/logo-mark-square-256.png";

    /// <summary>
    /// Returns the application window/taskbar icon, or null when the asset is unavailable.
    /// Windows gets the exact-size multi-resolution ICO so the shell does not have to
    /// downscale a single large bitmap for the taskbar.
    /// </summary>
    public static WindowIcon? LoadWindowIcon()
    {
        try
        {
            var uri = OperatingSystem.IsWindows() ? WindowsIconUri : FallbackIconUri;
            return new WindowIcon(AssetLoader.Open(new Uri(uri)));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
