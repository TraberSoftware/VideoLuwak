using VideoLuwak.Core.Abstractions;

namespace VideoLuwak.Core.Tests;

/// <summary>
/// Creates a unique temporary directory under the OS temp path and removes it
/// (recursively) when disposed. Never leaves artifacts behind.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "VideoLuwak.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = Path;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return System.IO.Path.Combine(all);
    }

    public string CreateFile(string fileName, string contents = "x")
    {
        var fullPath = System.IO.Path.Combine(Path, fileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup; a locked file must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }
}

/// <summary>
/// A deterministic <see cref="IPerceptualHasher"/> backed by a filename -&gt; hash map.
/// Records the order in which files were hashed so ordering can be asserted.
/// </summary>
internal sealed class FakePerceptualHasher : IPerceptualHasher
{
    private readonly Dictionary<string, ulong> _hashes;

    public FakePerceptualHasher(Dictionary<string, ulong> hashes)
    {
        _hashes = new Dictionary<string, ulong>(hashes, StringComparer.Ordinal);
    }

    public List<string> CallOrder { get; } = [];

    public ulong ComputeHash(string imagePath)
    {
        var name = System.IO.Path.GetFileName(imagePath);
        CallOrder.Add(name);
        return _hashes[name];
    }
}
