namespace PowerForge.Tests;

public sealed class VirusTotalMonitorReleaseSettlementTests
{
    [Fact]
    public void SelectArtifacts_LegacySymbolNuGetPackage_IsNeverEligible()
    {
        var selected = VirusTotalReleaseArtifactSelector.Select(
            new[]
            {
                Entry("Example.1.0.0.nupkg"),
                Entry("Example.1.0.0.symbols.nupkg"),
                Entry("Example.1.0.0.snupkg")
            },
            new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ArtifactKinds = [VirusTotalArtifactKind.NuGetPackage]
            },
            "Example",
            "1.0.0");

        var artifact = Assert.Single(selected);
        Assert.EndsWith("Example.1.0.0.nupkg", artifact.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publisher_PendingRequestedVerification_ReturnsFailedCheckpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "Example.msi");
        await File.WriteAllTextAsync(artifactPath, "signed installer");
        var checkpoints = new List<VirusTotalMonitorPublishResult>();
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new VerificationClient(
                VirusTotalMonitorVerificationStatus.Pending));

            var result = await publisher.PublishAsync(new VirusTotalMonitorPublishRequest
            {
                ApiKey = "secret",
                VerifySha256 = true,
                Artifacts = [Artifact(artifactPath)],
                CheckpointAsync = (checkpoint, _) =>
                {
                    checkpoints.Add(checkpoint);
                    return Task.CompletedTask;
                }
            });

            Assert.False(result.Success);
            Assert.Contains("did not complete", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(VirusTotalMonitorVerificationStatus.Pending, Assert.Single(result.Artifacts).VerificationStatus);
            Assert.Contains(checkpoints, checkpoint =>
                !checkpoint.Success &&
                checkpoint.Artifacts.Length == 1 &&
                checkpoint.ErrorMessage is not null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Publisher_DisabledVerification_AllowsNotRequestedReceipt()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "Example.msi");
        await File.WriteAllTextAsync(artifactPath, "signed installer");
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new VerificationClient(
                VirusTotalMonitorVerificationStatus.NotRequested));

            var result = await publisher.PublishAsync(new VirusTotalMonitorPublishRequest
            {
                ApiKey = "secret",
                VerifySha256 = false,
                Artifacts = [Artifact(artifactPath)]
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(VirusTotalMonitorVerificationStatus.NotRequested, Assert.Single(result.Artifacts).VerificationStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PowerForgeReleaseAssetEntry Entry(string fileName)
        => new()
        {
            Path = Path.Combine(Path.GetTempPath(), fileName),
            Category = PowerForgeReleaseAssetCategory.Package,
            RelativeStagePath = fileName,
            Version = "1.0.0",
            IsFinalPackageOutput = true
        };

    private static VirusTotalMonitorArtifact Artifact(string sourcePath)
        => new()
        {
            SourcePath = sourcePath,
            Kind = VirusTotalArtifactKind.MsiPackage,
            DestinationPath = "/Example/1.0.0/MsiPackage/Example.msi"
        };

    private sealed class VerificationClient : IVirusTotalMonitorUploadClient
    {
        private readonly VirusTotalMonitorVerificationStatus _status;

        public VerificationClient(VirusTotalMonitorVerificationStatus status)
        {
            _status = status;
        }

        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new VirusTotalMonitorUploadResponse
            {
                MonitorId = "monitor-id",
                RemotePath = artifact.DestinationPath,
                LocalSha256 = "LOCAL",
                RemoteSha256 = _status == VirusTotalMonitorVerificationStatus.Verified ? "LOCAL" : null,
                VerificationStatus = _status
            });

        public void Dispose()
        {
        }
    }
}
