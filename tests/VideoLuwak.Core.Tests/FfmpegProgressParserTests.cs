using FluentAssertions;
using VideoLuwak.Core.Media;

namespace VideoLuwak.Core.Tests;

public sealed class FfmpegProgressParserTests
{
    [Fact]
    public void Update_OutTimeUs_ComputesPercent()
    {
        var parser = new FfmpegProgressParser(10);

        var percent = parser.Update("out_time_us=5000000");

        percent.Should().BeApproximately(50.0, 0.001);
    }

    [Fact]
    public void Update_OutTimeMs_IsTreatedAsMicroseconds()
    {
        var parser = new FfmpegProgressParser(10);

        var percent = parser.Update("out_time_ms=2000000");

        percent.Should().BeApproximately(20.0, 0.001);
    }

    [Fact]
    public void Update_OutTimeTimestamp_ComputesPercent()
    {
        var parser = new FfmpegProgressParser(10);

        var percent = parser.Update("out_time=00:00:07.5");

        percent.Should().BeApproximately(75.0, 0.001);
    }

    [Fact]
    public void Update_ProgressEnd_IsCompleteAt100()
    {
        var parser = new FfmpegProgressParser(10);
        parser.Update("out_time_us=1000000");

        var percent = parser.Update("progress=end");

        percent.Should().Be(100.0);
        parser.IsComplete.Should().BeTrue();
        parser.CurrentPercent().Should().Be(100.0);
    }

    [Fact]
    public void UnknownDuration_BeforeEnd_ReturnsMinusOne_AfterEnd_Returns100()
    {
        var parser = new FfmpegProgressParser(0);

        parser.Update("out_time_us=5000000").Should().Be(-1.0);
        parser.CurrentPercent().Should().Be(-1.0);

        parser.Update("progress=end").Should().Be(100.0);
        parser.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void NegativeDuration_IsTreatedAsUnknown()
    {
        var parser = new FfmpegProgressParser(-5);

        parser.DurationSeconds.Should().Be(0);
        parser.Update("out_time_us=1000000").Should().Be(-1.0);
    }

    [Fact]
    public void ElapsedSeconds_ReflectsLastObservedPosition()
    {
        var parser = new FfmpegProgressParser(10);

        parser.Update("out_time_us=3000000");

        parser.ElapsedSeconds.Should().BeApproximately(3.0, 0.001);
    }

    [Fact]
    public void GarbageLines_ReturnCurrentPercent_WithoutThrowing()
    {
        var parser = new FfmpegProgressParser(10);
        parser.Update("out_time_us=5000000");
        var baseline = parser.CurrentPercent();

        parser.Update("benchmark: 1.0").Should().Be(baseline);
        parser.Update("no-equals-sign").Should().Be(baseline);
        parser.Update(string.Empty).Should().Be(baseline);
        parser.Update("=onlyvalue").Should().Be(baseline);
    }

    [Fact]
    public void Percent_IsClampedTo100()
    {
        var parser = new FfmpegProgressParser(1);

        // Reported position beyond the known duration must clamp, not exceed 100.
        parser.Update("out_time_us=5000000").Should().Be(100.0);
    }
}
