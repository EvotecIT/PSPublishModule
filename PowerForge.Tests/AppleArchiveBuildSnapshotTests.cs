namespace PowerForge.Tests;

public sealed class AppleArchiveBuildSnapshotTests
{
    [Fact]
    public void DirectExport_publish_rejects_artifact_replaced_after_xcodebuild_identity_was_observed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, "export");
            using var snapshot = AppleDirectExportSnapshot.Create();
            var artifact = Directory.CreateDirectory(Path.Combine(snapshot.ExportPath, "App.app"));
            var payload = Path.Combine(artifact.FullName, "payload");
            File.WriteAllText(payload, "approved export");
            var expected = AppleNotarizationService.ComputeArtifactSha256(artifact.FullName);
            snapshot.BindProducedArtifact(artifact.FullName, expected);
            File.WriteAllText(payload, "replacement export");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.Publish(destination));

            Assert.Contains("changed after xcodebuild completed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Publish_rejects_archive_replaced_after_xcodebuild_identity_was_observed()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, "App.xcarchive");
            using var snapshot = AppleArchiveBuildSnapshot.Create(destination);
            var archive = Directory.CreateDirectory(snapshot.ArchivePath);
            var payload = Path.Combine(archive.FullName, "payload");
            File.WriteAllText(payload, "approved archive");
            var expected = AppleNotarizationService.ComputeArtifactSha256(snapshot.ArchivePath);
            File.WriteAllText(payload, "replacement archive");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.Publish(destination, expected));

            Assert.Contains("changed after xcodebuild completed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(destination));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void RestoreDirectoryBackup_retains_previous_artifact_when_destination_was_recreated()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            File.WriteAllText(Path.Combine(destination.FullName, "payload"), "concurrent artifact");
            var backup = Directory.CreateDirectory(Path.Combine(root.FullName, ".App.xcarchive.powerforge-backup-test"));
            File.WriteAllText(Path.Combine(backup.FullName, "payload"), "previous artifact");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleArtifactCopy.RestoreDirectoryBackup(destination.FullName, backup.FullName));

            Assert.Contains("retained", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent artifact", File.ReadAllText(Path.Combine(destination.FullName, "payload")));
            Assert.Equal("previous artifact", File.ReadAllText(Path.Combine(backup.FullName, "payload")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
