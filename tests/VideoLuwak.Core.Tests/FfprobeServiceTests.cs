using FluentAssertions;
using VideoLuwak.Core.Ffmpeg;

namespace VideoLuwak.Core.Tests;

public sealed class FfprobeServiceTests
{
    [Fact]
    public void ParseKeyframeTimestamps_AcceptsStringAndNumericValuesInPriorityOrder()
    {
        const string json = """
        {
          "format": {
            "start_time": "1.500000"
          },
          "frames": [
            {
              "key_frame": 1,
              "best_effort_timestamp_time": "2.000000",
              "pts_time": "2.000000",
              "pkt_dts_time": "1.433333"
            },
            {
              "key_frame": "1",
              "best_effort_timestamp_time": 4.0,
              "pts_time": "3.900000",
              "pkt_dts_time": "3.800000"
            },
            {
              "key_frame": 0,
              "best_effort_timestamp_time": "4.500000"
            },
            {
              "key_frame": 1,
              "pts_time": "1.000000"
            },
            {
              "key_frame": 1,
              "best_effort_timestamp_time": "N/A",
              "pts_time": "5.750000",
              "pkt_dts_time": "5.700000"
            },
            {
              "key_frame": 1,
              "pkt_dts_time": "7.500000"
            },
            {
              "key_frame": 1,
              "pts_time": null
            }
          ]
        }
        """;

        var timestamps = FfprobeService.ParseKeyframeTimestamps(json);

        timestamps.Should().Equal(0.5, 2.5, 0.0, 4.25, 6.0);
    }

    [Theory]
    [InlineData("""{ "frames": [ { "key_frame": 1, "best_effort_timestamp_time": "3.0" } ] }""")]
    [InlineData("""{ "format": { "start_time": "N/A" }, "frames": [ { "key_frame": 1, "best_effort_timestamp_time": "3.0" } ] }""")]
    [InlineData("""{ "format": { "start_time": null }, "frames": [ { "key_frame": 1, "best_effort_timestamp_time": "3.0" } ] }""")]
    public void ParseKeyframeTimestamps_WithoutUsableStartTime_UsesZeroOffset(string json)
    {
        FfprobeService.ParseKeyframeTimestamps(json).Should().Equal(3.0);
    }

    [Fact]
    public void ParseKeyframeTimestamps_WithoutFrames_ReturnsEmpty()
    {
        FfprobeService.ParseKeyframeTimestamps("""{ "frames": [] }""").Should().BeEmpty();
        FfprobeService.ParseKeyframeTimestamps("""{ }""").Should().BeEmpty();
    }
}
