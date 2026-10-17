using FluentAssertions;
using VideoLuwak.Core.Config;

namespace VideoLuwak.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "nested", "settings.json");
        var store = new SettingsStore(settingsPath);
        var settings = new AppSettings
        {
            OutputBaseDirectory = "/tmp/extract",
            PhashSimilarityThreshold = 12,
            KeyframeFormat = "png",
            JpegQuality = 5,
            DeduplicateKeyframes = false,
        };

        store.Save(settings);
        var loaded = store.Load();

        loaded.Should().BeEquivalentTo(settings);
    }

    [Fact]
    public void Load_NonexistentPath_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(Path.Combine(temp.Path, "missing.json"));

        var loaded = store.Load();

        loaded.OutputBaseDirectory.Should().BeNull();
        loaded.PhashSimilarityThreshold.Should().Be(8);
        loaded.KeyframeFormat.Should().Be("jpg");
        loaded.JpegQuality.Should().Be(2);
        loaded.DeduplicateKeyframes.Should().BeTrue();
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var settingsPath = temp.CreateFile("settings.json", "{ this is not valid json");
        var store = new SettingsStore(settingsPath);

        var loaded = store.Load();

        loaded.PhashSimilarityThreshold.Should().Be(8);
        loaded.KeyframeFormat.Should().Be("jpg");
        loaded.DeduplicateKeyframes.Should().BeTrue();
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var settingsPath = temp.CreateFile("settings.json", "   ");
        var store = new SettingsStore(settingsPath);

        store.Load().Should().BeEquivalentTo(new AppSettings());
    }

    [Fact]
    public void ToProcessingOptions_ExplicitOverrideBeatsSettings()
    {
        var settings = new AppSettings
        {
            OutputBaseDirectory = "/from-settings",
            PhashSimilarityThreshold = 11,
            KeyframeFormat = "png",
            JpegQuality = 7,
            DeduplicateKeyframes = false,
        };

        var options = settings.ToProcessingOptions("/from-override");

        options.OutputBaseDirectory.Should().Be("/from-override");
        options.PhashSimilarityThreshold.Should().Be(11);
        options.KeyframeFormat.Should().Be("png");
        options.JpegQuality.Should().Be(7);
        options.DeduplicateKeyframes.Should().BeFalse();
    }

    [Fact]
    public void ToProcessingOptions_FallsBackToSettingsDirectory()
    {
        var settings = new AppSettings { OutputBaseDirectory = "/from-settings" };

        settings.ToProcessingOptions().OutputBaseDirectory.Should().Be("/from-settings");
    }

    [Fact]
    public void ToProcessingOptions_NoDirectories_YieldsNull()
    {
        var settings = new AppSettings { OutputBaseDirectory = null };

        settings.ToProcessingOptions(null).OutputBaseDirectory.Should().BeNull();
        settings.ToProcessingOptions("   ").OutputBaseDirectory.Should().BeNull();
    }
}
