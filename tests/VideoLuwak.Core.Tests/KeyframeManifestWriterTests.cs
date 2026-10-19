using FluentAssertions;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Tests;

public sealed class KeyframeManifestWriterTests
{
    [Fact]
    public void BuildAll_PairsEveryFrameAndAssignsSequentialEnds()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);
        var audio = new AudioStreamResult
        {
            OutputPath = Path.Combine(temp.Path, "audio", "audio_01_eng.wav"),
            Description = "Audio #1 (stream 1) - eng - aac, 1ch, 48000 Hz",
            Stream = new AudioStreamInfo { Language = "eng" },
        };

        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [0.0, 2.0, 4.0, 6.0],
            [audio],
            temp.Path);

        manifest.Video.Should().Be(Path.GetFullPath("/videos/talk.mp4"));
        manifest.DurationSeconds.Should().Be(10.0);
        manifest.TimeFormat.Should().Be("HH:MM:SS.mmm");
        manifest.KeyframeCount.Should().Be(4);
        manifest.Keyframes.Should().HaveCount(4);

        manifest.Keyframes.Select(entry => entry.Ordinal).Should().Equal(1, 2, 3, 4);
        manifest.Keyframes.Select(entry => entry.File).Should().Equal(
            "keyframes/kf_000001.jpg",
            "keyframes/kf_000002.jpg",
            "keyframes/kf_000003.jpg",
            "keyframes/kf_000004.jpg");
        manifest.Keyframes.Select(entry => entry.TimestampSeconds).Should().Equal(0.0, 2.0, 4.0, 6.0);
        manifest.Keyframes.Select(entry => entry.EndTimestampSeconds).Should().Equal(2.0, 4.0, 6.0, 10.0);
        manifest.Keyframes[0].Timestamp.Should().Be("00:00:00.000");
        manifest.Keyframes[3].EndTimestamp.Should().Be("00:00:10.000");
        manifest.Keyframes.Should().OnlyContain(entry => !entry.TimestampEstimated);

        var audioReference = manifest.Audio.Should().ContainSingle().Subject;
        audioReference.File.Should().Be("audio/audio_01_eng.wav");
        audioReference.Language.Should().Be("eng");
        audioReference.Description.Should().Be(audio.Description);
    }

    [Fact]
    public void BuildFinal_KeepsOnlyDeduplicatedFramesAndSkipsRemovedRanges()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 4,
            KeptFiles = 2,
            RemovedFiles = 2,
            KeptPaths =
            [
                Path.Combine(temp.Path, "final_keyframes", "kf_000001.jpg"),
                Path.Combine(temp.Path, "final_keyframes", "kf_000003.jpg"),
            ],
            RemovedPaths =
            [
                Path.Combine(temp.Path, "keyframes", "kf_000002.jpg"),
                Path.Combine(temp.Path, "keyframes", "kf_000004.jpg"),
            ],
        };

        var manifest = KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [0.0, 2.0, 4.0, 6.0],
            [],
            temp.Path,
            deduplication);

        manifest.KeyframeCount.Should().Be(2);
        manifest.Keyframes.Should().HaveCount(2);
        manifest.Keyframes.Select(entry => entry.Ordinal).Should().Equal(1, 3);
        manifest.Keyframes.Select(entry => entry.File).Should().Equal(
            "final_keyframes/kf_000001.jpg",
            "final_keyframes/kf_000003.jpg");
        manifest.Keyframes.Select(entry => entry.TimestampSeconds).Should().Equal(0.0, 4.0);
        manifest.Keyframes.Select(entry => entry.EndTimestampSeconds).Should().Equal(4.0, 10.0);
        manifest.Keyframes[0].EndTimestamp.Should().Be("00:00:04.000");
        manifest.Keyframes[1].EndTimestamp.Should().Be("00:00:10.000");
    }

    [Fact]
    public void BuildAll_WithTimestampCountMismatch_EstimatesEveryTimestamp()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);

        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [1.0, 2.0, 3.0],
            [],
            temp.Path);

        manifest.Keyframes.Should().HaveCount(4);
        manifest.Keyframes.Should().OnlyContain(entry => entry.TimestampEstimated);
        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().BeInAscendingOrder()
            .And.Equal(0.0, 2.5, 5.0, 7.5);
        manifest.Keyframes.Select(entry => entry.EndTimestampSeconds).Should().Equal(2.5, 5.0, 7.5, 10.0);
    }

    [Fact]
    public void BuildFinal_WithTimestampCountMismatch_EstimatesEveryTimestamp()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 4);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 4,
            KeptFiles = 2,
            RemovedFiles = 2,
            KeptPaths =
            [
                Path.Combine(temp.Path, "final_keyframes", "kf_000001.jpg"),
                Path.Combine(temp.Path, "final_keyframes", "kf_000003.jpg"),
            ],
        };

        var manifest = KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [1.0, 2.0, 3.0],
            [],
            temp.Path,
            deduplication);

        manifest.Keyframes.Should().HaveCount(2);
        manifest.Keyframes.Should().OnlyContain(entry => entry.TimestampEstimated);
        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().BeInAscendingOrder()
            .And.Equal(0.0, 5.0);
        manifest.Keyframes.Select(entry => entry.EndTimestampSeconds).Should().Equal(5.0, 10.0);
    }

    [Fact]
    public void BuildFinal_WhenKeptPathDoesNotMatchExtractedKeyframe_Throws()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 2,
            KeptFiles = 1,
            RemovedFiles = 1,
            KeptPaths = [Path.Combine(temp.Path, "final_keyframes", "kf_999999.jpg")],
        };

        Action act = () => KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            5.0,
            extracted,
            [0.0, 1.0],
            [],
            temp.Path,
            deduplication);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*did not match an extracted keyframe*");
    }

    [Fact]
    public void BuildFinal_WhenKeptCountDisagreesWithMatches_Throws()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 2,
            KeptFiles = 2,
            RemovedFiles = 0,
            KeptPaths = [Path.Combine(temp.Path, "final_keyframes", "kf_000001.jpg")],
        };

        Action act = () => KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            5.0,
            extracted,
            [0.0, 1.0],
            [],
            temp.Path,
            deduplication);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*disagrees with the deduplication result*");
    }

    [Fact]
    public void BuildFinal_WithSingleKeptFrame_EndsAtDuration()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 3);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 3,
            KeptFiles = 1,
            RemovedFiles = 2,
            KeptPaths = [Path.Combine(temp.Path, "final_keyframes", "kf_000002.jpg")],
        };

        var manifest = KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            9.0,
            extracted,
            [0.0, 3.0, 6.0],
            [],
            temp.Path,
            deduplication);

        var entry = manifest.Keyframes.Should().ContainSingle().Subject;
        entry.Ordinal.Should().Be(2);
        entry.File.Should().Be("final_keyframes/kf_000002.jpg");
        entry.TimestampSeconds.Should().Be(3.0);
        entry.EndTimestampSeconds.Should().Be(9.0);
        entry.EndTimestamp.Should().Be("00:00:09.000");
    }

    [Fact]
    public void BuildAll_WithOutOfOrderExactTimestamps_MarksClampedEntriesEstimated()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 3);

        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [5.0, 2.0, 6.0],
            [],
            temp.Path);

        manifest.Keyframes.Select(entry => entry.TimestampSeconds)
            .Should().Equal(5.0, 5.0, 6.0);
        manifest.Keyframes.Select(entry => entry.TimestampEstimated)
            .Should().Equal(false, true, false);
    }

    [Fact]
    public void BuildAll_WithNegativeExactTimestamp_MarksClampedEntryEstimated()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);

        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            10.0,
            extracted,
            [-1.0, 2.0],
            [],
            temp.Path);

        manifest.Keyframes[0].TimestampSeconds.Should().Be(0.0);
        manifest.Keyframes[0].TimestampEstimated.Should().BeTrue();
        manifest.Keyframes[1].TimestampEstimated.Should().BeFalse();
    }

    [Fact]
    public void BuildAll_RoundsTimestampsToMilliseconds()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);

        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            5.0,
            extracted,
            [1.23456, 2.34567],
            [],
            temp.Path);

        manifest.Keyframes[0].TimestampSeconds.Should().Be(1.235);
        manifest.Keyframes[0].Timestamp.Should().Be("00:00:01.235");
        manifest.Keyframes[1].TimestampSeconds.Should().Be(2.346);
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
    public void WriteAll_CreatesAllKeyframesManifest()
    {
        using var temp = new TempDirectory();
        var manifest = KeyframeManifestWriter.BuildAll(
            "/videos/talk.mp4",
            2.0,
            CreateKeyframePaths(temp.Path, 2),
            [0.0, 1.0],
            [],
            temp.Path);

        var path = KeyframeManifestWriter.WriteAll(temp.Path, manifest);

        path.Should().Be(Path.Combine(temp.Path, "keyframes.json"));
        File.Exists(path).Should().BeTrue();
        var json = File.ReadAllText(path);
        json.Should().Contain("\"keyframeCount\": 2");
        json.Should().Contain("\"file\": \"keyframes/kf_000001.jpg\"");
        json.Should().NotContain("\"kept\"");
    }

    [Fact]
    public void WriteFinal_CreatesFinalKeyframesManifest()
    {
        using var temp = new TempDirectory();
        var extracted = CreateKeyframePaths(temp.Path, 2);
        var deduplication = new DeduplicationResult
        {
            TotalFiles = 2,
            KeptFiles = 1,
            RemovedFiles = 1,
            KeptPaths = [Path.Combine(temp.Path, "final_keyframes", "kf_000001.jpg")],
        };
        var manifest = KeyframeManifestWriter.BuildFinal(
            "/videos/talk.mp4",
            2.0,
            extracted,
            [0.0, 1.0],
            [],
            temp.Path,
            deduplication);

        var path = KeyframeManifestWriter.WriteFinal(temp.Path, manifest);

        path.Should().Be(Path.Combine(temp.Path, "final_keyframes.json"));
        File.Exists(path).Should().BeTrue();
        var json = File.ReadAllText(path);
        json.Should().Contain("\"keyframeCount\": 1");
        json.Should().Contain("\"file\": \"final_keyframes/kf_000001.jpg\"");
        json.Should().NotContain("\"kept\"");
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
