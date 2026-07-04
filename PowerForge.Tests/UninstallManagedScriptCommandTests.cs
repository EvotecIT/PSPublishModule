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
    public void UninstallManagedScript_binds_script_root_from_pipeline_object()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));
        var input = new PSObject();
        input.Properties.Add(new PSNoteProperty("Name", "Invoke-CompanyTask"));
        input.Properties.Add(new PSNoteProperty("ScriptRoot", scriptRoot.Path));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke(new[] { input });

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal(scriptRoot.Path, result.ScriptRoot);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_binds_script_file_path_from_pipeline_object()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));
        var input = new PSObject();
        input.Properties.Add(new PSNoteProperty("Name", "Invoke-CompanyTask"));
        input.Properties.Add(new PSNoteProperty("Path", scriptPath));
        input.Properties.Add(new PSNoteProperty("Version", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript");
        var results = ps.Invoke(new[] { input });

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal(scriptRoot.Path, result.ScriptRoot);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_binds_destination_path_from_save_result_pipeline_object()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));
        var input = new PSObject();
        input.Properties.Add(new PSNoteProperty("Name", "Invoke-CompanyTask"));
        input.Properties.Add(new PSNoteProperty("DestinationPath", scriptRoot.Path));
        input.Properties.Add(new PSNoteProperty("Version", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript");
        var results = ps.Invoke(new[] { input });

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal(scriptRoot.Path, result.ScriptRoot);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_binds_script_path_from_save_result_pipeline_object()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));
        var input = new PSObject();
        input.Properties.Add(new PSNoteProperty("Name", "Invoke-CompanyTask"));
        input.Properties.Add(new PSNoteProperty("ScriptPath", scriptPath));
        input.Properties.Add(new PSNoteProperty("Version", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript");
        var results = ps.Invoke(new[] { input });

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal(scriptRoot.Path, result.ScriptRoot);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_binds_version_from_pipeline_object()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("2.0.0"));
        var input = new PSObject();
        input.Properties.Add(new PSNoteProperty("Name", "Invoke-CompanyTask"));
        input.Properties.Add(new PSNoteProperty("ScriptRoot", scriptRoot.Path));
        input.Properties.Add(new PSNoteProperty("Version", "1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript");
        var results = ps.Invoke(new[] { input });

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.SkippedVersionMismatch, result.Status);
        Assert.Equal("2.0.0", result.ExistingVersion);
        Assert.True(File.Exists(scriptPath));
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
    public void UninstallManagedScript_rejects_malformed_required_version_before_removing()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.foo");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Invalid script version", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_treats_malformed_installed_version_as_mismatch()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.foo"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.SkippedVersionMismatch, result.Status);
        Assert.Null(result.ExistingVersion);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_accepts_required_version_with_build_metadata()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0+build.7"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0+build.7");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_ignores_installed_build_metadata_for_exact_version_match()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0+build.7"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.Removed, result.Status);
        Assert.Equal("1.0.0+build.7", result.ExistingVersion);
        Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_rejects_malformed_required_build_metadata_before_removing()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0+build.7"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0+bad..metadata");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Invalid script version", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_rejects_malformed_required_prerelease_before_removing()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0-.."));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0-..");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Invalid script version", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_treats_malformed_installed_prerelease_as_mismatch()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0-.."));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0-beta");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptUninstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallStatus.SkippedVersionMismatch, result.Status);
        Assert.Null(result.ExistingVersion);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_plan_treats_unreadable_exact_metadata_as_mismatch()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("1.0.0-.."));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Uninstall-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0-beta")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptUninstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptUninstallPlanAction.SkipVersionMismatch, plan.Action);
        Assert.False(plan.WouldRemoveFile);
        Assert.Null(plan.ExistingVersion);
        Assert.True(File.Exists(scriptPath));
    }

    [Fact]
    public void UninstallManagedScript_matches_installed_script_name_case_insensitively()
    {
        using var scriptRoot = new TemporaryDirectory();
        var scriptPath = Path.Combine(scriptRoot.Path, "invoke-companytask.ps1");
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
        Assert.False(File.Exists(scriptPath));
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

    [Theory]
    [InlineData(ManagedModuleShellEdition.Desktop, true, "WindowsPowerShell")]
    [InlineData(ManagedModuleShellEdition.Core, true, "PowerShell")]
    [InlineData(ManagedModuleShellEdition.Desktop, false, "PowerShell")]
    [InlineData(ManagedModuleShellEdition.Core, false, "PowerShell")]
    public void Script_root_folder_name_matches_install_semantics(
        ManagedModuleShellEdition shellEdition,
        bool isWindows,
        string expected)
    {
        Assert.Equal(expected, ManagedScriptResourceService.ResolveScriptShellFolderName(shellEdition, isWindows));
    }

    [Fact]
    public void Final_uninstall_action_revalidates_exact_version()
    {
        var action = ManagedScriptResourceService.ResolveFinalUninstallAction(
            ManagedScriptUninstallPlanAction.Remove,
            currentVersion: "2.0.0",
            requestedVersion: "1.0.0");

        Assert.Equal(ManagedScriptUninstallPlanAction.SkipVersionMismatch, action);
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
