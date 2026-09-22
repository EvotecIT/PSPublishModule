namespace PowerForge.Tests;

public sealed class WingetSubmissionServiceTests
{
    [Fact]
    public void Plan_ManifestMode_BuildsSubmitCommandAndRedactsToken()
    {
        var root = CreateSandbox();
        try
        {
            var manifestPath = Path.Combine(root, "Evotec.Test.yaml");
            File.WriteAllText(manifestPath, "PackageIdentifier: Evotec.Test");
            var service = new WingetSubmissionService(new NullLogger(), new StubProcessRunner(_ => Success()));

            var plan = service.Plan(
                new PowerForgeReleaseWingetOptions
                {
                    Submit = true,
                    Submission = new PowerForgeReleaseWingetSubmissionOptions
                    {
                        Token = "secret-token",
                        PullRequestTitle = "Submit {PackageIdentifier} {PackageVersion}"
                    }
                },
                new[]
                {
                    new PowerForgeWingetManifestArtifact
                    {
                        PackageIdentifier = "Evotec.Test",
                        PackageVersion = "1.2.3",
                        ManifestPath = manifestPath,
                        InstallerUrls = new[] { "https://example.test/tool.zip" }
                    }
                },
                root,
                new PowerForgeReleaseRequest());

            var entry = Assert.Single(plan.Entries);
            Assert.Equal(new[] { "submit", manifestPath, "--prtitle", "Submit Evotec.Test 1.2.3", "--token", "secret-token", "--no-open" }, entry.Arguments);
            Assert.Equal(new[] { "submit", manifestPath, "--prtitle", "Submit Evotec.Test 1.2.3", "--token", "***", "--no-open" }, entry.RedactedArguments);
            Assert.Null(typeof(PowerForgeWingetSubmissionEntryPlan).GetProperty(nameof(PowerForgeWingetSubmissionEntryPlan.Arguments)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_UpdateMode_BuildsAutonomousSubmitCommand()
    {
        var root = CreateSandbox();
        try
        {
            var manifestPath = Path.Combine(root, "Evotec.Test.yaml");
            File.WriteAllText(manifestPath, "PackageIdentifier: Evotec.Test");
            var service = new WingetSubmissionService(new NullLogger(), new StubProcessRunner(_ => Success()));

            var plan = service.Plan(
                new PowerForgeReleaseWingetOptions
                {
                    Submit = true,
                    Submission = new PowerForgeReleaseWingetSubmissionOptions
                    {
                        Mode = PowerForgeWingetSubmissionMode.Update,
                        Token = "secret-token"
                    }
                },
                new[]
                {
                    new PowerForgeWingetManifestArtifact
                    {
                        PackageIdentifier = "Evotec.Test",
                        PackageVersion = "1.2.3",
                        ManifestPath = manifestPath,
                        InstallerUrls = new[] { "https://example.test/tool-x64.zip", "https://example.test/tool-arm64.zip" }
                    }
                },
                root,
                new PowerForgeReleaseRequest());

            var entry = Assert.Single(plan.Entries);
            Assert.Equal("update", entry.Arguments[0]);
            Assert.Contains("--urls", entry.Arguments);
            Assert.Contains("--version", entry.Arguments);
            Assert.Contains("--submit", entry.Arguments);
            Assert.DoesNotContain("secret-token", entry.RedactedArguments);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_Disabled_DoesNotResolveTokenSources()
    {
        var service = new WingetSubmissionService(new NullLogger(), new StubProcessRunner(_ => Success()));

        var plan = service.Plan(
            new PowerForgeReleaseWingetOptions
            {
                Submission = new PowerForgeReleaseWingetSubmissionOptions
                {
                    TokenFilePath = "missing-token.txt"
                }
            },
            Array.Empty<PowerForgeWingetManifestArtifact>(),
            Path.GetTempPath(),
            new PowerForgeReleaseRequest());

        Assert.False(plan.Enabled);
        Assert.False(plan.UsesToken);
        Assert.Empty(plan.Entries);
    }

    [Fact]
    public void Run_InteractiveAuthentication_DoesNotCaptureProcessStreams()
    {
        ProcessRunRequest? capturedRequest = null;
        var service = new WingetSubmissionService(
            new NullLogger(),
            new StubProcessRunner(request =>
            {
                capturedRequest = request;
                return Success();
            }));
        var plan = new PowerForgeWingetSubmissionPlan
        {
            Enabled = true,
            UsesInteractiveAuthentication = true,
            ToolPath = "wingetcreate",
            WorkingDirectory = Path.GetTempPath(),
            TimeoutSeconds = 60,
            Entries = new[]
            {
                new PowerForgeWingetSubmissionEntryPlan
                {
                    PackageIdentifier = "Evotec.Test",
                    PackageVersion = "1.2.3",
                    ManifestPath = "Evotec.Test.yaml",
                    Arguments = new[] { "submit", "Evotec.Test.yaml" },
                    RedactedArguments = new[] { "submit", "Evotec.Test.yaml" }
                }
            }
        };

        var result = service.Run(plan);

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedRequest);
        Assert.False(capturedRequest!.CaptureOutput);
        Assert.False(capturedRequest.CaptureError);
    }

    [Fact]
    public void Run_ExecutesPlannedCommandThroughProcessRunner()
    {
        ProcessRunRequest? capturedRequest = null;
        var service = new WingetSubmissionService(
            new NullLogger(),
            new StubProcessRunner(request =>
            {
                capturedRequest = request;
                return new ProcessRunResult(0,
                    "Pull request can be found here: https://github.com/microsoft/winget-pkgs/pull/120206?token=discard",
                    string.Empty, "wingetcreate", TimeSpan.FromMilliseconds(1), timedOut: false);
            }));
        var plan = new PowerForgeWingetSubmissionPlan
        {
            Enabled = true,
            ToolPath = "wingetcreate",
            WorkingDirectory = Path.GetTempPath(),
            TimeoutSeconds = 60,
            Entries = new[]
            {
                new PowerForgeWingetSubmissionEntryPlan
                {
                    PackageIdentifier = "Evotec.Test",
                    PackageVersion = "1.2.3",
                    ManifestPath = "Evotec.Test.yaml",
                    Arguments = new[] { "submit", "Evotec.Test.yaml" },
                    RedactedArguments = new[] { "submit", "Evotec.Test.yaml" }
                }
            }
        };

        var result = service.Run(plan);

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedRequest);
        Assert.Equal("wingetcreate", capturedRequest!.FileName);
        Assert.Equal(new[] { "submit", "Evotec.Test.yaml" }, capturedRequest.Arguments);
        Assert.Equal(60, capturedRequest.Timeout.TotalSeconds);
        Assert.Equal("https://github.com/microsoft/winget-pkgs/pull/120206", Assert.Single(result.Entries).PullRequestUrl);
    }

    [Fact]
    public void Run_CancellationAfterFirstCommand_PreservesAttemptAndSkipsNextPackage()
    {
        using var cancellation = new CancellationTokenSource();
        var commands = 0;
        var service = new WingetSubmissionService(
            new NullLogger(),
            new StubProcessRunner((_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                commands++;
                cancellation.Cancel();
                return Success();
            }));
        var plan = new PowerForgeWingetSubmissionPlan {
            Enabled = true,
            ToolPath = "wingetcreate",
            WorkingDirectory = Path.GetTempPath(),
            TimeoutSeconds = 60,
            Entries = [
                new PowerForgeWingetSubmissionEntryPlan {
                    PackageIdentifier = "Evotec.First", PackageVersion = "1.0.0", ManifestPath = "first.yaml"
                },
                new PowerForgeWingetSubmissionEntryPlan {
                    PackageIdentifier = "Evotec.Second", PackageVersion = "1.0.0", ManifestPath = "second.yaml"
                }
            ]
        };

        var result = service.Run(plan, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(1, commands);
        var attempted = Assert.Single(result.Entries);
        Assert.Equal("Evotec.First", attempted.PackageIdentifier);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(17, "Pull request can be found here: https://github.com/microsoft/winget-pkgs/pull/42", "https://github.com/microsoft/winget-pkgs/pull/42")]
    [InlineData(0, "https://evil.example/https://github.com/microsoft/winget-pkgs/pull/42", null)]
    public void Run_KeepsOnlyRecognizedUpstreamReviewLink(int exitCode, string output, string? expectedUrl)
    {
        var service = new WingetSubmissionService(new NullLogger(),
            new StubProcessRunner(_ => new ProcessRunResult(exitCode, output, string.Empty, "wingetcreate",
                TimeSpan.FromMilliseconds(1), timedOut: false)));
        var plan = new PowerForgeWingetSubmissionPlan {
            Enabled = true, ToolPath = "wingetcreate", WorkingDirectory = Path.GetTempPath(), TimeoutSeconds = 60,
            Entries = [new PowerForgeWingetSubmissionEntryPlan {
                PackageIdentifier = "Evotec.Test", PackageVersion = "1.2.3", ManifestPath = "Evotec.Test.yaml"
            }]
        };

        var result = service.Run(plan);

        Assert.Equal(exitCode == 0, result.Succeeded);
        Assert.Equal(expectedUrl, Assert.Single(result.Entries).PullRequestUrl);
    }

    private static ProcessRunResult Success()
        => new(0, "ok", string.Empty, "wingetcreate", TimeSpan.FromMilliseconds(1), timedOut: false);

    private static string CreateSandbox()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-winget-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, CancellationToken, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = (request, _) => execute(request);
        }

        public StubProcessRunner(Func<ProcessRunRequest, CancellationToken, ProcessRunResult> execute)
            => _execute = execute;

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request, cancellationToken));
    }
}
