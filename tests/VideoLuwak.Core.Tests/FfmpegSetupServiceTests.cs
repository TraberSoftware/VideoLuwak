using FluentAssertions;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Ffmpeg;

namespace VideoLuwak.Core.Tests;

public sealed class FfmpegSetupServiceTests
{
    [Fact]
    public async Task EnsureAvailableAsync_WhenBothExecutablesExist_ReturnsAlreadyAvailable()
    {
        var service = new FfmpegSetupService(new FakeLocator(true, true), new ProcessRunner());

        var result = await service.EnsureAvailableAsync();

        result.Status.Should().Be(FfmpegSetupStatus.AlreadyAvailable);
        result.IsReady.Should().BeTrue();
        result.FfmpegPath.Should().Be("/fake/ffmpeg");
        result.FfprobePath.Should().Be("/fake/ffprobe");
    }

    [Fact]
    public async Task EnsureAvailableAsync_WhenMissingOnNonWindows_ReturnsInstructions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var service = new FfmpegSetupService(new FakeLocator(false, false), new ProcessRunner());

        var result = await service.EnsureAvailableAsync();

        result.Status.Should().Be(FfmpegSetupStatus.UnsupportedPlatform);
        result.IsReady.Should().BeFalse();
        result.Message.Should().Contain("FFmpeg");
    }

    private sealed class FakeLocator(bool hasFfmpeg, bool hasFfprobe) : IFfmpegLocator
    {
        public string ResolveFfmpeg() =>
            hasFfmpeg ? "/fake/ffmpeg" : throw new FfmpegNotFoundException("ffmpeg missing");

        public string ResolveFfprobe() =>
            hasFfprobe ? "/fake/ffprobe" : throw new FfmpegNotFoundException("ffprobe missing");

        public bool TryResolveFfmpeg(out string path)
        {
            path = hasFfmpeg ? "/fake/ffmpeg" : string.Empty;
            return hasFfmpeg;
        }

        public bool TryResolveFfprobe(out string path)
        {
            path = hasFfprobe ? "/fake/ffprobe" : string.Empty;
            return hasFfprobe;
        }
    }
}
