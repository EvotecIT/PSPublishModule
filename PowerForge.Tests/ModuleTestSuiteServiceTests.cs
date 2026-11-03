using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleTestSuiteServiceTests
{
    private const string InstalledVersionsMarker = "PFMOD::ITEM::";
    private const string PsResourceInstallMarker = "PFPSRG::INSTALL::OK";
    private const string PowerShellGetInstallMarker = "PFMOD::INSTALL::OK";

    [Fact]
    public void Run_PassesFileCompatibleStringFlagForImportVerbose()
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
            var arguments = capturedRequest!.Arguments.ToList();
            Assert.Equal("1", arguments[arguments.IndexOf("-ImportVerbose") + 1]);
        }
        finally
        {
            try { projectRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_BootstrapsRepositoryToolAndCarriesInstallHostPathsToDesktop(bool windowsPowerShell)
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

            var runner = new TestSuiteDependencyRunner();
            var service = new ModuleTestSuiteService(runner, new NullLogger());

            _ = service.Run(new ModuleTestSuiteSpec
            {
                ProjectPath = projectRoot.FullName,
                AdditionalModules = new[] { "Az.Accounts", "Pester" },
                WindowsPowerShell = windowsPowerShell,
                SkipImport = true
            });

            Assert.Equal(
                new[] { "Microsoft.PowerShell.PSResourceGet", "Az.Accounts", "Pester" },
                runner.InstallNames);
            Assert.NotNull(runner.TestRequest);
            Assert.Equal(windowsPowerShell ? PowerShellHostRequirement.WindowsPowerShell : PowerShellHostRequirement.Any,
                runner.TestRequest!.HostRequirement);
            if (windowsPowerShell)
            {
                var arguments = runner.TestRequest.Arguments.ToList();
                var paths = arguments[arguments.IndexOf("-DependencyModulePathsB64") + 1];
                Assert.Equal(@"C:\CustomCoreModules", Encoding.UTF8.GetString(Convert.FromBase64String(paths)));
            }
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

    private sealed class TestSuiteDependencyRunner : IPowerShellRunner
    {
        public List<string> InstallNames { get; } = new();
        public PowerShellRunRequest? TestRequest { get; private set; }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            if (request.InvocationMode == PowerShellInvocationMode.Command)
                return new PowerShellRunResult(0, "PFTESTPATH::" + Encode(@"C:\CustomCoreModules"), string.Empty, "pwsh.exe");
            Assert.NotNull(request.ScriptPath);
            var script = File.ReadAllText(request.ScriptPath!);

            if (script.Contains(InstalledVersionsMarker, StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names.Select(name =>
                    InstalledVersionsMarker + Encode(name) + "::" + Encode(string.Empty));
                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            if (script.Contains(PsResourceInstallMarker, StringComparison.Ordinal) ||
                script.Contains(PowerShellGetInstallMarker, StringComparison.Ordinal))
            {
                InstallNames.Add(request.Arguments[0]);
                return new PowerShellRunResult(0, PsResourceInstallMarker, string.Empty, "pwsh.exe");
            }

            TestRequest = request;
            return new PowerShellRunResult(
                0,
                string.Join(Environment.NewLine, new[]
                {
                    "PFTEST::PESTER::5.7.1",
                    "PFTEST::COUNTS::0::0::0::0"
                }),
                string.Empty,
                "pwsh.exe");
        }

        private static string[] DecodeLines(string value)
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .ToArray();
        }

        private static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }
}
