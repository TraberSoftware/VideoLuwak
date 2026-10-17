using System.Diagnostics;

namespace VideoLuwak.App.Services;

/// <summary>Small platform bridge for opening folders in the system file browser.</summary>
public static class ShellService
{
    /// <summary>Opens <paramref name="path"/> in the platform file manager. Returns false on failure.</summary>
    public static bool OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            ProcessStartInfo startInfo;

            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true,
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo = new ProcessStartInfo("open")
                {
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add(path);
            }
            else
            {
                startInfo = new ProcessStartInfo("xdg-open")
                {
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add(path);
            }

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
