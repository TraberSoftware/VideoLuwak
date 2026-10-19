using FluentAssertions;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Tests;

public sealed class OutputLayoutTests
{
    [Fact]
    public void GetOutputDirectory_WithoutBase_UsesVideoDirectory()
    {
        using var temp = new TempDirectory();
        var video = Path.Combine(temp.Path, "sub", "My Movie.mkv");
        var expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(video))!, "My Movie");

        var actual = OutputLayout.GetOutputDirectory(video, null);

        actual.Should().Be(expected);
    }

    [Fact]
    public void GetOutputDirectory_WithBase_UsesBaseDirectory()
    {
        using var temp = new TempDirectory();
        var video = Path.Combine(temp.Path, "My Movie.mkv");
        var baseDirectory = Path.Combine(temp.Path, "base");

        var actual = OutputLayout.GetOutputDirectory(video, baseDirectory);

        actual.Should().Be(Path.Combine(Path.GetFullPath(baseDirectory), "My Movie"));
    }

    [Fact]
    public void GetOutputDirectory_StripsOnlyTheFinalExtension()
    {
        using var temp = new TempDirectory();
        var video = Path.Combine(temp.Path, "My.Video.final.mkv");

        var actual = OutputLayout.GetOutputDirectory(video, null);

        Path.GetFileName(actual).Should().Be("My.Video.final");
    }

    [Theory]
    [InlineData("Energy-Oil-and-SnapYT.App.mp4", "Energy-Oil-and-SnapYT.App (output)")]
    [InlineData("Talk.APP.mp4", "Talk.APP (output)")]
    [InlineData("Slides.xcodeproj.mkv", "Slides.xcodeproj (output)")]
    [InlineData("My.Video.final.mkv", "My.Video.final")]
    [InlineData("App.video.mp4", "App.video")]
    public void GetOutputDirectory_EscapesMacOsPackageExtensions(string fileName, string expectedName)
    {
        using var temp = new TempDirectory();

        var actual = OutputLayout.GetOutputDirectory(Path.Combine(temp.Path, fileName), null);

        Path.GetFileName(actual).Should().Be(expectedName);
    }

    [Fact]
    public void SanitizeName_ReplacesInvalidCharacters()
    {
        var invalid = Path.GetInvalidFileNameChars();
        var input = "x" + invalid[0] + "y";

        var sanitized = OutputLayout.SanitizeName(input);

        sanitized.Should().Be("x_y");
        foreach (var c in invalid)
        {
            sanitized.Should().NotContain(c.ToString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void SanitizeName_EmptyOrWhitespace_ReturnsFallback(string input)
    {
        OutputLayout.SanitizeName(input).Should().Be("video");
    }

    [Theory]
    [InlineData("name.", "name")]
    [InlineData("  name  ", "name")]
    [InlineData("plain", "plain")]
    public void SanitizeName_TrimsWhitespaceAndTrailingDots(string input, string expected)
    {
        OutputLayout.SanitizeName(input).Should().Be(expected);
    }

    [Fact]
    public void FolderAndFileNameConstants_HaveExpectedValues()
    {
        OutputLayout.AudioFolderName.Should().Be("audio");
        OutputLayout.KeyframesFolderName.Should().Be("keyframes");
        OutputLayout.FinalKeyframesFolderName.Should().Be("final_keyframes");
        OutputLayout.AudioDescriptionFileName.Should().Be("audio_streams.json");
        OutputLayout.KeyframeManifestFileName.Should().Be("keyframes.json");
        OutputLayout.ResultFileName.Should().Be("processing_result.json");
    }

    [Fact]
    public void DirectoryHelpers_ComposeFromConstants()
    {
        const string outputDirectory = "/tmp/videoluwak-output";

        OutputLayout.GetAudioDirectory(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.AudioFolderName));
        OutputLayout.GetKeyframesDirectory(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.KeyframesFolderName));
        OutputLayout.GetFinalKeyframesDirectory(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.FinalKeyframesFolderName));
        OutputLayout.GetAudioDescriptionPath(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.AudioFolderName, OutputLayout.AudioDescriptionFileName));
        OutputLayout.GetKeyframeManifestPath(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.KeyframeManifestFileName));
        OutputLayout.GetResultPath(outputDirectory)
            .Should().Be(Path.Combine(outputDirectory, OutputLayout.ResultFileName));
    }
}
