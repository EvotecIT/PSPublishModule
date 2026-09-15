namespace PowerForge.Tests;

public sealed class FrameworkCompatibilityPathComparerTests
{
    [Fact]
    public void ConservativePathStringComparison_FailsClosedCaseSensitively()
    {
        Assert.Equal(
            StringComparison.Ordinal,
            FrameworkCompatibility.ConservativePathStringComparison());
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

    [Fact]
    public void PathBoundary_UsesParentSemanticsForRootName()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForge.Tests"));
        string root = Path.Combine(parent, "CaseSensitiveRoot");
        string aliasRoot = Path.Combine(parent, "casesensitiveroot");
        var comparer = new FrameworkCompatibility.FileSystemAwarePathComparer(path =>
            string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root,
                StringComparison.OrdinalIgnoreCase)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);
        var caseSensitiveComparer = new FrameworkCompatibility.FileSystemAwarePathComparer(
            _ => StringComparison.Ordinal);

        Assert.True(DotNetPublishPipelineRunner.IsSameOrBelowBuildInputPath(
            Path.Combine(aliasRoot, "input.dll"),
            root,
            comparer));
        Assert.True(DotNetPublishPipelineRunner.ContainsPathWithFileSystemSemantics(
            "reference:" + Path.Combine(aliasRoot, "input.dll"),
            root,
            comparer));
        Assert.False(DotNetPublishPipelineRunner.IsSameOrBelowBuildInputPath(
            Path.Combine(aliasRoot, "input.dll"),
            root,
            caseSensitiveComparer));
        Assert.False(DotNetPublishPipelineRunner.ContainsPathWithFileSystemSemantics(
            "reference:" + Path.Combine(aliasRoot, "input.dll"),
            root,
            caseSensitiveComparer));
    }

    [Fact]
    public void ProjectReferenceGlob_UsesReadOnlyExistingChildSemantics()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        string projects = Path.Combine(root, "Projects");
        string referencedPath = Path.Combine(projects, "Referenced.csproj");
        Directory.CreateDirectory(projects);
        File.WriteAllText(referencedPath, "<Project />");
        try
        {
            string[] before = Directory.GetFileSystemEntries(projects);
            string aliasPath = Path.Combine(projects, "referenced.csproj");
            bool expectedAliasMatch = FileSystemPathSafety.ExistingPathComparer.Equals(
                referencedPath,
                aliasPath);

            Assert.True(DotNetPublishPipelineRunner.TryMatchProjectReferenceGlob(
                root,
                "Projects/*.csproj",
                referencedPath));
            Assert.Equal(
                expectedAliasMatch,
                DotNetPublishPipelineRunner.TryMatchProjectReferenceGlob(
                    root,
                    "Projects/referenced.*",
                    referencedPath));
            Assert.Equal(before, Directory.GetFileSystemEntries(projects));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PathStringComparison_UsesReadOnlyExistingChildEvidence()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string child = Path.Combine(root, "CaseEvidence.txt");
        File.WriteAllText(child, "evidence");
        try
        {
            string[] before = Directory.GetFileSystemEntries(root);
            bool aliasExists = File.Exists(Path.Combine(root, "caseEvidence.txt"));

            Assert.Equal(
                aliasExists ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
                FrameworkCompatibility.GetPathStringComparison(root));
            Assert.Equal(before, Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PathStringComparison_EmptyDirectoryFailsClosedWithoutWriting()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(
                StringComparison.Ordinal,
                FrameworkCompatibility.GetPathStringComparison(root));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
