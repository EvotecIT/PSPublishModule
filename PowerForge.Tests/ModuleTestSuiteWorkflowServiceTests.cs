using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteWorkflowServiceTests
{
    [Fact]
    public void Execute_returns_failure_message_and_ci_lines_when_tests_fail()
    {
        var result = new ModuleTestSuiteResult(
            projectPath: "c:\\repo",
            testPath: "c:\\repo\\Tests",
            moduleName: "Sample",
            moduleVersion: "1.0.0",
            manifestPath: "c:\\repo\\Sample.psd1",
            requiredModules: Array.Empty<RequiredModuleReference>(),
            dependencyResults: Array.Empty<ModuleDependencyInstallResult>(),
            moduleImported: true,
            exportedFunctionCount: null,
            exportedCmdletCount: null,
            exportedAliasCount: null,
            pesterVersion: "5.7.1",
            totalCount: 10,
            passedCount: 8,
            failedCount: 2,
            skippedCount: 0,
            duration: null,
            coveragePercent: null,
            failureAnalysis: null,
            exitCode: 1,
            stdOut: string.Empty,
            stdErr: string.Empty,
            resultsXmlPath: null);

        var workflow = new ModuleTestSuiteWorkflowService(
            new NullLogger(),
            runSuite: spec =>
            {
                Assert.Equal("c:\\repo", spec.ProjectPath);
                return result;
            },
            buildCiOutputs: (suiteResult, success, errorMessage) =>
            {
                Assert.Same(result, suiteResult);
                Assert.False(success);
                Assert.Equal("2 tests failed", errorMessage);
                return new[] { "ci-line" };
            });

        var output = workflow.Execute(new ModuleTestSuitePreparedContext
        {
            ProjectRoot = "c:\\repo",
            Spec = new ModuleTestSuiteSpec
            {
                ProjectPath = "c:\\repo"
            }
        });

        Assert.Same(result, output.Result);
        Assert.Equal("2 tests failed", output.FailureMessage);
        Assert.Equal(new[] { "ci-line" }, output.CiOutputLines);
    }
}
