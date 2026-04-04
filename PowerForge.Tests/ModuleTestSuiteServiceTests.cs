using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteServiceTests
{
    [Fact]
    public void Run_PassesNamedBooleanTokenForImportVerbose()
    {
        var projectRoot = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "SampleModule";
            Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Tests"));
            File.WriteAllText(Path.Combine(projectRoot.FullName, $"{moduleName}.psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(projectRoot.FullName, $"{moduleName}.psd1"),
                "@{ RootModule = 'SampleModule.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @(); CmdletsToExport = @(); AliasesToExport = @() }");

            PowerShellRunRequest? capturedRequest = null;
            var runner = new RecordingPowerShellRunner(request =>
            {
                capturedRequest = request;
                return new PowerShellRunResult(0, "PFTEST::COUNTS::0::0::0::0", string.Empty, "pwsh.exe");
            });

            var service = new ModuleTestSuiteService(runner, new NullLogger());

            _ = service.Run(new ModuleTestSuiteSpec
            {
                ProjectPath = projectRoot.FullName,
                SkipDependencies = true,
                ImportModulesVerbose = true
            });

            Assert.NotNull(capturedRequest);
            Assert.Contains("-ImportVerbose:$true", capturedRequest!.Arguments);
        }
        finally
        {
            try { projectRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class RecordingPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _run;

        public RecordingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> run)
        {
            _run = run;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _run(request);
    }
}
