using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoLuwak.Core.Config;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as indented camelCase JSON.
/// <para>
/// The default location is per-user:
/// Windows <c>%APPDATA%\VideoLuwak\settings.json</c>, otherwise
/// <c>$XDG_CONFIG_HOME/VideoLuwak/settings.json</c> or
/// <c>~/.config/VideoLuwak/settings.json</c>.
/// </para>
/// <para>
/// <see cref="Load"/> NEVER throws: a missing, unreadable or malformed file yields
/// default settings. <see cref="Save"/> writes atomically (temp file + move).
/// </para>
/// </summary>
public sealed class SettingsStore
{
    private const string AppFolderName = "VideoLuwak";
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public SettingsStore(string? settingsFilePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? GetDefaultFilePath()
            : Path.GetFullPath(settingsFilePath);
    }

    /// <summary>Absolute path of the settings file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>Returns the persisted settings, or defaults when anything goes wrong.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Missing / unreadable / malformed -> silently fall back to defaults.
            return new AppSettings();
        }
    }

    /// <summary>Persists the settings atomically, creating the directory when needed.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempPath = FilePath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            // If the move failed the temp file may still exist; best-effort cleanup.
            TryDelete(tempPath);
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
        catch (Exception)
        {
            // Best effort only.
        }
    }

    private static string GetDefaultFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            string? appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetEnvironmentVariable("APPDATA");
            }

            var root = string.IsNullOrWhiteSpace(appData) ? AppContext.BaseDirectory : appData;
            return Path.Combine(root, AppFolderName, SettingsFileName);
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, AppFolderName, SettingsFileName);
        }

        string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }

        var homeRoot = string.IsNullOrWhiteSpace(home) ? AppContext.BaseDirectory : home;
        return Path.Combine(homeRoot, ".config", AppFolderName, SettingsFileName);
    }
}
