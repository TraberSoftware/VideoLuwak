using FluentAssertions;
using VideoLuwak.Core.Ffmpeg;

namespace VideoLuwak.Core.Tests;

public sealed class FfmpegLocatorTests
{
    [Fact]
    public void EnumerateSearchDirectories_OnMacOs_AddsPackageManagerLocations()
    {
        var pathVariable = string.Join(Path.PathSeparator, "/opt/homebrew/bin", "/usr/bin", "/opt/homebrew/bin/");

        var directories = FfmpegLocator.EnumerateSearchDirectories(pathVariable, isMacOs: true);

        directories.Should().Equal("/opt/homebrew/bin", "/usr/bin", "/usr/local/bin", "/opt/local/bin");
    }

    [Fact]
    public void EnumerateSearchDirectories_OnMacOsWithNoPath_StillSearchesPackageManagerLocations()
    {
        var directories = FfmpegLocator.EnumerateSearchDirectories(null, isMacOs: true);

        directories.Should().Equal("/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin");
    }

    [Fact]
    public void EnumerateSearchDirectories_OnOtherPlatforms_DoesNotAddMacLocations()
    {
        var pathVariable = string.Join(Path.PathSeparator, "/usr/bin", "/bin");

        var directories = FfmpegLocator.EnumerateSearchDirectories(pathVariable, isMacOs: false);

        directories.Should().Equal("/usr/bin", "/bin");
    }

    [Fact]
    public void TryFindInDirectories_FindsExecutableInAnyDirectory()
    {
        using var temp = new TempDirectory();
        var bin = temp.Combine("bin");
        Directory.CreateDirectory(bin);

        var fileName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var expected = Path.Combine(bin, fileName);
        File.WriteAllText(expected, string.Empty);

        var found = FfmpegLocator.TryFindInDirectories("ffprobe", [temp.Path, bin], out var path);

        found.Should().BeTrue();
        path.Should().Be(expected);
    }

    [Fact]
    public void TryFindInDirectories_ReturnsFalseWhenExecutableIsAbsent()
    {
        using var temp = new TempDirectory();

        var found = FfmpegLocator.TryFindInDirectories("ffprobe", [temp.Path], out var path);

        found.Should().BeFalse();
        path.Should().BeEmpty();
    }
}
