namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Theory]
    [InlineData("{}", null, null, 0, false)]
    [InlineData("{\"id\":\"pending-submission\",\"status\":\"In Progress\"}", "pending-submission", "In Progress", 0, false)]
    [InlineData("", null, null, 1, false)]
    [InlineData("", null, null, -1, true)]
    public async Task NotarizeAsync_checkpoints_every_attempt_without_terminal_submission_evidence(
        string response,
        string? expectedId,
        string? expectedStatus,
        int exitCode,
        bool timedOut)
    {
        var package = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(package, "approved-package");
        try
        {
            AppleNotarizationAmbiguousCheckpoint? checkpoint = null;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new IncompleteSubmissionEvidenceRunner(response, exitCode, timedOut)).NotarizeAsync(
                    new AppleNotarizationRequest
                    {
                        ArtifactPath = package,
                        KeychainProfile = "powerforge-notary",
                        Staple = false,
                        Assess = false,
                        AmbiguousCheckpoint = ambiguous => checkpoint = ambiguous
                    }));

            Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("do not resubmit", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(checkpoint);
            Assert.Equal(expectedId, checkpoint.SubmissionId);
            Assert.Equal(expectedStatus, checkpoint.Status);
            Assert.Equal(64, checkpoint.SubmissionSha256.Length);
        }
        finally
        {
            try { File.Delete(package); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_checkpoints_ambiguous_submission_when_runner_throws()
    {
        var package = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(package, "approved-package");
        try
        {
            AppleNotarizationAmbiguousCheckpoint? checkpoint = null;
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new ThrowingSubmissionRunner()).NotarizeAsync(
                    new AppleNotarizationRequest
                    {
                        ArtifactPath = package,
                        KeychainProfile = "powerforge-notary",
                        Staple = false,
                        Assess = false,
                        AmbiguousCheckpoint = ambiguous => checkpoint = ambiguous
                    }));

            Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.IsType<IOException>(exception.InnerException);
            Assert.NotNull(checkpoint);
            Assert.Equal(64, checkpoint.SubmissionSha256.Length);
        }
        finally
        {
            try { File.Delete(package); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_rejects_private_app_root_replaced_after_acceptance()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Accepted.app"));
            await File.WriteAllTextAsync(Path.Combine(app.FullName, "payload"), "approved-app");
            var runner = new AcceptedArtifactMutationRunner();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Assess = false,
                    AcceptedCheckpoint = _ =>
                    {
                        var privateApp = runner.PrivateArtifactPath!;
                        Directory.Move(privateApp, privateApp + ".replaced");
                        Directory.CreateDirectory(privateApp);
                        File.WriteAllText(Path.Combine(privateApp, "payload"), "replacement-app");
                    }
                }));

            Assert.Contains("exact submitted file changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not resubmit", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(runner.Commands, command => command.StartsWith("stapler staple", StringComparison.Ordinal));
            Assert.Equal("approved-app", await File.ReadAllTextAsync(Path.Combine(app.FullName, "payload")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_rejects_private_artifact_changed_after_acceptance_before_stapling()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var package = Path.Combine(root.FullName, "Accepted.pkg");
            await File.WriteAllTextAsync(package, "approved-package");
            var runner = new AcceptedArtifactMutationRunner();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = package,
                    KeychainProfile = "powerforge-notary",
                    Assess = false,
                    AcceptedCheckpoint = _ => File.WriteAllText(
                        runner.PrivateArtifactPath!,
                        "replacement-after-acceptance")
                }));

            Assert.Contains("exact submitted file changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Do not resubmit", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(runner.Commands, command => command.StartsWith("stapler staple", StringComparison.Ordinal));
            Assert.Equal("approved-package", await File.ReadAllTextAsync(package));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class AcceptedArtifactMutationRunner : IProcessRunner
    {
        internal List<string> Commands { get; } = [];
        internal string? PrivateArtifactPath { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Commands.Add(string.Join(" ", request.Arguments));
            if (request.Arguments.Count == 5 && request.Arguments[0] == "-c")
                File.WriteAllText(request.Arguments[4], "exact-private-app-zip");
            var isSubmission = request.Arguments.Count > 2 && request.Arguments[0] == "notarytool";
            if (isSubmission)
                PrivateArtifactPath = Directory.EnumerateDirectories(request.WorkingDirectory, "*.app").FirstOrDefault() ??
                                      request.Arguments[2];
            var result = new ProcessRunResult(
                0,
                isSubmission ? "{\"id\":\"accepted-boundary\",\"status\":\"Accepted\"}" : "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                false);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }

    private sealed class IncompleteSubmissionEvidenceRunner : IProcessRunner
    {
        private readonly string _response;
        private readonly int _exitCode;
        private readonly bool _timedOut;

        internal IncompleteSubmissionEvidenceRunner(string response, int exitCode, bool timedOut)
        {
            _response = response;
            _exitCode = exitCode;
            _timedOut = timedOut;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var isSubmission = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool";
            var result = new ProcessRunResult(
                _exitCode,
                isSubmission ? _response : "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                _timedOut);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingSubmissionRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => throw new IOException("notarytool response channel closed after submission started");
    }
}
