namespace PowerForge.Tests;

public sealed partial class VirusTotalMonitorReleaseTests
{
    [Fact]
    public async Task Publisher_FailureBeforeFirstResume_RetainsEveryPriorMonitorId()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstPath = Path.Combine(root, "First.msi");
        var secondPath = Path.Combine(root, "Second.msi");
        await File.WriteAllTextAsync(firstPath, "first");
        await File.WriteAllTextAsync(secondPath, "second");
        var firstDestination = "/Example/1.0.0/MsiPackage/First.msi";
        var secondDestination = "/Example/1.0.0/MsiPackage/Second.msi";
        var firstArtifact = Artifact(firstPath, firstDestination);
        firstArtifact.ExistingItemId = "existing-first";
        var secondArtifact = Artifact(secondPath, secondDestination);
        secondArtifact.ExistingItemId = "existing-second";
        var priorReceipts = new[]
        {
            ExistingReceipt(firstPath, firstDestination, "existing-first"),
            ExistingReceipt(secondPath, secondDestination, "existing-second")
        };
        var checkpoints = new List<VirusTotalMonitorPublishResult>();
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new SequencedClient(failOnCall: 1));

            var result = await publisher.PublishAsync(new VirusTotalMonitorPublishRequest
            {
                ApiKey = "secret",
                Artifacts = [firstArtifact, secondArtifact],
                ResumeReceipts = priorReceipts,
                CheckpointAsync = (checkpoint, _) =>
                {
                    checkpoints.Add(checkpoint);
                    return Task.CompletedTask;
                }
            });

            Assert.False(result.Success);
            Assert.Equal(2, result.Artifacts.Length);
            Assert.Contains(result.Artifacts, receipt => receipt.MonitorId == "existing-first");
            Assert.Contains(result.Artifacts, receipt => receipt.MonitorId == "existing-second");
            var checkpoint = Assert.Single(checkpoints);
            Assert.Equal(2, checkpoint.Artifacts.Length);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static VirusTotalMonitorArtifactReceipt ExistingReceipt(
        string sourcePath,
        string destinationPath,
        string monitorId)
        => new()
        {
            SourcePath = sourcePath,
            Kind = VirusTotalArtifactKind.MsiPackage,
            DestinationPath = destinationPath,
            MonitorId = monitorId,
            LocalSha256 = "LOCAL",
            RemoteSha256 = "REMOTE",
            VerificationStatus = VirusTotalMonitorVerificationStatus.Verified,
            UploadedAtUtc = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero)
        };
}
