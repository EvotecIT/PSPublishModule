namespace PowerForge.Tests;

public sealed class FrameworkCompatibilityPathComparerTests
{
    [Fact]
    public void FileSystemPathComparisonCache_ProbesEachDirectoryOnlyOnce()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        int probes = 0;
        var cache = new FileSystemPathComparisonCache(
            _ =>
            {
                probes++;
                return true;
            },
            () => StringComparison.OrdinalIgnoreCase,
            StringComparer.Ordinal);

        Assert.Equal(StringComparison.Ordinal, cache.GetComparison(root));
        Assert.Equal(StringComparison.Ordinal, cache.GetComparison(root + Path.DirectorySeparatorChar));
        Assert.Equal(StringComparison.Ordinal, cache.GetComparison(root));
        Assert.Equal(1, probes);
    }

    [Fact]
    public void FileSystemPathComparisonCache_CachesProbeFailureFallback()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        int probes = 0;
        var cache = new FileSystemPathComparisonCache(
            _ =>
            {
                probes++;
                throw new UnauthorizedAccessException("read-only");
            },
            () => StringComparison.OrdinalIgnoreCase,
            StringComparer.Ordinal);

        Assert.Equal(StringComparison.OrdinalIgnoreCase, cache.GetComparison(root));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, cache.GetComparison(root));
        Assert.Equal(1, probes);
    }
}
