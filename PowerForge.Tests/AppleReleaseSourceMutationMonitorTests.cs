namespace PowerForge.Tests;

public sealed class AppleReleaseSourceMutationMonitorTests
{
    [Fact]
    public void ValidateNoChanges_keeps_monitoring_during_final_identity_validation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var monitor = new AppleReleaseSourceMutationMonitor(root);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                monitor.ValidateNoChanges(() =>
                {
                    var transientPath = Path.Combine(root, "unapproved-transient");
                    File.WriteAllText(transientPath, "unapproved bytes");
                    File.Delete(transientPath);
                }));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CaptureExpectedProducerOutput_arms_monitor_only_at_completion_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.SourceMonitorTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var artifact = Path.Combine(root.FullName, "Artifact.zip");
            using var monitor = new AppleReleaseSourceMutationMonitor(
                root.FullName,
                "producer output root",
                "test producer",
                "Discard the output.",
                enableImmediately: false);
            File.WriteAllText(artifact, "producer-output");

            var identity = monitor.CaptureExpectedProducerOutput(
                () => AppleNotarizationService.ComputeArtifactSha256(artifact),
                "test producer");

            Assert.Equal(AppleNotarizationService.ComputeArtifactSha256(artifact), identity);
            monitor.ValidateNoChanges();
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void CaptureExpectedProducerOutput_rejects_replacement_after_first_identity_is_bound()
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
            var firstCapture = true;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                monitor.CaptureExpectedProducerOutput(
                    () =>
                    {
                        var identity = AppleNotarizationService.ComputeArtifactSha256(artifact);
                        if (firstCapture)
                        {
                            firstCapture = false;
                            File.WriteAllText(artifact, "replacement-output");
                        }
                        return identity;
                    },
                    "test producer"));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

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

    [Fact]
    public void CaptureExpectedProducerOutput_rejects_write_and_restore_during_final_arm_transition()
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
                "Discard the output.",
                enableImmediately: false);
            var captures = 0;

            var exception = Assert.Throws<InvalidOperationException>(() =>
                monitor.CaptureExpectedProducerOutput(
                    () =>
                    {
                        captures++;
                        if (captures == 2)
                        {
                            File.WriteAllText(artifact, "replacement-output");
                            File.WriteAllText(artifact, "producer-output");
                            Thread.Sleep(250);
                        }
                        return AppleNotarizationService.ComputeArtifactSha256(artifact);
                    },
                    "test producer"));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bound", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
