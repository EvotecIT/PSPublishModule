namespace PowerForge.Tests;

public sealed class AppleReleaseSourceMutationMonitorTests
{
    [Fact]
    public async Task CaptureExpectedProducerOutput_rejects_replacement_during_post_exit_drain()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.SourceMonitorTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var artifact = Path.Combine(root.FullName, "Artifact.zip");
            File.WriteAllText(artifact, "producer-output");
            using var monitor = new AppleReleaseSourceMutationMonitor(
                root.FullName,
                "producer output root",
                "test producer",
                "Discard the output.");
            var replacement = Task.Run(async () =>
            {
                await Task.Delay(50);
                File.WriteAllText(artifact, "replacement-output");
            });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                monitor.CaptureExpectedProducerOutput(
                    () => AppleNotarizationService.ComputeArtifactSha256(artifact),
                    "test producer"));
            await replacement;

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
