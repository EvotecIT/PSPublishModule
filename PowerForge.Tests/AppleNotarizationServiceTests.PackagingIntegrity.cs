namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
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
}
