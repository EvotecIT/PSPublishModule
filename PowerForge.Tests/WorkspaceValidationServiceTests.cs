using PowerForge;

namespace PowerForge.Tests;

public sealed class WorkspaceValidationServiceTests
{
    [Fact]
    public void Plan_ProfileAndFeatureOverrides_FilterSteps()
    {
        var root = CreateTempRoot();
        try
        {
            var spec = new WorkspaceValidationSpec
            {
                ProjectRoot = root,
                DefaultFeatures = new[] { "tests", "harness", "public-tools" },
                Profiles = new[]
                {
                    new WorkspaceValidationProfile { Name = "oss" },
                    new WorkspaceValidationProfile { Name = "full-private", Features = new[] { "private-tools" } }
                },
                Steps = new[]
                {
                    new WorkspaceValidationStep
                    {
                        Id = "ci-build",
                        Name = "CI build",
                        Arguments = new[] { "build", "Repo.slnf", "-c", "{configuration}" }
                    },
                    new WorkspaceValidationStep
                    {
                        Id = "ci-test",
                        Name = "CI tests",
                        RequiredFeatures = new[] { "tests" },
                        Arguments = new[] { "test", "Repo.slnf", "-c", "{configuration}", "--no-build" }
                    },
                    new WorkspaceValidationStep
                    {
                        Id = "private-build",
                        Name = "Private build",
                        RequiredFeatures = new[] { "private-tools" },
                        Arguments = new[] { "build", "Private.csproj", "-c", "{configuration}" }
                    },
                    new WorkspaceValidationStep
                    {
                        Id = "chat-build",
                        Name = "Chat build",
                        RequiredFeatures = new[] { "chat" },
                        Arguments = new[] { "build", "Chat.sln", "-c", "{configuration}" }
                    }
                }
            };

            var service = new WorkspaceValidationService();
            var plan = service.Plan(spec, Path.Combine(root, "workspace.validation.json"), new WorkspaceValidationRequest
            {
                ProfileName = "full-private",
                Configuration = "Debug",
                EnabledFeatures = new[] { "chat" },
                DisabledFeatures = new[] { "tests" }
            });

            Assert.Equal("full-private", plan.ProfileName);
            Assert.Contains("private-tools", plan.ActiveFeatures);
            Assert.Contains("chat", plan.ActiveFeatures);
            Assert.DoesNotContain("tests", plan.ActiveFeatures);
            Assert.Equal(3, plan.Steps.Length);
            Assert.Contains(plan.Steps, s => s.Id == "ci-build");
            Assert.DoesNotContain(plan.Steps, s => s.Id.StartsWith("ci-test", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(plan.Steps, s => s.Id == "private-build");
            Assert.Contains(plan.Steps, s => s.Id == "chat-build");
            Assert.Contains(plan.Steps.First(s => s.Id == "ci-build").Arguments, a => a == "Debug");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task RunAsync_MissingOptionalRequiredPath_SkipsStep()
    {
        var root = CreateTempRoot();
        try
        {
            var spec = new WorkspaceValidationSpec
            {
                ProjectRoot = root,
                Steps = new[]
                {
                    new WorkspaceValidationStep
                    {
                        Id = "ci-build",
                        Name = "CI build",
                        Arguments = new[] { "build", "Repo.slnf", "-c", "{configuration}" }
                    },
                    new WorkspaceValidationStep
                    {
                        Id = "harness",
                        Name = "Harness ({framework})",
                        Frameworks = new[] { "net8.0", "net10.0" },
                        RequiredPath = "bin/{configuration}/{framework}/Tests.dll",
                        ContinueOnMissingRequiredPath = true,
                        Arguments = new[] { "bin/{configuration}/{framework}/Tests.dll" }
                    }
                }
            };

            var requests = new List<ProcessRunRequest>();
            var runner = new StubProcessRunner(request =>
            {
                requests.Add(request);
                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });

            var service = new WorkspaceValidationService(runner);
            var result = await service.RunAsync(spec, Path.Combine(root, "workspace.validation.json"), new WorkspaceValidationRequest
            {
                Configuration = "Release"
            });

            Assert.True(result.Succeeded);
            Assert.Single(requests);
            Assert.Equal("dotnet", requests[0].FileName);
            Assert.Equal(3, result.Steps.Length);
            Assert.Equal(2, result.Steps.Count(s => s.Skipped));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

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
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}
