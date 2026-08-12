namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Fact]
    public async Task NotarizeAsync_AtomicallyReplacesExistingRetainedAppSubmission()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "EasyControlX Agent.app"));
            var retainedPath = Path.Combine(root.FullName, "retained.notarization.zip");
            await File.WriteAllTextAsync(retainedPath, "previous accepted submission");
            var checkpointObservedPrevious = false;

            var result = await new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(
                new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    SubmissionPath = retainedPath,
                    KeychainProfile = "powerforge-notary",
                    AcceptedCheckpoint = _ =>
                        checkpointObservedPrevious = File.ReadAllText(retainedPath) == "previous accepted submission"
                });

            Assert.True(checkpointObservedPrevious);
            Assert.Equal(retainedPath, result.SubmissionPath);
            Assert.Equal(result.SubmissionSha256, AppleNotarizationService.ComputeFileSha256(retainedPath));
            Assert.Empty(Directory.GetFiles(root.FullName, ".retained.notarization.zip.*.tmp"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
