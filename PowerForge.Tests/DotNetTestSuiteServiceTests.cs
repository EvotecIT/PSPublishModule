namespace PowerForge.Tests;

public sealed class DotNetTestSuiteServiceTests
{
    [Fact]
    public void Test_plan_builds_prerequisites_and_both_test_projects_with_distinct_reports()
    {
        var profile = new DotNetTestProfile
        {
            Name = "test", TimeoutSeconds = 37,
            BuildProjects = new[] { new DotNetTestBuildProject { Path = "Module Host/Host.csproj", Frameworks = new[] { "net8.0", "net10.0" } } },
            TestProjects = new[] { "Core.Tests/Core.Tests.csproj", "Extensions.Tests/Extensions.Tests.csproj" },
            BeforeTests = new[] { new WorkspaceValidationStep { Kind = WorkspaceValidationStepKind.Command,
                Executable = "python3", Arguments = new[] { "-B", "probe.py" } } }
        };
        var spec = Spec(profile);
        var plan = new DotNetTestSuiteService().Plan(spec, Config, "test", "net472", null, "results");
        var prepared = new WorkspaceValidationService().Plan(plan, Config, new WorkspaceValidationRequest { ProfileName = "test" });
        Assert.Equal(7, prepared.Steps.Length);
        Assert.Contains("net8.0", prepared.Steps[0].Arguments);
        Assert.Contains("net10.0", prepared.Steps[1].Arguments);
        Assert.All(prepared.Steps.Where(step => step.Id.StartsWith("test-build")), step => Assert.Contains("net472", step.Arguments));
        Assert.Equal("python3", prepared.Steps[4].Executable);
        Assert.Contains("trx;LogFilePrefix=tests-0", prepared.Steps[5].Arguments);
        Assert.Contains("trx;LogFilePrefix=tests-1", prepared.Steps[6].Arguments);
        Assert.All(prepared.Steps, step => Assert.Equal(37, step.TimeoutSeconds));
        Assert.Contains(prepared.Steps[0].Arguments, value => value.EndsWith(Path.Combine("Module Host", "Host.csproj")));
    }

    [Fact]
    public void Native_plan_publishes_with_AOT_then_runs_each_environment_overlay()
    {
        var profile = new DotNetTestProfile
        {
            Name = "native", EnvironmentVariables = new() { ["TERM"] = "xterm-256color", ["LANG"] = "C.UTF-8" },
            NativeProjects = new[] { new DotNetTestNativeProject { Path = "Smoke.csproj", Executable = "Smoke",
                RunEnvironments = new[] { new Dictionary<string, string?>(), new() { ["LANG"] = "C" } } } }
        };
        var plan = new DotNetTestSuiteService().Plan(Spec(profile), Config, "native", "net10.0", "linux-x64", "results");
        Assert.Equal(3, plan.Steps.Length);
        Assert.Contains("-p:PublishAot=true", plan.Steps[0].Arguments);
        Assert.Contains("-warnaserror", plan.Steps[0].Arguments);
        Assert.Contains("linux-x64", plan.Steps[0].Arguments);
        Assert.Equal("C.UTF-8", plan.Steps[1].EnvironmentVariables["LANG"]);
        Assert.Equal("C", plan.Steps[2].EnvironmentVariables["LANG"]);
        Assert.Equal("xterm-256color", plan.Steps[2].EnvironmentVariables["TERM"]);
    }

    [Fact]
    public async Task Failing_test_keeps_a_failure_summary_and_stops_later_probes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-dotnet-test-" + Guid.NewGuid().ToString("N"));
        var calls = new List<ProcessRunRequest>();
        var runner = new Runner(request =>
        {
            calls.Add(request);
            return new ProcessRunResult(request.Arguments[0] == "test" ? 1 : 0,
                "", "", request.FileName, TimeSpan.Zero, false);
        });
        try
        {
            var spec = Spec(new DotNetTestProfile { TestProjects = new[] { "Tests.csproj" },
                AfterTests = new[] { new WorkspaceValidationStep { Executable = "should-not-run", Kind = WorkspaceValidationStepKind.Command } } });
            var result = await new DotNetTestSuiteService(runner).RunAsync(spec, Config, "test", "net8.0", null, root);
            Assert.False(result.Succeeded);
            Assert.Equal(2, calls.Count);
            Assert.Equal(TimeSpan.FromSeconds(1200), calls[0].Timeout);
            Assert.Contains("FAIL `test-0` (exit 1)", File.ReadAllText(Path.Combine(root, "summary.md")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Empty_profiles_and_native_without_a_runtime_fail_before_execution()
    {
        var service = new DotNetTestSuiteService();
        Assert.Throws<ArgumentException>(() => service.Plan(Spec(new DotNetTestProfile()), Config, "test", null, null, "results"));
        Assert.Throws<ArgumentException>(() => service.Plan(Spec(new DotNetTestProfile { NativeProjects = new[] {
            new DotNetTestNativeProject { Path = "Smoke.csproj", Executable = "Smoke" } } }), Config, "test", "net10.0", null, "results"));
    }

    [Fact]
    public async Task Probe_tokens_and_missing_prerequisite_checks_survive_the_shared_plan()
    {
        var calls = 0;
        var profile = new DotNetTestProfile
        {
            Configuration = "Debug",
            BeforeTests = new[] { new WorkspaceValidationStep
            {
                Kind = WorkspaceValidationStepKind.Command, Executable = "dotnet",
                Arguments = new[] { "bin/{configuration}/{framework}/Host.dll" },
                RequiredPath = "missing/{configuration}/{framework}/Host.dll"
            } }
        };
        var output = Path.Combine(Path.GetTempPath(), "pf-probe-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new DotNetTestSuiteService(new Runner(request =>
            {
                calls++;
                return new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false);
            }));
            var result = await service.RunAsync(Spec(profile), Config, "test", "net8.0", null, output);
            Assert.False(result.Succeeded);
            Assert.Equal(0, calls);
            Assert.Equal("bin/Debug/net8.0/Host.dll", Assert.Single(result.Plan.Steps).Arguments[0]);
            Assert.Contains(Path.Combine("missing", "Debug", "net8.0", "Host.dll"), result.ErrorMessage);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private static string Config => Path.Combine(Path.GetTempPath(), "tests.json");
    private static DotNetTestSuiteSpec Spec(DotNetTestProfile profile) => new() { Profiles = new[] { profile } };
    private sealed class Runner(Func<ProcessRunRequest, ProcessRunResult> run) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(run(request));
    }
}
