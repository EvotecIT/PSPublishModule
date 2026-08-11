namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Fact]
    public void FileSnapshot_rejects_mutation_before_submission_monitor_takes_ownership()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var package = Path.Combine(root.FullName, "Race.pkg");
            File.WriteAllText(package, "approved-package");
            var expected = AppleNotarizationService.ComputeArtifactSha256(package);
            using var snapshot = AppleNotarizationInputSnapshot.Create(package, expected);

            File.WriteAllText(snapshot.ArtifactPath, "attacker-package");

            var exception = Assert.Throws<InvalidOperationException>(() => snapshot.CompleteSubmissionCapture(expected));
            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_RejectsTransientAppMutationWhileDittoCreatesSubmissionZip()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "PackagingRace.app"));
            await File.WriteAllTextAsync(Path.Combine(app.FullName, "payload"), "approved-app");
            var checkpointed = false;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new MutatingDittoInputRunner()).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Staple = false,
                    Assess = false,
                    AcceptedCheckpoint = _ => checkpointed = true
                }));

            Assert.Contains("changed while ditto", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(checkpointed);
            Assert.Equal("approved-app", await File.ReadAllTextAsync(Path.Combine(app.FullName, "payload")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_BindsDittoZipAtProcessCompletionBoundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "DittoBoundary.app"));
            await File.WriteAllTextAsync(Path.Combine(app.FullName, "payload"), "approved-app");
            var checkpointed = false;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new MutatingDittoOutputAfterCompletionRunner()).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Staple = false,
                    Assess = false,
                    AcceptedCheckpoint = _ => checkpointed = true
                }));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(checkpointed);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationRollback_preserves_concurrently_replaced_notarization_artifact(bool directoryArtifact)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, directoryArtifact ? "Published.app" : "Published.pkg");
            var backup = Path.Combine(root.FullName, directoryArtifact ? "Previous.app" : "Previous.pkg");
            var quarantine = Path.Combine(root.FullName, directoryArtifact ? "Failed.app" : "Failed.pkg");
            if (directoryArtifact)
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "payload"), "published");
                Directory.CreateDirectory(backup);
                File.WriteAllText(Path.Combine(backup, "payload"), "previous");
            }
            else
            {
                File.WriteAllText(destination, "published");
                File.WriteAllText(backup, "previous");
            }
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(destination);
            if (directoryArtifact)
                File.WriteAllText(Path.Combine(destination, "payload"), "concurrent replacement");
            else
                File.WriteAllText(destination, "concurrent replacement");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleNotarizationInputSnapshot.RollbackPublication(
                    destination,
                    backup,
                    quarantine,
                    publishedSha256,
                    published: true,
                    movedExisting: true));

            Assert.Contains("replacement bytes", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(directoryArtifact ? Directory.Exists(destination) : File.Exists(destination));
            Assert.True(directoryArtifact ? Directory.Exists(backup) : File.Exists(backup));
            var destinationPayload = directoryArtifact
                ? File.ReadAllText(Path.Combine(destination, "payload"))
                : File.ReadAllText(destination);
            Assert.Equal("concurrent replacement", destinationPayload);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationRollback_removes_owned_bytes_and_restores_previous_notarization_artifact(bool directoryArtifact)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, directoryArtifact ? "Published.app" : "Published.pkg");
            var backup = Path.Combine(root.FullName, directoryArtifact ? "Previous.app" : "Previous.pkg");
            var quarantine = Path.Combine(root.FullName, directoryArtifact ? "Failed.app" : "Failed.pkg");
            if (directoryArtifact)
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Combine(destination, "payload"), "published");
                Directory.CreateDirectory(backup);
                File.WriteAllText(Path.Combine(backup, "payload"), "previous");
            }
            else
            {
                File.WriteAllText(destination, "published");
                File.WriteAllText(backup, "previous");
            }
            var publishedSha256 = AppleNotarizationService.ComputeArtifactSha256(destination);

            AppleNotarizationInputSnapshot.RollbackPublication(
                destination,
                backup,
                quarantine,
                publishedSha256,
                published: true,
                movedExisting: true);

            var destinationPayload = directoryArtifact
                ? File.ReadAllText(Path.Combine(destination, "payload"))
                : File.ReadAllText(destination);
            Assert.Equal("previous", destinationPayload);
            Assert.False(directoryArtifact ? Directory.Exists(backup) : File.Exists(backup));
            Assert.False(directoryArtifact ? Directory.Exists(quarantine) : File.Exists(quarantine));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationRollback_preserves_linked_notarization_replacement(bool directoryArtifact)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var destination = Path.Combine(root.FullName, directoryArtifact ? "Published.app" : "Published.pkg");
            var backup = Path.Combine(root.FullName, directoryArtifact ? "Previous.app" : "Previous.pkg");
            var quarantine = Path.Combine(root.FullName, directoryArtifact ? "Failed.app" : "Failed.pkg");
            var external = Path.Combine(root.FullName, directoryArtifact ? "External.app" : "External.pkg");
            if (directoryArtifact)
            {
                Directory.CreateDirectory(external);
                File.WriteAllText(Path.Combine(external, "payload"), "external replacement");
                Directory.CreateDirectory(backup);
                File.WriteAllText(Path.Combine(backup, "payload"), "previous");
                Directory.CreateSymbolicLink(destination, external);
            }
            else
            {
                File.WriteAllText(external, "external replacement");
                File.WriteAllText(backup, "previous");
                File.CreateSymbolicLink(destination, external);
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                AppleNotarizationInputSnapshot.RollbackPublication(
                    destination,
                    backup,
                    quarantine,
                    new string('a', 64),
                    published: true,
                    movedExisting: true));

            Assert.Contains("linked replacement", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0);
            Assert.True(directoryArtifact ? Directory.Exists(backup) : File.Exists(backup));
            var externalPayload = directoryArtifact
                ? File.ReadAllText(Path.Combine(external, "payload"))
                : File.ReadAllText(external);
            Assert.Equal("external replacement", externalPayload);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class MutatingDittoInputRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (request.FileName.Contains("ditto", StringComparison.OrdinalIgnoreCase))
            {
                var privateApp = request.Arguments[^2];
                var payload = Path.Combine(privateApp, "payload");
                File.WriteAllText(payload, "attacker-during-ditto");
                File.WriteAllText(request.Arguments[^1], "zip-created-from-mutated-app");
                File.WriteAllText(payload, "approved-app");
            }

            return Task.FromResult(new ProcessRunResult(
                0,
                "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                false));
        }
    }

    private sealed class MutatingDittoOutputAfterCompletionRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var result = new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, false);
            if (request.FileName.Contains("ditto", StringComparison.OrdinalIgnoreCase))
            {
                var zip = request.Arguments[^1];
                File.WriteAllText(zip, "approved-zip");
                request.InvokeCompletionBoundary(result);
                File.WriteAllText(zip, "replacement-after-process-completion");
            }
            return Task.FromResult(result);
        }
    }
}
