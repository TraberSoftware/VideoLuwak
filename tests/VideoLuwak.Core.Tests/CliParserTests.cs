using FluentAssertions;
using VideoLuwak.Core.Cli;

namespace VideoLuwak.Core.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void Parse_NoArguments_ReturnsNoInputsAndNoError()
    {
        var result = CliParser.Parse([]);

        result.Error.Should().BeNull();
        result.Options.InputPaths.Should().BeEmpty();
        result.Options.ShowHelp.Should().BeFalse();
        result.Options.ShowVersion.Should().BeFalse();
    }

    [Fact]
    public void Parse_SingleInput_ReturnsOneInput()
    {
        var result = CliParser.Parse(["movie.mkv"]);

        result.Error.Should().BeNull();
        result.Options.InputPaths.Should().Equal("movie.mkv");
    }

    [Fact]
    public void Parse_AllOptions_PopulatesEveryField()
    {
        var result = CliParser.Parse(
            ["a.mp4", "b.mkv", "-o", "/tmp/out", "-t", "6", "--format", "png", "--quality", "4", "--no-dedup"]);

        result.Error.Should().BeNull();
        var options = result.Options;
        options.InputPaths.Should().Equal("a.mp4", "b.mkv");
        options.OutputBaseDirectory.Should().Be("/tmp/out");
        options.PhashSimilarityThreshold.Should().Be(6);
        options.KeyframeFormat.Should().Be("png");
        options.JpegQuality.Should().Be(4);
        options.DeduplicateKeyframes.Should().BeFalse();
    }

    [Fact]
    public void Parse_DedupFlag_EnablesDeduplication()
    {
        var result = CliParser.Parse(["--dedup", "movie.mkv"]);

        result.Error.Should().BeNull();
        result.Options.DeduplicateKeyframes.Should().BeTrue();
    }

    [Fact]
    public void Parse_InlineOptionValues_AreSupported()
    {
        var result = CliParser.Parse(["--output=/tmp/out", "--threshold=9", "movie.mkv"]);

        result.Error.Should().BeNull();
        result.Options.OutputBaseDirectory.Should().Be("/tmp/out");
        result.Options.PhashSimilarityThreshold.Should().Be(9);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_Help_SetsShowHelp(string flag)
    {
        var result = CliParser.Parse([flag]);

        result.Error.Should().BeNull();
        result.Options.ShowHelp.Should().BeTrue();
    }

    [Fact]
    public void Parse_Version_SetsShowVersion()
    {
        var result = CliParser.Parse(["--version"]);

        result.Error.Should().BeNull();
        result.Options.ShowVersion.Should().BeTrue();
    }

    [Fact]
    public void Parse_UnknownOption_ReturnsErrorWithoutThrowing()
    {
        var act = () => CliParser.Parse(["--bogus"]);

        var result = act.Should().NotThrow().Subject;
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_OptionMissingValue_ReturnsError()
    {
        var result = CliParser.Parse(["-o"]);

        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("--format", "bmp")]
    [InlineData("--quality", "0")]
    [InlineData("--quality", "99")]
    [InlineData("--threshold", "-1")]
    public void Parse_InvalidOptionValue_ReturnsError(string option, string value)
    {
        var result = CliParser.Parse([option, value, "movie.mkv"]);

        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_SttAudioOptions_PopulatesEveryField()
    {
        var result = CliParser.Parse(
            ["movie.mkv", "--audio-rate", "22050", "--audio-channels", "2", "--highpass", "80", "--loudnorm"]);

        result.Error.Should().BeNull();
        var options = result.Options;
        options.AudioSampleRate.Should().Be(22050);
        options.AudioChannels.Should().Be(2);
        options.AudioHighPassHz.Should().Be(80);
        options.AudioNormalizeLoudness.Should().BeTrue();
    }

    [Theory]
    [InlineData("--no-stt-audio", false)]
    [InlineData("--stt-audio", true)]
    public void Parse_SttOptimizeFlag_SetsSttOptimizeAudio(string flag, bool expected)
    {
        var result = CliParser.Parse(["movie.mkv", flag]);

        result.Error.Should().BeNull();
        result.Options.SttOptimizeAudio.Should().Be(expected);
    }

    [Theory]
    [InlineData("--mono", 1)]
    [InlineData("--stereo", 2)]
    public void Parse_ChannelShorthand_SetsAudioChannels(string flag, int expected)
    {
        var result = CliParser.Parse(["movie.mkv", flag]);

        result.Error.Should().BeNull();
        result.Options.AudioChannels.Should().Be(expected);
    }

    [Theory]
    [InlineData("--audio-rate", "0")]
    [InlineData("--audio-rate", "abc")]
    [InlineData("--audio-channels", "0")]
    [InlineData("--highpass", "-5")]
    public void Parse_InvalidSttAudioValue_ReturnsError(string option, string value)
    {
        var result = CliParser.Parse([option, value, "movie.mkv"]);

        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_DoubleDash_MakesFollowingArgumentsPositional()
    {
        var result = CliParser.Parse(["--", "--not-an-option", "-x"]);

        result.Error.Should().BeNull();
        result.Options.InputPaths.Should().Equal("--not-an-option", "-x");
    }

    [Fact]
    public void GetUsage_ReturnsNonEmptyText()
    {
        CliParser.GetUsage().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--gui" }, false)]
    [InlineData(new[] { "x.mp4" }, true)]
    [InlineData(new[] { "x.mp4", "--gui" }, false)]
    public void IsCliRequest_ClassifiesArguments(string[] args, bool expected)
    {
        CliRunner.IsCliRequest(args).Should().Be(expected);
    }
}
