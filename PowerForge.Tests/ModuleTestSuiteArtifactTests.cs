using System.Text;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteArtifactTests
{
    [Fact]
    public void Run_keeps_both_reports_summarizes_failure_and_requires_the_requested_host()
    {
        using var fixture = new SampleModule();
        var runner = new CallbackRunner(request =>
        {
            Assert.Equal(PowerShellHostRequirement.WindowsPowerShell, request.HostRequirement);
            Assert.Equal(fixture.Root, request.WorkingDirectory);
            Assert.Equal("6.1.0", NamedArgument(request, "-PesterVersion"));
            File.WriteAllText(request.Arguments[4], """
                <test-results total="1" failures="1" errors="0" not-run="0">
                  <test-suite name="Sample"><results>
                    <test-case name="Sample fails" success="False" result="Failure">
                      <failure><message>Expected &lt;true&gt;</message></failure>
                    </test-case>
                  </results></test-suite>
                </test-results>
                """);
            File.WriteAllText(NamedArgument(request, "-JUnitResultsPath"), "<testsuites tests=\"1\" failures=\"1\" />");
            return new PowerShellRunResult(1, "PFTEST::PESTER::6.1.0\nPFTEST::COUNTS::1::0::1::0", string.Empty, "powershell.exe");
        });
        var spec = fixture.Spec();
        spec.WindowsPowerShell = true;
        spec.PesterVersion = "6.1.0";
        var result = new ModuleTestSuiteService(runner, new NullLogger()).Run(spec);

        Assert.Equal(1, result.ExitCode);
        Assert.True(File.Exists(result.ResultsXmlPath));
        Assert.True(File.Exists(result.JUnitResultsPath));
        Assert.Contains("Sample fails", File.ReadAllText(result.SummaryPath!));
        Assert.Contains("&lt;true&gt;", File.ReadAllText(result.SummaryPath!));
        Assert.Equal(1, result.FailedCount);
        Assert.NotNull(result.FailureAnalysis);
    }

    [Fact]
    public void Setup_failure_removes_stale_reports_and_writes_a_failure_summary()
    {
        using var fixture = new SampleModule();
        var spec = fixture.Spec();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "results"));
        File.WriteAllText(Path.Combine(fixture.Root, spec.ResultsPath!), "old green report");
        File.WriteAllText(Path.Combine(fixture.Root, spec.JUnitResultsPath!), "old green report");
        var runner = new CallbackRunner(_ => new PowerShellRunResult(2,
            "PFTEST::ERROR::" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Pester import failed")), string.Empty, "pwsh"));

        Assert.Throws<InvalidOperationException>(() => new ModuleTestSuiteService(runner, new NullLogger()).Run(spec));
        Assert.False(File.Exists(Path.Combine(fixture.Root, spec.ResultsPath!)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, spec.JUnitResultsPath!)));
        Assert.Contains("Pester import failed", File.ReadAllText(Path.Combine(fixture.Root, spec.SummaryPath!)));
    }

    [Fact]
    public void Workflow_treats_discovery_errors_as_failure_even_with_zero_failed_tests()
    {
        using var fixture = new SampleModule();
        var result = new ModuleTestSuiteService(new CallbackRunner(_ =>
            new PowerShellRunResult(1, "PFTEST::COUNTS::1::1::0::0", "Discovery failed", "pwsh")), new NullLogger()).Run(fixture.Spec());
        var output = new ModuleTestSuiteWorkflowService(new NullLogger(), runSuite: _ => result)
            .Execute(new ModuleTestSuitePreparedContext { Spec = fixture.Spec() });

        Assert.False(output.Success);
        Assert.Contains("exit code 1", output.FailureMessage);
    }

    private static string NamedArgument(PowerShellRunRequest request, string name)
    {
        var args = request.Arguments.ToList();
        return args[args.IndexOf(name) + 1];
    }

    private sealed class CallbackRunner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }

    private sealed class SampleModule : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "pf-suite-artifacts-" + Guid.NewGuid().ToString("N"));
        internal SampleModule()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Tests"));
            File.WriteAllText(Path.Combine(Root, "Sample.psd1"), "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(Root, "Sample.psm1"), string.Empty);
        }

        internal ModuleTestSuiteSpec Spec() => new()
        {
            ProjectPath = Root,
            SkipDependencies = true,
            SkipImport = true,
            ResultsPath = "results/nunit.xml",
            JUnitResultsPath = "results/junit.xml",
            SummaryPath = "results/summary.md"
        };
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
