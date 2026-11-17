using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineHostedOperationsTests
{
    [Fact]
    public void EnsureBuildDependenciesInstalledIfNeeded_FailureNamesEachModuleAndItsCause()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMinimalModule(root.FullName, moduleName, "1.0.0");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec { Name = moduleName, SourcePath = root.FullName, Version = "1.0.0" },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment { BuildModule = new BuildModuleConfiguration { InstallMissingModules = true } },
                    new ConfigurationModuleSegment
                    {
                        Kind = ModuleDependencyKind.RequiredModule,
                        Configuration = new ModuleDependencyConfiguration
                        {
                            ModuleName = "Pester",
                            RequiredVersion = "5.6.1",
                            VersionSource = ModuleDependencyVersionSource.Auto
                        }
                    }
                }
            };
            var hostedOperations = new FakeHostedOperations();
            hostedOperations.DependencyFailures["Pester"] = "Install-PSResource failed (exit 1). Response status code does not indicate success: 403 (Forbidden).";
            var runner = new ModulePipelineRunner(new NullLogger(), new ThrowingPowerShellRunner(), new FakeMetadataProvider(), hostedOperations);

            var error = Assert.Throws<TargetInvocationException>(() => InvokeEnsureBuildDependenciesInstalledIfNeeded(runner, runner.Plan(spec)));

            var failure = Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Contains("failed for 1 module", failure.Message, StringComparison.Ordinal);
            Assert.Contains("Pester 5.6.1: Install-PSResource failed (exit 1).", failure.Message, StringComparison.Ordinal);
            Assert.Contains("403 (Forbidden)", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
