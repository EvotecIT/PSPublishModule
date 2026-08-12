namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Fact]
    public async Task NotarizeAsync_rejects_restored_assessment_bytes_changed_through_external_hard_link()
    {
        var package = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(package, "approved-package");
        using var runner = new RestoringAssessmentHardLinkRunner();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = package,
                    KeychainProfile = "powerforge-notary",
                    Staple = false,
                    Assess = true
                }));

            Assert.Contains("Gatekeeper assessment artifact changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("approved-package", await File.ReadAllTextAsync(package));
        }
        finally
        {
            try { File.Delete(package); } catch { }
        }
    }

    private sealed class RestoringAssessmentHardLinkRunner : IProcessRunner, IDisposable
    {
        private readonly string _aliasRoot = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            $"assessment-alias-{Guid.NewGuid():N}");

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var isSubmission = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool";
            var isAssessment = request.FileName.Contains("spctl", StringComparison.OrdinalIgnoreCase);
            if (isAssessment)
            {
                Directory.CreateDirectory(_aliasRoot);
                var artifactPath = request.Arguments[^1];
                var approvedBytes = File.ReadAllBytes(artifactPath);
                var alias = Path.Combine(_aliasRoot, "assessment-alias");
                TestFileLink.CreateHardLink(alias, artifactPath);
                File.WriteAllText(alias, "transient-assessment-bytes");
                File.WriteAllBytes(alias, approvedBytes);
                File.Delete(alias);
            }

            var result = new ProcessRunResult(
                0,
                isSubmission ? "{\"id\":\"assessment-submission\",\"status\":\"Accepted\"}" : "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                false);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }

        public void Dispose()
        {
            try { Directory.Delete(_aliasRoot, recursive: true); } catch { }
        }
    }
}
