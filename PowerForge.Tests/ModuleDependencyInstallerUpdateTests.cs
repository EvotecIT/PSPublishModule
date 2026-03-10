using System.Collections.Generic;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleDependencyInstallerUpdateTests
{
    [Fact]
    public void EnsureUpdated_ReturnsSatisfied_WhenInstalledVersionDidNotChange()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.0.0")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, "PFMOD::UPDATE::OK", string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.0.0")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company");

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Satisfied, result.Status);
        Assert.Equal("1.0.0", result.InstalledVersion);
        Assert.Equal("1.0.0", result.ResolvedVersion);
        Assert.Equal("PSResourceGet", result.Installer);
        Assert.Equal("Already up to date", result.Message);
    }

    [Fact]
    public void EnsureUpdated_ReturnsUpdated_WhenInstalledVersionChanged()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.0.0")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, "PFMOD::UPDATE::OK", string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.1.0")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company");

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("1.0.0", result.InstalledVersion);
        Assert.Equal("1.1.0", result.ResolvedVersion);
        Assert.Equal("PSResourceGet", result.Installer);
        Assert.Equal("Update requested", result.Message);
    }

    private static string BuildInstalledVersionsStdOut(params (string Name, string Version)[] items)
    {
        var lines = new List<string>(items.Length);
        foreach (var item in items)
        {
            lines.Add($"PFMOD::ITEM::{Encode(item.Name)}::{Encode(item.Version)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private sealed class QueuePowerShellRunner : IPowerShellRunner
    {
        private readonly Queue<PowerShellRunResult> _results;

        public QueuePowerShellRunner(IEnumerable<PowerShellRunResult> results)
        {
            _results = new Queue<PowerShellRunResult>(results);
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _results.Dequeue();
        }
    }
}
