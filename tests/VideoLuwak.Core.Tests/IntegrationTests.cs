using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using VideoLuwak.Core.Composition;
using VideoLuwak.Core.Models;
using VideoLuwak.Core.Pipeline;

namespace VideoLuwak.Core.Tests;

/// <summary>
/// End-to-end tests that exercise the real ffmpeg/ffprobe toolchain installed on the
/// machine. When either executable cannot be resolved the test returns early (success),
/// so the suite still passes on machines without ffmpeg.
/// </summary>
public sealed class IntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task FullPipeline_MultiAudioVideo_ExtractsAudioKeyframesAndDeduplicates()
    {
        var locator = ExtractorFactory.CreateLocator();
        if (!locator.TryResolveFfmpeg(out var ffmpeg) || !locator.TryResolveFfprobe(out var ffprobe))
        {
            return; // ffmpeg/ffprobe unavailable on this machine.
        }

        using var temp = new TempDirectory();
        var moviePath = Path.Combine(temp.Path, "input", "movie.mp4");
        CreateVideo(ffmpeg, moviePath, multiAudio: true);
        File.Exists(moviePath).Should().BeTrue();
        new FileInfo(moviePath).Length.Should().BeGreaterThan(0);

        var outputBase = Path.Combine(temp.Path, "out");
        var processor = ExtractorFactory.CreateVideoProcessor();
        var options = new ProcessingOptions
        {
            OutputBaseDirectory = outputBase,
            PhashSimilarityThreshold = 8,
        };

        var result = await processor.ProcessAsync(moviePath, options);

        result.Success.Should().BeTrue(result.Error);
        var outputDirectory = Path.Combine(outputBase, "movie");
        result.OutputDirectory.Should().Be(outputDirectory);

        // --- audio -----------------------------------------------------------------
        var audioDirectory = Path.Combine(outputDirectory, "audio");
        Directory.GetFiles(audioDirectory, "*.wav").Should().HaveCount(2);
        result.AudioStreams.Should().HaveCount(2);

        foreach (var stream in result.AudioStreams)
        {
            File.Exists(stream.OutputPath).Should().BeTrue();
            stream.OutputSampleRate.Should().Be(16000);
            stream.OutputChannels.Should().Be(1);
            stream.OutputSampleFormat.Should().Be("pcm_s16le");
            stream.SttOptimized.Should().BeTrue();

            var probe = await ProbeAudioAsync(ffprobe, stream.OutputPath);
            probe.CodecName.Should().Be("pcm_s16le");
            probe.SampleFormat.Should().Be("s16");
            probe.SampleRate.Should().Be(16000);
            probe.Channels.Should().Be(1);
        }

        var descriptionPath = Path.Combine(audioDirectory, "audio_streams.json");
        File.Exists(descriptionPath).Should().BeTrue();
        using (var document = JsonDocument.Parse(File.ReadAllText(descriptionPath)))
        {
            var root = document.RootElement;
            root.GetProperty("sttOptimized").GetBoolean().Should().BeTrue();

            var target = root.GetProperty("target");
            target.GetProperty("sampleRate").GetInt32().Should().Be(16000);
            target.GetProperty("channels").GetInt32().Should().Be(1);
            target.GetProperty("sampleFormat").GetString().Should().Be("pcm_s16le");

            var streams = root.GetProperty("streams");
            streams.GetArrayLength().Should().Be(2);
            foreach (var stream in streams.EnumerateArray())
            {
                stream.GetProperty("outputSampleRate").GetInt32().Should().Be(16000);
                stream.GetProperty("outputChannels").GetInt32().Should().Be(1);
                stream.GetProperty("outputSampleFormat").GetString().Should().Be("pcm_s16le");
                stream.GetProperty("sttOptimized").GetBoolean().Should().BeTrue();
                stream.GetProperty("outputDescription").GetString().Should().NotBeNullOrWhiteSpace();
            }

            var languages = streams.EnumerateArray()
                .Select(stream => stream.GetProperty("language").GetString())
                .ToArray();
            languages.Should().BeEquivalentTo(["eng", "spa"]);
        }

        // --- keyframes -------------------------------------------------------------
        var keyframesDirectory = Path.Combine(outputDirectory, "keyframes");
        var extracted = Directory.GetFiles(keyframesDirectory, "kf_*.jpg");
        extracted.Should().NotBeEmpty();
        result.KeyframesExtracted.Should().Be(extracted.Length);

        var finalDirectory = Path.Combine(outputDirectory, "final_keyframes");
        Directory.Exists(finalDirectory).Should().BeTrue();
        Directory.GetFiles(finalDirectory).Should().HaveCount(result.KeyframesKept);
        (result.KeyframesKept + result.KeyframesRemoved).Should().Be(result.KeyframesExtracted);

        // --- keyframe manifest -----------------------------------------------------
        var manifestPath = Path.Combine(outputDirectory, "keyframes.json");
        File.Exists(manifestPath).Should().BeTrue();
        result.KeyframeManifestPath.Should().Be(manifestPath);

        using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            var manifest = manifestDocument.RootElement;
            manifest.GetProperty("video").GetString().Should().Be(Path.GetFullPath(moviePath));
            manifest.GetProperty("timeFormat").GetString().Should().Be("HH:MM:SS.mmm");
            manifest.GetProperty("audio").GetArrayLength().Should().Be(result.AudioStreams.Count);

            var manifestKeyframes = manifest.GetProperty("keyframes");
            manifestKeyframes.GetArrayLength().Should().Be(result.KeyframesExtracted);

            var previousTimestamp = -1.0;
            var kept = 0;
            foreach (var keyframe in manifestKeyframes.EnumerateArray())
            {
                var timestamp = keyframe.GetProperty("timestampSeconds").GetDouble();
                timestamp.Should().BeGreaterThanOrEqualTo(0.0);
                timestamp.Should().BeLessThanOrEqualTo(result.DurationSeconds + 0.001);
                timestamp.Should().BeGreaterThanOrEqualTo(previousTimestamp);
                previousTimestamp = timestamp;

                if (keyframe.GetProperty("kept").GetBoolean())
                {
                    kept++;
                    var end = keyframe.GetProperty("endTimestampSeconds").GetDouble();
                    end.Should().BeGreaterThanOrEqualTo(timestamp);
                }
                else
                {
                    keyframe.GetProperty("endTimestampSeconds").ValueKind.Should().Be(JsonValueKind.Null);
                }
            }

            kept.Should().Be(result.KeyframesKept);
        }

        // --- persisted result ------------------------------------------------------
        var resultPath = Path.Combine(outputDirectory, "processing_result.json");
        File.Exists(resultPath).Should().BeTrue();
        var persisted = JsonSerializer.Deserialize<ProcessingResult>(File.ReadAllText(resultPath), JsonOptions);
        persisted.Should().NotBeNull();
        persisted!.Success.Should().BeTrue();
        persisted.KeyframesExtracted.Should().Be(result.KeyframesExtracted);
        persisted.KeyframesKept.Should().Be(result.KeyframesKept);
        persisted.KeyframesRemoved.Should().Be(result.KeyframesRemoved);
        persisted.KeyframeManifestPath.Should().Be(manifestPath);
        persisted.AudioStreams.Should().HaveCount(2);
    }

    [Fact]
    public async Task FullPipeline_WithoutSttOptimization_PreservesSourceSampleRateAndChannels()
    {
        var locator = ExtractorFactory.CreateLocator();
        if (!locator.TryResolveFfmpeg(out var ffmpeg) || !locator.TryResolveFfprobe(out var ffprobe))
        {
            return; // ffmpeg/ffprobe unavailable on this machine.
        }

        using var temp = new TempDirectory();
        var moviePath = Path.Combine(temp.Path, "input", "movie.mp4");
        CreateVideo(ffmpeg, moviePath, multiAudio: false, sampleRate: 48000, channels: 2);
        File.Exists(moviePath).Should().BeTrue();

        var outputBase = Path.Combine(temp.Path, "out");
        var processor = ExtractorFactory.CreateVideoProcessor();
        var options = new ProcessingOptions
        {
            OutputBaseDirectory = outputBase,
            SttOptimizeAudio = false,
        };

        var result = await processor.ProcessAsync(moviePath, options);

        result.Success.Should().BeTrue(result.Error);
        var outputDirectory = Path.Combine(outputBase, "movie");
        var audioDirectory = Path.Combine(outputDirectory, "audio");
        var wavFiles = Directory.GetFiles(audioDirectory, "*.wav");
        wavFiles.Should().ContainSingle();

        var probe = await ProbeAudioAsync(ffprobe, wavFiles[0]);
        probe.CodecName.Should().Be("pcm_s16le");
        probe.SampleFormat.Should().Be("s16");
        probe.SampleRate.Should().Be(48000);
        probe.Channels.Should().Be(2);

        var streamResult = result.AudioStreams.Should().ContainSingle().Subject;
        streamResult.OutputSampleRate.Should().Be(48000);
        streamResult.OutputChannels.Should().Be(2);
        streamResult.OutputSampleFormat.Should().Be("pcm_s16le");
        streamResult.SttOptimized.Should().BeFalse();

        var descriptionPath = Path.Combine(audioDirectory, "audio_streams.json");
        File.Exists(descriptionPath).Should().BeTrue();
        using var document = JsonDocument.Parse(File.ReadAllText(descriptionPath));
        document.RootElement.GetProperty("sttOptimized").GetBoolean().Should().BeFalse();
        var entry = document.RootElement.GetProperty("streams")[0];
        entry.GetProperty("outputSampleRate").GetInt32().Should().Be(48000);
        entry.GetProperty("outputChannels").GetInt32().Should().Be(2);
        entry.GetProperty("outputSampleFormat").GetString().Should().Be("pcm_s16le");
        entry.GetProperty("sttOptimized").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task FullPipeline_WithoutDeduplication_SkipsFinalKeyframes()
    {
        var locator = ExtractorFactory.CreateLocator();
        if (!locator.TryResolveFfmpeg(out var ffmpeg) || !locator.TryResolveFfprobe(out _))
        {
            return; // ffmpeg/ffprobe unavailable on this machine.
        }

        using var temp = new TempDirectory();
        var moviePath = Path.Combine(temp.Path, "input", "movie.mp4");
        CreateVideo(ffmpeg, moviePath, multiAudio: false);
        File.Exists(moviePath).Should().BeTrue();

        var outputBase = Path.Combine(temp.Path, "out");
        var processor = ExtractorFactory.CreateVideoProcessor();
        var options = new ProcessingOptions
        {
            OutputBaseDirectory = outputBase,
            DeduplicateKeyframes = false,
        };

        var result = await processor.ProcessAsync(moviePath, options);

        result.Success.Should().BeTrue(result.Error);
        result.KeyframesKept.Should().Be(0);
        result.KeyframesRemoved.Should().Be(0);

        var outputDirectory = Path.Combine(outputBase, "movie");
        Directory.Exists(Path.Combine(outputDirectory, "final_keyframes")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(outputDirectory, "audio"), "*.wav").Should().ContainSingle();
        Directory.GetFiles(Path.Combine(outputDirectory, "keyframes"), "kf_*.jpg").Should().NotBeEmpty();

        var manifestPath = Path.Combine(outputDirectory, "keyframes.json");
        File.Exists(manifestPath).Should().BeTrue();
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifestKeyframes = manifestDocument.RootElement.GetProperty("keyframes");
        manifestKeyframes.GetArrayLength().Should().Be(result.KeyframesExtracted);
        foreach (var keyframe in manifestKeyframes.EnumerateArray())
        {
            keyframe.GetProperty("kept").GetBoolean().Should().BeTrue();
            keyframe.GetProperty("endTimestampSeconds").ValueKind.Should().NotBe(JsonValueKind.Null);

            var file = keyframe.GetProperty("file").GetString() ?? string.Empty;
            file.Should().StartWith("keyframes/");
            file.Should().Be(keyframe.GetProperty("sourceFile").GetString());
            File.Exists(Path.Combine(outputDirectory, file.Replace('/', Path.DirectorySeparatorChar)))
                .Should().BeTrue();
        }
    }

    private static void CreateVideo(
        string ffmpeg,
        string outputPath,
        bool multiAudio,
        int sampleRate = 48000,
        int channels = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // libx264 is preferred; mpeg4 is a widely available fallback.
        Exception? lastError = null;
        foreach (var videoCodec in new[] { "libx264", "mpeg4" })
        {
            try
            {
                RunFfmpeg(ffmpeg, BuildVideoArguments(outputPath, multiAudio, videoCodec, sampleRate, channels));
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Could not create a test video with any available codec.", lastError);
    }

    private static IReadOnlyList<string> BuildVideoArguments(
        string outputPath,
        bool multiAudio,
        string videoCodec,
        int sampleRate,
        int channels)
    {
        var rateText = sampleRate.ToString(CultureInfo.InvariantCulture);
        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=5",
            "-f", "lavfi", "-i", $"sine=frequency=440:sample_rate={rateText}:duration=5",
        };

        if (multiAudio)
        {
            arguments.AddRange(["-f", "lavfi", "-i", $"sine=frequency=880:sample_rate={rateText}:duration=5"]);
        }

        arguments.AddRange(["-map", "0:v"]);
        if (multiAudio)
        {
            arguments.AddRange(
                ["-map", "1:a", "-map", "2:a", "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=spa"]);
        }
        else
        {
            arguments.AddRange(["-map", "1:a", "-metadata:s:a:0", "language=eng"]);
        }

        arguments.AddRange(
        [
            "-c:v", videoCodec, "-g", "30", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-ar", rateText, "-ac", channels.ToString(CultureInfo.InvariantCulture),
            outputPath,
        ]);

        return arguments;
    }

    private static async Task<AudioProbeInfo> ProbeAudioAsync(string ffprobe, string wavPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in new[]
                 {
                     "-v", "error",
                     "-select_streams", "a:0",
                     "-show_entries", "stream=codec_name,sample_fmt,sample_rate,channels",
                     "-of", "json",
                     wavPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffprobe.");

        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffprobe exited with code {process.ExitCode} for '{wavPath}': {standardError}");
        }

        using var document = JsonDocument.Parse(standardOutput);
        var stream = document.RootElement.GetProperty("streams")[0];
        return new AudioProbeInfo(
            stream.GetProperty("codec_name").GetString() ?? string.Empty,
            stream.GetProperty("sample_fmt").GetString() ?? string.Empty,
            int.Parse(stream.GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture),
            stream.GetProperty("channels").GetInt32());
    }

    private sealed record AudioProbeInfo(string CodecName, string SampleFormat, int SampleRate, int Channels);

    private static void RunFfmpeg(string ffmpeg, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg.");

        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg exited with code {process.ExitCode}: {standardError}");
        }
    }
}
