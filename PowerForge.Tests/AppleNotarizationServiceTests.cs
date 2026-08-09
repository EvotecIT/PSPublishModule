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
                SpctlExecutable = "spctl-test",
                AcceptedCheckpoint = checkpoint =>
                {
                    Assert.Equal("submission-1", checkpoint.SubmissionId);
                    Assert.Equal("Accepted", checkpoint.Status);
                    Assert.Equal(2, runner.Requests.Count);
                }
            });

            Assert.True(result.Succeeded);
            Assert.Equal("submission-1", result.SubmissionId);
            Assert.Equal("Accepted", result.Status);
            Assert.EndsWith(".notarization.zip", result.SubmissionPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(result.SubmissionPath));
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
    public async Task NotarizeAsync_SubmitsPrivateImmutableArtifactSnapshot()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "approved-pkg");
        try
        {
            var runner = new SnapshotObservingNotaryRunner(artifact);
            var result = await new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
            {
                ArtifactPath = artifact,
                KeychainProfile = "powerforge-notary",
                Staple = false,
                Assess = false
            });

            Assert.True(result.Succeeded);
            Assert.NotEqual(artifact, runner.SubmittedPath);
            Assert.Equal("approved-pkg", runner.SubmittedContents);
            Assert.Equal("approved-pkg", await File.ReadAllTextAsync(artifact));
        }
        finally
        {
            try { File.Delete(artifact); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_AcceptedCheckpointFailureReportsSubmissionBeforeStapling()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "pkg");
        try
        {
            var runner = new NotaryProcessRunner();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = artifact,
                    KeychainProfile = "powerforge-notary",
                    AcceptedCheckpoint = _ => throw new IOException("receipt storage unavailable")
                }));

            Assert.Contains("submission-1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Do not resubmit", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(runner.Requests);
            Assert.DoesNotContain(runner.Requests, request =>
                request.Arguments.Count > 1 && request.Arguments[0] == "stapler");
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
                    ExpectedArtifactSha256 = new string('0', 64),
                    Staple = false
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
    public async Task NotarizeAsync_ResumeRejectsChangedExecutableMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Mode.app"));
            var executableDirectory = Directory.CreateDirectory(Path.Combine(app.FullName, "Contents", "MacOS"));
            var executable = Path.Combine(executableDirectory.FullName, "Mode");
            await File.WriteAllTextAsync(executable, "binary");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var first = await new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(
                new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Staple = false,
                    Assess = false
                });

            File.SetUnixFileMode(
                executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(
                    new AppleNotarizationRequest
                    {
                        ArtifactPath = app.FullName,
                        AcceptedSubmissionId = first.SubmissionId,
                        ExpectedArtifactSha256 = first.ArtifactSha256,
                        Staple = false,
                        Assess = false
                    }));

            Assert.Contains("artifact changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_ResumeRejectsAddedEmptyBundleDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "EmptyDirectory.app"));
            var originalWriteTime = app.LastWriteTimeUtc;
            var first = await new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(
                new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Staple = false,
                    Assess = false
                });

            Directory.CreateDirectory(Path.Combine(app.FullName, "Contents", "Empty"));
            app.LastWriteTimeUtc = originalWriteTime;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(new NotaryProcessRunner()).NotarizeAsync(
                    new AppleNotarizationRequest
                    {
                        ArtifactPath = app.FullName,
                        AcceptedSubmissionId = first.SubmissionId,
                        ExpectedArtifactSha256 = first.ArtifactSha256,
                        Staple = false,
                        Assess = false
                    }));

            Assert.Contains("artifact changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ComputeArtifactSha256_IsStableAcrossTimestampOnlyCopyChanges()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.NotaryTests", Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Portable.app"));
            var contents = Directory.CreateDirectory(Path.Combine(app.FullName, "Contents"));
            var payload = Path.Combine(contents.FullName, "payload");
            File.WriteAllText(payload, "identical signed bytes");
            var expected = AppleNotarizationService.ComputeArtifactSha256(app.FullName);

            File.SetLastWriteTimeUtc(payload, DateTime.UtcNow.AddYears(-2));
            Directory.SetLastWriteTimeUtc(contents.FullName, DateTime.UtcNow.AddYears(-1));
            Directory.SetLastWriteTimeUtc(app.FullName, DateTime.UtcNow.AddMonths(-3));

            Assert.Equal(expected, AppleNotarizationService.ComputeArtifactSha256(app.FullName));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
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

    [Fact]
    public async Task NotarizeAsync_ResumeRejectsChangedArtifactEvenWhenStaplerCouldValidateIt()
    {
        var artifact = Path.GetTempFileName() + ".pkg";
        await File.WriteAllTextAsync(artifact, "pkg-before-stapling");
        try
        {
            var acceptedHash = AppleNotarizationService.ComputeArtifactSha256(artifact);
            await File.AppendAllTextAsync(artifact, "-ticket-stapled-before-crash");
            var runner = new MutatingStapleRunner(artifact, failAssessment: false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = artifact,
                    AcceptedSubmissionId = "accepted-before-crash",
                    ExpectedArtifactSha256 = acceptedHash
                }));

            Assert.Contains("cannot prove artifact identity", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
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
            if (request.FileName.Contains("ditto", StringComparison.OrdinalIgnoreCase) && request.Arguments.Count > 0)
                File.WriteAllText(request.Arguments[^1], "private notarization package");
            var output = request.Arguments.Count > 0 && request.Arguments[0] == "notarytool"
                ? JsonSerializer.Serialize(new { id = "submission-1", status = _status })
                : "ok";
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty, request.FileName, TimeSpan.FromMilliseconds(1), false));
        }
    }

    private sealed class SnapshotObservingNotaryRunner : IProcessRunner
    {
        private readonly string _originalArtifact;

        internal SnapshotObservingNotaryRunner(string originalArtifact)
        {
            _originalArtifact = originalArtifact;
        }

        internal string? SubmittedPath { get; private set; }

        internal string? SubmittedContents { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Arguments.Count > 2 && request.Arguments[0] == "notarytool")
            {
                SubmittedPath = request.Arguments[2];
                File.WriteAllText(_originalArtifact, "transient-pkg");
                SubmittedContents = File.ReadAllText(SubmittedPath);
                File.WriteAllText(_originalArtifact, "approved-pkg");
                return Task.FromResult(new ProcessRunResult(
                    0,
                    JsonSerializer.Serialize(new { id = "submission-private", status = "Accepted" }),
                    string.Empty,
                    request.FileName,
                    TimeSpan.FromMilliseconds(1),
                    false));
            }

            return Task.FromResult(new ProcessRunResult(
                0,
                "ok",
                string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                false));
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
