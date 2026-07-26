namespace PowerForge.Tests;

public sealed class AppleReleaseArtifactServiceTests
{
    [Fact]
    public void RemoveCurrentArtifacts_RefusesPathsOutsideConfiguredProjectRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge.Outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = Path.Combine(outside, "App.xcarchive"),
                        ExportPath = Path.Combine(root, "exports", "App")
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(plan));

            Assert.Contains("inside AppleApps.ProjectRoot", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            if (Directory.Exists(outside))
                Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void Preflight_RemovesOnlyStaleEntriesUnderConfiguredRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var archiveRoot = Path.Combine(root, "archives", "iOS");
        var exportRoot = Path.Combine(root, "exports", "iOS");
        Directory.CreateDirectory(archiveRoot);
        Directory.CreateDirectory(exportRoot);
        var stale = Path.Combine(archiveRoot, "old.xcarchive");
        var current = Path.Combine(archiveRoot, "current.xcarchive");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(stale, "old.bin"), "old");
        File.WriteAllText(Path.Combine(current, "current.bin"), "current");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-10));
        Directory.SetLastWriteTimeUtc(current, DateTime.UtcNow);
        try
        {
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Automation = new PowerForgeAppleReleaseAutomationOptions
                {
                    MinimumFreeSpaceGB = 0,
                    CleanupBeforeArchive = true,
                    ArtifactRetentionDays = 7
                },
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = Path.Combine(archiveRoot, "App.xcarchive"),
                        ExportPath = Path.Combine(exportRoot, "App")
                    }
                }
            };

            var receipt = new AppleReleaseArtifactService(_ => 100_000_000).Preflight(plan);

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(current));
            Assert.Contains("archives/iOS/old.xcarchive", receipt.RemovedPaths);
            Assert.DoesNotContain("archives/iOS/current.xcarchive", receipt.RemovedPaths);
            Assert.DoesNotContain(root, string.Join("|", receipt.RemovedPaths), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_RefusesSymbolicLinkArtifactRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge.Outside", Guid.NewGuid().ToString("N"));
        var link = Path.Combine(root, "archives");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.bin"), "keep");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception linkCreationException) when (
                linkCreationException is PlatformNotSupportedException ||
                linkCreationException is UnauthorizedAccessException ||
                linkCreationException is IOException)
            {
                return;
            }

            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = Path.Combine(link, "App.xcarchive"),
                        ExportPath = Path.Combine(root, "exports", "App")
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(plan));

            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(outside, "keep.bin")));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            if (Directory.Exists(outside))
                Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_UsesCaseSensitiveContainmentOnUnix()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var parent = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "Project");
        var caseVariant = Path.Combine(parent, "project");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(caseVariant);
        try
        {
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = Path.Combine(caseVariant, "archives", "App.xcarchive"),
                        ExportPath = Path.Combine(root, "exports", "App")
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(plan));

            Assert.Contains("inside AppleApps.ProjectRoot", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, true);
        }
    }
}
