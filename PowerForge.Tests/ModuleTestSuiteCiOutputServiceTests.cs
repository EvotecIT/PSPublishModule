using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteCiOutputServiceTests
{
    [Fact]
    public void BuildOutputs_emits_github_and_azure_lines_when_environments_are_present()
    {
        var service = new ModuleTestSuiteCiOutputService(name => name switch
        {
            "GITHUB_ACTIONS" => "true",
            "TF_BUILD" => "true",
            _ => null
        });

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
            coveragePercent: 87.25,
            failureAnalysis: null,
            exitCode: 1,
            stdOut: string.Empty,
            stdErr: string.Empty,
            resultsXmlPath: null);

        var lines = service.BuildOutputs(result, success: false, errorMessage: "2 tests failed");

        Assert.Contains("::set-output name=test-result::false", lines);
        Assert.Contains("::set-output name=code-coverage::87.25", lines);
        Assert.Contains("##vso[task.setvariable variable=FailedTests;isOutput=true]2", lines);
        Assert.Contains("##vso[task.setvariable variable=ErrorMessage;isOutput=true]2 tests failed", lines);
    }
}
