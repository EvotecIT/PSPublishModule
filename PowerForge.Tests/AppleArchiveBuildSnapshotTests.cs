namespace PowerForge.Tests;

public sealed class AppleArchiveBuildSnapshotTests
{
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
}
