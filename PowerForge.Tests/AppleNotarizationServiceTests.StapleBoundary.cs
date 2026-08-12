namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Fact]
    public async Task NotarizeAsync_rejects_artifact_replaced_after_stapler_completion_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var package = Path.Combine(root.FullName, "Boundary.pkg");
            await File.WriteAllTextAsync(package, "approved-package");
            var checkpointed = false;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new PostStapleReplacementRunner()).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = package,
                    KeychainProfile = "powerforge-notary",
                    Assess = false,
                    StapledCheckpoint = _ => checkpointed = true
                }));

            Assert.Contains("changed after stapler completed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(checkpointed);
            Assert.Equal("approved-package", await File.ReadAllTextAsync(package));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class PostStapleReplacementRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var isSubmission = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool";
            var isStaple = request.Arguments.Count > 2 &&
                           request.Arguments[0] == "stapler" &&
                           request.Arguments[1] == "staple";
            if (isStaple)
                File.AppendAllText(request.Arguments[2], "-stapled");

            var result = new ProcessRunResult(
                0,
                isSubmission ? "{\"id\":\"submission-boundary\",\"status\":\"Accepted\"}" : "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                false);
            if (isStaple)
            {
                request.InvokeCompletionBoundary(result);
                File.WriteAllText(request.Arguments[2], "different-already-stapled-package");
            }
            return Task.FromResult(result);
        }
    }
}
