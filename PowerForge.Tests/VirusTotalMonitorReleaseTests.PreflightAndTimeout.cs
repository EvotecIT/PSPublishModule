namespace PowerForge.Tests;

public sealed partial class VirusTotalMonitorReleaseTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("owner/project")]
    [InlineData("owner\\project")]
    public void ValidateConfiguration_InvalidExplicitProjectName_FailsBeforeRelease(string projectName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            VirusTotalReleaseArtifactSelector.ValidateConfiguration(new PowerForgeVirusTotalOptions
            {
                Enabled = true,
                ProjectName = projectName,
                ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
                ArtifactKinds = [VirusTotalArtifactKind.NuGetPackage],
                ReceiptPath = "receipt.json"
            }));

        Assert.Contains("path token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Publisher_ProviderTimeout_ReturnsCheckpointedFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "App.msi");
        await File.WriteAllTextAsync(artifactPath, "installer");
        var checkpoints = new List<VirusTotalMonitorPublishResult>();
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new ProviderTimeoutClient());
            var result = await publisher.PublishAsync(new VirusTotalMonitorPublishRequest
            {
                ApiKey = "secret",
                Artifacts = [Artifact(artifactPath, "/Example/1.0.0/MsiPackage/App.msi")],
                CheckpointAsync = (checkpoint, token) =>
                {
                    Assert.False(token.CanBeCanceled);
                    checkpoints.Add(checkpoint);
                    return Task.CompletedTask;
                }
            });

            Assert.False(result.Success);
            Assert.Contains("provider timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Same(result, Assert.Single(checkpoints));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Publisher_CallerCancellation_RemainsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var artifactPath = Path.Combine(root, "App.msi");
        await File.WriteAllTextAsync(artifactPath, "installer");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            var publisher = new VirusTotalMonitorPublisher((_, _) => new ProviderTimeoutClient());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(
                new VirusTotalMonitorPublishRequest
                {
                    ApiKey = "secret",
                    Artifacts = [Artifact(artifactPath, "/Example/1.0.0/MsiPackage/App.msi")]
                },
                cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ProviderTimeoutClient : IVirusTotalMonitorUploadClient
    {
        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
            => throw new TaskCanceledException("provider timeout");

        public void Dispose()
        {
        }
    }
}
