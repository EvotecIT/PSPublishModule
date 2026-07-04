using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class SaveManagedScriptCommandTests
{
    [Fact]
    public void SaveManagedScript_saves_script_resource_to_destination()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
        Assert.Equal("1.0.0", result.ScriptInfo?.Version);
    }

    [Fact]
    public void SaveManagedScript_plan_does_not_write_script()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSavePlanAction.Save, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_skips_existing_selected_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.SkippedExisting, result.Status);
        Assert.Null(result.Download);
    }

    [Fact]
    public void SaveManagedScript_rejects_package_without_script_metadata()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = "Write-Output 'missing metadata'"
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("PSScriptInfo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(SaveManagedScriptCommand).Assembly.Location)
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
