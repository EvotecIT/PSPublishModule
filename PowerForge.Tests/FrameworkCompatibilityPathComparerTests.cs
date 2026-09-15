namespace PowerForge.Tests;

public sealed class FrameworkCompatibilityPathComparerTests
{
    [Theory]
    [InlineData(true, false, StringComparison.OrdinalIgnoreCase)]
    [InlineData(false, true, StringComparison.OrdinalIgnoreCase)]
    [InlineData(false, false, StringComparison.Ordinal)]
    public void DefaultExistingFileSystemPathComparison_UsesPlatformFileSystemDefault(
        bool isWindows,
        bool isMacOS,
        StringComparison expected)
    {
        Assert.Equal(
            expected,
            FrameworkCompatibility.DefaultExistingFileSystemPathStringComparison(isWindows, isMacOS));
    }

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
            () => StringComparison.OrdinalIgnoreCase);

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
            () => StringComparison.OrdinalIgnoreCase);

        Assert.Equal(StringComparison.OrdinalIgnoreCase, cache.GetComparison(root));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, cache.GetComparison(root));
        Assert.Equal(1, probes);
    }

    [Fact]
    public void FileSystemPathComparisonCache_ProbesCaseDistinctDirectoryKeysSeparately()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string upper = Path.Combine(parent, "CaseRoot");
        string lower = Path.Combine(parent, "caseroot");
        int probes = 0;
        var cache = new FileSystemPathComparisonCache(
            path =>
            {
                probes++;
                return string.Equals(path, upper, StringComparison.Ordinal);
            },
            () => StringComparison.OrdinalIgnoreCase);

        Assert.Equal(StringComparison.Ordinal, cache.GetComparison(upper));
        Assert.Equal(StringComparison.OrdinalIgnoreCase, cache.GetComparison(lower));
        Assert.Equal(2, probes);
    }

    [Fact]
    public void FileSystemAwarePathComparer_UsesParentSemanticsForFirstCaseDistinctComponent()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string parent = Path.Combine(root, "case-sensitive-parent");
        string upper = Path.Combine(parent, "Foo", "payload.zip");
        string lower = Path.Combine(parent, "foo", "payload.zip");
        var comparer = new FrameworkCompatibility.FileSystemAwarePathComparer(path =>
            string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                parent,
                StringComparison.Ordinal)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);

        Assert.False(comparer.Equals(upper, lower));
        Assert.NotEqual(0, comparer.Compare(upper, lower));
        Assert.Equal(comparer.GetHashCode(upper), comparer.GetHashCode(lower));
    }

    [Fact]
    public void FileSystemAwarePathComparer_IgnoresLeafModesWhenParentResolvesCaseVariantsTogether()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        string parent = Path.Combine(root, "case-insensitive-parent");
        string upperDirectory = Path.Combine(parent, "Foo");
        string upper = Path.Combine(upperDirectory, "payload.zip");
        string lower = Path.Combine(parent, "foo", "payload.zip");
        var comparer = new FrameworkCompatibility.FileSystemAwarePathComparer(path =>
            string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                upperDirectory,
                StringComparison.Ordinal)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);

        Assert.True(comparer.Equals(upper, lower));
        Assert.Equal(0, comparer.Compare(upper, lower));
    }

    [Fact]
    public void FileSystemAwarePathComparer_DoesNotProbeUnrelatedPathsForEquality()
    {
        int probes = 0;
        var comparer = new FrameworkCompatibility.FileSystemAwarePathComparer(_ =>
        {
            probes++;
            return StringComparison.OrdinalIgnoreCase;
        });

        Assert.False(comparer.Equals(
            Path.Combine("sdk", "Microsoft.Common.targets"),
            Path.Combine("repo", "Custom.targets")));
        Assert.Equal(0, probes);
    }
}
