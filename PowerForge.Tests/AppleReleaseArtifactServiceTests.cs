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
    public void RemoveStaleArtifacts_preserves_case_equivalent_protected_path_on_case_insensitive_volume()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (FrameworkCompatibility.GetPathStringComparison(root) != StringComparison.OrdinalIgnoreCase)
                return;

            var archiveRoot = Path.Combine(root, "archives");
            var exportRoot = Path.Combine(root, "exports");
            var accepted = Directory.CreateDirectory(Path.Combine(archiveRoot, "Accepted.xcarchive"));
            Directory.CreateDirectory(exportRoot);
            File.WriteAllText(Path.Combine(accepted.FullName, "payload"), "accepted notarization bytes");
            Directory.SetLastWriteTimeUtc(accepted.FullName, DateTime.UtcNow.AddDays(-30));
            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Automation = new PowerForgeAppleReleaseAutomationOptions { ArtifactRetentionDays = 7 },
                Apps =
                [
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = Path.Combine(archiveRoot, "App.xcarchive"),
                        ExportPath = Path.Combine(exportRoot, "App")
                    }
                ]
            };

            var receipt = new AppleReleaseArtifactService(_ => long.MaxValue).RemoveStaleArtifacts(
                plan,
                [Path.Combine(archiveRoot, "accepted.xcarchive")]);

            Assert.True(Directory.Exists(accepted.FullName));
            Assert.Empty(receipt.RemovedPaths);
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
    public void RemoveCurrentArtifacts_AllowsStandardVersionedFrameworkLinks()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        var framework = Path.Combine(archive, "Products", "Applications", "App.app", "Frameworks", "LiveKitWebRTC.framework");
        var version = Path.Combine(framework, "Versions", "A");
        Directory.CreateDirectory(Path.Combine(version, "Headers"));
        Directory.CreateDirectory(Path.Combine(version, "Modules"));
        Directory.CreateDirectory(Path.Combine(version, "Resources"));
        File.WriteAllText(Path.Combine(version, "LiveKitWebRTC"), "signed-framework");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(framework, "Versions", "Current"), "A");
            Directory.CreateSymbolicLink(Path.Combine(framework, "Headers"), "Versions/Current/Headers");
            Directory.CreateSymbolicLink(Path.Combine(framework, "Modules"), "Versions/Current/Modules");
            Directory.CreateSymbolicLink(Path.Combine(framework, "Resources"), "Versions/Current/Resources");
            File.CreateSymbolicLink(Path.Combine(framework, "LiveKitWebRTC"), "Versions/Current/LiveKitWebRTC");

            var plan = new PowerForgeAppleReleasePlan
            {
                ProjectRoot = root,
                Apps = new[]
                {
                    new PowerForgeAppleAppReleaseTargetPlan
                    {
                        ArchivePath = archive,
                        ExportPath = Path.Combine(root, "exports", "App")
                    }
                }
            };

            var receipt = new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(plan);

            Assert.False(Directory.Exists(archive));
            Assert.Contains("archives/App.xcarchive", receipt.RemovedPaths);
            Assert.Equal("signed-framework".Length, receipt.ReclaimedBytes);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_RejectsFrameworkLinkThatEscapesItsBundle()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge.Outside", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        var framework = Path.Combine(archive, "Products", "Applications", "App.app", "Frameworks", "LiveKitWebRTC.framework");
        Directory.CreateDirectory(framework);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.bin"), "outside");
        try
        {
            var target = Path.GetRelativePath(framework, Path.Combine(outside, "keep.bin"));
            File.CreateSymbolicLink(Path.Combine(framework, "LiveKitWebRTC"), target);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(CreatePlan(root, archive)));

            Assert.Contains("escapes its bundle", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(outside, "keep.bin")));
            Assert.True(Directory.Exists(archive));
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
    public void RemoveCurrentArtifacts_RejectsAbsoluteFrameworkLink()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        var framework = Path.Combine(archive, "Products", "Applications", "App.app", "Frameworks", "LiveKitWebRTC.framework");
        var version = Path.Combine(framework, "Versions", "A");
        Directory.CreateDirectory(version);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(framework, "Versions", "Current"), version);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(CreatePlan(root, archive)));

            Assert.Contains("absolute symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(archive));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_RejectsFrameworkLinkChainThatEscapesItsBundle()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "PowerForge.Outside", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        var framework = Path.Combine(archive, "Products", "Applications", "App.app", "Frameworks", "LiveKitWebRTC.framework");
        var versions = Path.Combine(framework, "Versions");
        Directory.CreateDirectory(versions);
        Directory.CreateDirectory(Path.Combine(outside, "Headers"));
        try
        {
            var target = Path.GetRelativePath(versions, outside);
            Directory.CreateSymbolicLink(Path.Combine(versions, "Current"), target);
            Directory.CreateSymbolicLink(Path.Combine(framework, "Headers"), "Versions/Current/Headers");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(CreatePlan(root, archive)));

            Assert.Contains("escapes its bundle", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(Path.Combine(outside, "Headers")));
            Assert.True(Directory.Exists(archive));
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
    public void RemoveCurrentArtifacts_RejectsBrokenFrameworkLink()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        var framework = Path.Combine(archive, "Products", "Applications", "App.app", "Frameworks", "LiveKitWebRTC.framework");
        Directory.CreateDirectory(framework);
        try
        {
            File.CreateSymbolicLink(Path.Combine(framework, "LiveKitWebRTC"), "Versions/Current/LiveKitWebRTC");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(CreatePlan(root, archive)));

            Assert.Contains("broken symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(archive));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_RejectsLinkOutsideFrameworkEvenWhenItStaysInsideArchive()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var root = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archives", "App.xcarchive");
        Directory.CreateDirectory(archive);
        File.WriteAllText(Path.Combine(archive, "Info.plist"), "archive");
        try
        {
            File.CreateSymbolicLink(Path.Combine(archive, "Info.current"), "Info.plist");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseArtifactService(_ => long.MaxValue).RemoveCurrentArtifacts(CreatePlan(root, archive)));

            Assert.Contains("outside a framework bundle", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(archive));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemoveCurrentArtifacts_UsesCaseSensitiveContainmentOnCaseSensitiveUnixVolume()
    {
        if (Path.DirectorySeparatorChar == '\\')
            return;

        var parent = Path.Combine(Path.GetTempPath(), "PowerForge.AppleCleanup", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "Project");
        var caseVariant = Path.Combine(parent, "project");
        Directory.CreateDirectory(root);
        try
        {
            if (FrameworkCompatibility.GetPathStringComparison(root) != StringComparison.Ordinal)
                return;
            Directory.CreateDirectory(caseVariant);
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

    private static PowerForgeAppleReleasePlan CreatePlan(string root, string archive)
        => new()
        {
            ProjectRoot = root,
            Apps = new[]
            {
                new PowerForgeAppleAppReleaseTargetPlan
                {
                    ArchivePath = archive,
                    ExportPath = Path.Combine(root, "exports", "App")
                }
            }
        };
}
