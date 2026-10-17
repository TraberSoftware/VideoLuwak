using FluentAssertions;
using VideoLuwak.Core.Abstractions;
using VideoLuwak.Core.Hashing;

namespace VideoLuwak.Core.Tests;

public sealed class KeyframeDeduplicatorTests
{
    private readonly KeyframeDeduplicator _deduplicator = new();

    [Fact]
    public void FirstFile_IsAlwaysKept()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000001.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            ["kf_000001.jpg"] = 0,
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 0, hasher);

        result.TotalFiles.Should().Be(1);
        result.KeptFiles.Should().Be(1);
        result.RemovedFiles.Should().Be(0);
        result.KeptPaths.Should().ContainSingle();
        result.RemovedPaths.Should().BeEmpty();
        File.Exists(Path.Combine(destination, "kf_000001.jpg")).Should().BeTrue();
    }

    [Fact]
    public void RollingReference_KeepsOnlyFirst_WhenChainDriftsPastThreshold()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000001.jpg", "kf_000002.jpg", "kf_000003.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            // Consecutive distances are 2 and 2 (both <= threshold 2), but first vs last is 4.
            ["kf_000001.jpg"] = 0b0000,
            ["kf_000002.jpg"] = 0b0011,
            ["kf_000003.jpg"] = 0b1111,
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 2, hasher);

        result.TotalFiles.Should().Be(3);
        result.KeptFiles.Should().Be(1);
        result.RemovedFiles.Should().Be(2);
        result.KeptPaths.Should().ContainSingle().Which.Should().Be(Path.Combine(destination, "kf_000001.jpg"));

        var copied = Directory.GetFiles(destination).Select(Path.GetFileName).ToArray();
        copied.Should().BeEquivalentTo(["kf_000001.jpg"]);
    }

    [Fact]
    public void DistanceEqualToThreshold_IsRemoved()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000001.jpg", "kf_000002.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            ["kf_000001.jpg"] = 0b000,
            ["kf_000002.jpg"] = 0b111, // Hamming distance 3 == threshold
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 3, hasher);

        result.KeptFiles.Should().Be(1);
        result.RemovedFiles.Should().Be(1);
        File.Exists(Path.Combine(destination, "kf_000002.jpg")).Should().BeFalse();
    }

    [Fact]
    public void DistanceOneAboveThreshold_IsKept()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000001.jpg", "kf_000002.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            ["kf_000001.jpg"] = 0b0000,
            ["kf_000002.jpg"] = 0b1111, // Hamming distance 4 == threshold + 1
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 3, hasher);

        result.KeptFiles.Should().Be(2);
        result.RemovedFiles.Should().Be(0);
        File.Exists(Path.Combine(destination, "kf_000002.jpg")).Should().BeTrue();
    }

    [Fact]
    public void EmptySourceDirectory_ReturnsZeroedResult()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "keyframes");
        Directory.CreateDirectory(source);
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>());

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 8, hasher);

        result.TotalFiles.Should().Be(0);
        result.KeptFiles.Should().Be(0);
        result.RemovedFiles.Should().Be(0);
        result.KeptPaths.Should().BeEmpty();
        result.RemovedPaths.Should().BeEmpty();
    }

    [Fact]
    public void MissingSourceDirectory_ReturnsZeroedResult_WithoutThrowing()
    {
        using var temp = new TempDirectory();
        var source = Path.Combine(temp.Path, "does-not-exist");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>());

        var act = () => _deduplicator.Deduplicate(source, destination, similarityThreshold: 8, hasher);

        var result = act.Should().NotThrow().Subject;
        result.TotalFiles.Should().Be(0);
        result.KeptFiles.Should().Be(0);
        result.RemovedFiles.Should().Be(0);
        result.KeptPaths.Should().BeEmpty();
        result.RemovedPaths.Should().BeEmpty();
    }

    [Fact]
    public void DotFiles_AreSkipped()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, ".hidden", "kf_000001.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            ["kf_000001.jpg"] = 0,
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 8, hasher);

        result.TotalFiles.Should().Be(1);
        hasher.CallOrder.Should().NotContain(".hidden");
        hasher.CallOrder.Should().BeEquivalentTo(["kf_000001.jpg"]);
    }

    [Fact]
    public void Files_AreProcessedInAscendingOrdinalOrder()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000010.jpg", "kf_000002.jpg", "kf_000001.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            // Distant hashes so every frame is kept and the whole sequence is visited.
            ["kf_000001.jpg"] = 0x0000000000000000UL,
            ["kf_000002.jpg"] = 0x00000000000000FFUL,
            ["kf_000010.jpg"] = 0xFFFFFFFFFFFFFFFFUL,
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 0, hasher);

        hasher.CallOrder.Should().Equal("kf_000001.jpg", "kf_000002.jpg", "kf_000010.jpg");
        result.KeptPaths.Select(Path.GetFileName).Should().Equal("kf_000001.jpg", "kf_000002.jpg", "kf_000010.jpg");
    }

    [Fact]
    public void KeptPathsAreDestination_RemovedPathsAreSource_AndCountsAddUp()
    {
        using var temp = new TempDirectory();
        var source = CreateSource(temp, "kf_000001.jpg", "kf_000002.jpg", "kf_000003.jpg");
        var destination = Path.Combine(temp.Path, "final");
        var hasher = new FakePerceptualHasher(new Dictionary<string, ulong>
        {
            ["kf_000001.jpg"] = 0b0000,
            ["kf_000002.jpg"] = 0b0000, // duplicate of previous -> removed
            ["kf_000003.jpg"] = 0b1111, // distance 4 from rolled reference -> kept
        });

        var result = _deduplicator.Deduplicate(source, destination, similarityThreshold: 2, hasher);

        (result.KeptFiles + result.RemovedFiles).Should().Be(result.TotalFiles);
        result.KeptFiles.Should().Be(2);
        result.RemovedFiles.Should().Be(1);

        result.KeptPaths.Should().OnlyContain(path => path.StartsWith(destination, StringComparison.Ordinal));
        result.RemovedPaths.Should().OnlyContain(path => path.StartsWith(source, StringComparison.Ordinal));
        result.RemovedPaths.Should().ContainSingle().Which.Should().Be(Path.Combine(source, "kf_000002.jpg"));
    }

    private static string CreateSource(TempDirectory temp, params string[] fileNames)
    {
        var source = Path.Combine(temp.Path, "keyframes");
        Directory.CreateDirectory(source);
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(source, fileName), fileName);
        }

        return source;
    }
}
