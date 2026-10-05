using System.Text;

namespace PowerForge.Tests;

public sealed class ModuleImportSearchPathTests
{
    [Fact]
    public void ValidationImportsRequiredModulesFromTheBuildHostsCustomSearchPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-import-path-" + Guid.NewGuid().ToString("N"));
        var dependencyRoot = Path.Combine(root, "custom modules");
        var dependency = Path.Combine(dependencyRoot, "Fixture.Dependency");
        Directory.CreateDirectory(dependency);
        try
        {
            File.WriteAllText(Path.Combine(dependency, "Fixture.Dependency.psm1"), "function Get-FixtureValue { 'ok' }");
            File.WriteAllText(Path.Combine(dependency, "Fixture.Dependency.psd1"),
                "@{ RootModule = 'Fixture.Dependency.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-FixtureValue') }");
            var manifest = Path.Combine(root, "Consumer.psd1");
            File.WriteAllText(manifest,
                "@{ ModuleVersion = '1.0.0'; RequiredModules = @(@{ModuleName='Fixture.Dependency';ModuleVersion='1.0.0'}) }");
            var script = Path.Combine(root, "Import-Modules.ps1");
            File.WriteAllText(script, PowerForgeScripts.Load("Scripts/ModulePipeline/Import-Modules.ps1"));
            var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var result = new PowerShellRunner().Run(new PowerShellRunRequest(
                    script,
                    new[] { "", "0", "1", manifest, "0", Convert.ToBase64String(Encoding.UTF8.GetBytes(dependencyRoot)) },
                    TimeSpan.FromMinutes(1), workingDirectory: root, executableOverride: host));
                Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
