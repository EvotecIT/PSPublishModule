using System.Text.Json;

namespace PowerForge.Tests;

public sealed class AppleNotarizationServiceTests
{
    [Fact]
    public async Task NotarizeAsync_PackagesSubmitsStaplesValidatesAndAssessesApp()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "EasyControlX Agent.app"));
            var runner = new NotaryProcessRunner();
            var result = await new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = app.FullName,
                KeychainProfile = "powerforge-notary",
                XcrunExecutable = "xcrun-test",
                DittoExecutable = "ditto-test",
                SpctlExecutable = "spctl-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("submission-1", result.SubmissionId);
            Assert.Equal("Accepted", result.Status);
            Assert.EndsWith(".notarization.zip", result.SubmissionPath, StringComparison.OrdinalIgnoreCase);
            Assert.Collection(
                runner.Requests,
                request => Assert.Equal("ditto-test", request.FileName),
                request =>
                {
                    Assert.Equal("xcrun-test", request.FileName);
                    Assert.Equal("notarytool", request.Arguments[0]);
                    Assert.Contains("--keychain-profile", request.Arguments);
                },
                request => Assert.Equal(new[] { "stapler", "staple", app.FullName }, request.Arguments),
                request => Assert.Equal(new[] { "stapler", "validate", app.FullName }, request.Arguments),
                request =>
                {
                    Assert.Equal("spctl-test", request.FileName);
                    Assert.Contains("execute", request.Arguments);
                });
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_InvalidSubmissionDoesNotStapleOrAssess()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "pkg");
        try
        {
            var runner = new NotaryProcessRunner(status: "Invalid");
            var result = await new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                KeychainProfile = "powerforge-notary"
            });

            Assert.False(result.Succeeded);
            Assert.Equal("Invalid", result.Status);
            Assert.Single(runner.Requests);
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_DiskImageUsesOpenAssessmentWithPrimarySignatureContext()
    {
        var artifact = Path.GetTempFileName() + ".dmg";
        await File.WriteAllTextAsync(artifact, "dmg");
        try
        {
            var runner = new NotaryProcessRunner();
            var result = await new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                KeychainProfile = "powerforge-notary"
            });

            Assert.True(result.Succeeded);
            Assert.Collection(
                runner.Requests,
                request => Assert.Equal("notarytool", request.Arguments[0]),
                request => Assert.Equal(new[] { "stapler", "staple", artifact }, request.Arguments),
                request => Assert.Equal(new[] { "stapler", "validate", artifact }, request.Arguments),
                request => Assert.Equal(
                    new[] { "--assess", "--type", "open", "--context", "context:primary-signature", "--verbose=4", artifact },
                    request.Arguments));
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_ResumeAcceptedSubmissionSkipsUploadAndAuthentication()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "pkg");
        try
        {
            var runner = new NotaryProcessRunner();
            var result = await new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                AcceptedSubmissionId = "submission-existing"
            });

            Assert.True(result.Succeeded);
            Assert.True(result.ResumedAcceptedSubmission);
            Assert.Equal("submission-existing", result.SubmissionId);
            Assert.DoesNotContain(runner.Requests, request => request.Arguments.Contains("notarytool"));
            Assert.Equal(3, runner.Requests.Count);
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_RejectsStandaloneZipBecauseItCannotBeStapled()
    {
        var artifact = Path.GetTempFileName() + ".zip";
        await File.WriteAllTextAsync(artifact, "zip");
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
                new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = artifact,
                    KeychainProfile = "powerforge-notary"
                }));

            Assert.Contains("cannot be stapled", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_ResumeRejectsChangedArtifactBytes()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "changed-pkg");
        try
        {
            var runner = new NotaryProcessRunner();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = artifact,
                    AcceptedSubmissionId = "submission-existing",
                    ExpectedArtifactSha256 = new string('0', 64)
                }));

            Assert.Contains("artifact changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_ResumeUsesPostStapleHashAndDoesNotStapleAgain()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "pkg");
        try
        {
            var firstRunner = new MutatingStapleRunner(artifact, failAssessment: true);
            var first = await new AppleNotarizationService(firstRunner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                KeychainProfile = "powerforge-notary"
            });

            Assert.False(first.Succeeded);
            Assert.True(first.Staple?.Succeeded);
            Assert.False(first.Assessment?.Succeeded);

            var resumedRunner = new MutatingStapleRunner(artifact, failAssessment: false);
            var resumed = await new AppleNotarizationService(resumedRunner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                AcceptedSubmissionId = first.SubmissionId,
                ExpectedArtifactSha256 = first.ArtifactSha256,
                StaplingCompleted = true
            });

            Assert.True(resumed.Succeeded);
            Assert.Equal(first.ArtifactSha256, resumed.ArtifactSha256);
            Assert.DoesNotContain(resumedRunner.Requests, request =>
                request.Arguments.Count > 1 &&
                request.Arguments[0] == "stapler" &&
                request.Arguments[1] == "staple");
            Assert.Contains(resumedRunner.Requests, request =>
                request.Arguments.Count > 1 &&
                request.Arguments[0] == "stapler" &&
                request.Arguments[1] == "validate");
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    private sealed class NotaryProcessRunner : IProcessRunner
    {
        private readonly string _status;

        internal NotaryProcessRunner(string status = "Accepted")
        {
            _status = status;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var output = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool"
                ? JsonSerializer.Serialize(new { id = "submission-1", status = _status })
                : "ok";
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty, request.FileName, TimeSpan.FromMilliseconds(1), false));
        }
    }

    private sealed class MutatingStapleRunner : IProcessRunner
    {
        private readonly string _artifact;
        private readonly bool _failAssessment;

        internal MutatingStapleRunner(string artifact, bool failAssessment)
        {
            _artifact = artifact;
            _failAssessment = failAssessment;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.Count > 1 &&
                request.Arguments[0] == "stapler" &&
                request.Arguments[1] == "staple")
            {
                File.AppendAllText(_artifact, "-stapled");
            }

            var notarySubmission = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool";
            var assessmentFailure = _failAssessment && request.FileName.Contains("spctl", StringComparison.OrdinalIgnoreCase);
            var result = new ProcessRunResult(
                assessmentFailure ? 1 : 0,
                notarySubmission ? JsonSerializer.Serialize(new { id = "submission-1", status = "Accepted" }) : "ok",
                assessmentFailure ? "rejected" : string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                false);
            return Task.FromResult(result);
        }
    }
}
