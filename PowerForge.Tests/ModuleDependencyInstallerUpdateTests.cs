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

    [Fact]
    public void EnsureUpdated_UsesRepositoryScopedPowerShellGetFallback_WhenRequested()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.0.0")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildPowerShellGetFindStdOut(("ModuleA", "1.1.0", "Company")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, "PFMOD::INSTALL::OK", string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.1.0")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company",
            preferPowerShellGet: true);

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("PowerShellGet", result.Installer);
        Assert.Equal("1.1.0", result.ResolvedVersion);
    }

    [Fact]
    public void EnsureUpdated_UsesSemanticOrdering_ForPowerShellGetRepositoryFallback()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.2.0-preview9")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildPowerShellGetFindStdOut(
                ("ModuleA", "1.2.0-preview9", "Company"),
                ("ModuleA", "1.2.0-preview10", "Company")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, "PFMOD::INSTALL::OK", string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.2.0-preview10")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company",
            prerelease: true,
            preferPowerShellGet: true);

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("PowerShellGet", result.Installer);
        Assert.Equal("1.2.0-preview10", result.ResolvedVersion);
    }

    [Fact]
    public void EnsureUpdated_FallsBackToPowerShellGet_WhenPSResourceGetUpdateFails()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.0.0")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(1, string.Empty, "Repository 'Company' is not registered in PSResourceGet.", "pwsh.exe"),
            new PowerShellRunResult(0, BuildPowerShellGetFindStdOut(("ModuleA", "1.1.0", "Company")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, "PFMOD::INSTALL::OK", string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.1.0")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company");

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("PowerShellGet", result.Installer);
        Assert.Equal("1.1.0", result.ResolvedVersion);
    }

    [Fact]
    public void EnsureUpdated_DoesNotDowngrade_WhenRepositoryFallbackFindsOlderVersion()
    {
        var runner = new QueuePowerShellRunner(new[]
        {
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.2.0-preview10")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(1, string.Empty, "Repository 'Company' is not registered in PSResourceGet.", "pwsh.exe"),
            new PowerShellRunResult(0, BuildPowerShellGetFindStdOut(("ModuleA", "1.2.0-preview9", "Company")), string.Empty, "pwsh.exe"),
            new PowerShellRunResult(0, BuildInstalledVersionsStdOut(("ModuleA", "1.2.0-preview10")), string.Empty, "pwsh.exe")
        });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(
            new[] { new ModuleDependency("ModuleA") },
            repository: "Company");

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Satisfied, result.Status);
        Assert.Equal("1.2.0-preview10", result.InstalledVersion);
        Assert.Equal("1.2.0-preview10", result.ResolvedVersion);
        Assert.Null(result.Installer);
        Assert.Equal("Already up to date", result.Message);
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

    private static string BuildPowerShellGetFindStdOut(params (string Name, string Version, string Repository)[] items)
    {
        var lines = new List<string>(items.Length);
        foreach (var item in items)
        {
            lines.Add($"PFPWSGET::ITEM::{Encode(item.Name)}::{Encode(item.Version)}::{Encode(item.Repository)}::{Encode(string.Empty)}");
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
