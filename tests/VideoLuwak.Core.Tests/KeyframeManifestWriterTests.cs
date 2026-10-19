using FluentAssertions;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Tests;

public sealed class KeyframeManifestWriterTests
{
    [Fact]
    public void Build_WithDeduplication_PairsTimestampsKeptFlagsAndRanges()
    {
        using var temp = new TempDirectory();
        var output = temp.Path;
        var extracted = CreateKeyframePaths(output, 4);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 4,
            KeptFiles = 2,
            RemovedFiles = 2,
            KeptPaths =
            [
                Path.Combine(output, "final_keyframes", "kf_000001.jpg"),
                Path.Combine(output, "final_keyframes", "kf_000003.jpg"),
            ],
            RemovedPaths =
            [
                Path.Combine(output, "keyframes", "kf_000002.jpg"),
                Path.Combine(output, "keyframes", "kf_000004.jpg"),
            ],
        };

        var audio = new AudioStreamResult
        {
            OutputPath = Path.Combine(output, "audio", "audio_01_eng.wav"),
            Description = "Audio #1 (stream 1) - eng - aac, 1ch, 48000 Hz",
            Stream = new AudioStreamInfo { Language = "eng" },
        };

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [0.0, 2.0, 4.0, 6.0],
            [audio],
            output,
            deduplication);

        manifest.Video.Should().Be(Path.GetFullPath("/videos/talk.mp4"));
        manifest.DurationSeconds.Should().Be(10.0);
        manifest.TimeFormat.Should().Be("HH:MM:SS.mmm");

        var audioReference = manifest.Audio.Should().ContainSingle().Subject;
        audioReference.File.Should().Be("audio/audio_01_eng.wav");
        audioReference.Language.Should().Be("eng");
        audioReference.Description.Should().Be(audio.Description);

        manifest.Keyframes.Should().HaveCount(4);

        var first = manifest.Keyframes[0];
        first.Ordinal.Should().Be(1);
        first.SourceFile.Should().Be("keyframes/kf_000001.jpg");
        first.File.Should().Be("final_keyframes/kf_000001.jpg");
        first.TimestampSeconds.Should().Be(0.0);
        first.Timestamp.Should().Be("00:00:00.000");
        first.Kept.Should().BeTrue();
        first.TimestampEstimated.Should().BeFalse();
        first.EndTimestampSeconds.Should().Be(4.0);
        first.EndTimestamp.Should().Be("00:00:04.000");

        var second = manifest.Keyframes[1];
        second.Ordinal.Should().Be(2);
        second.SourceFile.Should().Be("keyframes/kf_000002.jpg");
        second.File.Should().Be("keyframes/kf_000002.jpg");
        second.Kept.Should().BeFalse();
        second.EndTimestampSeconds.Should().BeNull();
        second.EndTimestamp.Should().BeNull();

        var third = manifest.Keyframes[2];
        third.Ordinal.Should().Be(3);
        third.File.Should().Be("final_keyframes/kf_000003.jpg");
        third.Kept.Should().BeTrue();
        third.TimestampSeconds.Should().Be(4.0);
        third.EndTimestampSeconds.Should().Be(10.0);
        third.EndTimestamp.Should().Be("00:00:10.000");

        var fourth = manifest.Keyframes[3];
        fourth.File.Should().Be("keyframes/kf_000004.jpg");
        fourth.Kept.Should().BeFalse();
        fourth.EndTimestampSeconds.Should().BeNull();
    }

    [Fact]
    public void Build_WithoutDeduplication_KeepsEveryFrame()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 3);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            3.0,
            extracted,
            [0.0, 1.0, 2.0],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes.Should().HaveCount(3);
        manifest.Keyframes.Should().OnlyContain(entry => entry.Kept);
        manifest.Keyframes.Should().OnlyContain(
            entry => entry.File.StartsWith("keyframes/", StringComparison.Ordinal) &&
                     entry.File == entry.SourceFile);
        manifest.Keyframes[0].EndTimestampSeconds.Should().Be(1.0);
        manifest.Keyframes[1].EndTimestampSeconds.Should().Be(2.0);
        manifest.Keyframes[2].EndTimestampSeconds.Should().Be(3.0);
    }

    [Fact]
    public void Build_WithTimestampCountMismatch_EstimatesEveryTimestamp()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [1.0, 2.0, 3.0],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes.Should().HaveCount(4);
        manifest.Keyframes.Should().OnlyContain(entry => entry.TimestampEstimated);
        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().BeInAscendingOrder()
            .And.Equal(0.0, 2.5, 5.0, 7.5);
    }

    [Fact]
    public void Build_WithShortTimestampList_EstimatesEveryTimestamp()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [1.0],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes.Should().HaveCount(4);
        manifest.Keyframes.Should().OnlyContain(entry => entry.TimestampEstimated);
        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().BeInAscendingOrder()
            .And.Equal(0.0, 2.5, 5.0, 7.5);
    }

    [Fact]
    public void Build_WithoutTimestamps_EstimatesEveryTimestamp()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 3);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            9.0,
            extracted,
            [],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes.Should().OnlyContain(entry => entry.TimestampEstimated);
        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().BeInAscendingOrder()
            .And.Equal(0.0, 3.0, 6.0);
    }

    [Fact]
    public void Build_RoundsTimestampsToMilliseconds()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            5.0,
            extracted,
            [1.23456, 2.34567],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes[0].TimestampSeconds.Should().Be(1.235);
        manifest.Keyframes[0].Timestamp.Should().Be("00:00:01.235");
        manifest.Keyframes[1].TimestampSeconds.Should().Be(2.346);
    }

    [Fact]
    public void Build_WithOutOfOrderExactTimestamps_MarksClampedEntriesEstimated()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 3);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [5.0, 2.0, 6.0],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().Equal(5.0, 5.0, 6.0);
        manifest.Keyframes.Select(entry => entry.TimestampEstimated)
            .Should().Equal(false, true, false);
    }

    [Fact]
    public void Build_WithNegativeExactTimestamp_MarksClampedEntryEstimated()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);

        var manifest = KeyframeManifestWriter.Build(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [-1.0, 2.0],
            [],
            temp.Path,
            deduplication: null);

        manifest.Keyframes[0].TimestampSeconds.Should().Be(0.0);
        manifest.Keyframes[0].TimestampEstimated.Should().BeTrue();
        manifest.Keyframes[1].TimestampEstimated.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.0, "00:00:00.000")]
    [InlineData(3661.5, "01:01:01.500")]
    [InlineData(90061.25, "25:01:01.250")]
    [InlineData(86400.0, "24:00:00.000")]
    public void FormatTimestamp_UsesTotalHoursWithoutWrapping(double seconds, string expected)
    {
        KeyframeManifestWriter.FormatTimestamp(seconds).Should().Be(expected);
    }

    [Fact]
    public void Write_CreatesManifestFileAndReturnsAbsolutePath()
    {
        using var temp = new TempDirectory();
        var manifest = new KeyframeManifest
        {
            Video = "/videos/talk.mp4",
            DurationSeconds = 1.0,
        };

        var path = KeyframeManifestWriter.Write(temp.Path, manifest);

        path.Should().Be(Path.Combine(temp.Path, "keyframes.json"));
        File.Exists(path).Should().BeTrue();
        var json = File.ReadAllText(path);
        json.Should().Contain("\"video\": \"/videos/talk.mp4\"");
        json.Should().Contain("\"timeFormat\": \"HH:MM:SS.mmm\"");
    }

    private static string[] CreateKeyframePaths(string outputDirectory, int count)
    {
        var paths = new string[count];
        for (var i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(outputDirectory, "keyframes", $"kf_{i + 1:000000}.jpg");
        }

        return paths;
    }
}
