using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class UninstallManagedScriptCommandTests
{
    [Fact]
    public void UninstallManagedScript_removes_script_from_custom_root()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal("1.0.0", result.ExistingVersion);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_plan_does_not_remove_script()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptUninstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallPlanAction.Remove, plan.Action);
        Assert.True(plan.WouldRemoveFile);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_skips_missing_script()
    {
        using var scriptRoot = new TemporaryDirectory();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path);
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.SkippedMissing, result.Status);
    }

    [Fact]
    public void UninstallManagedScript_skips_version_mismatch()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "2.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.SkippedVersionMismatch, result.Status);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_force_removes_script_without_metadata()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, "Write-Output 'missing metadata'");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.False(File.Exists(scriptPath));
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(UninstallManagedScriptCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();
        return ps;
    }

    private static string CreateScript(string version)
        => $$"""
           <#PSScriptInfo
           .VERSION {{version}}
           .GUID 00000000-0000-0000-0000-000000000001
           .AUTHOR Evotec
           .DESCRIPTION Test script.
           #>

           <#
           .SYNOPSIS
           Test script.
           #>
           Write-Output 'ok'
           """;

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
