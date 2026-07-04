using System.Runtime.InteropServices;
using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InstallManagedScriptCommandTests
{
    [Fact]
    public void InstallManagedScript_installs_script_resource_to_custom_root()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallStatus.Installed, result.Status);
        Assert.Equal(ManagedScriptInstallScope.Custom, result.Scope);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1")));
        Assert.Equal("1.0.0", result.ScriptInfo?.Version);
    }

    [Fact]
    public void InstallManagedScript_plan_does_not_write_script()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallPlanAction.Install, plan.Action);
        Assert.Equal(ManagedScriptInstallScope.Custom, plan.Scope);
        Assert.True(plan.WouldWriteFiles);
        Assert.False(File.Exists(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void InstallManagedScript_plan_blocks_existing_different_version_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallPlanAction.BlockedExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.Equal("1.0.0", plan.ExistingVersion);
        Assert.Contains("Use Force", plan.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallManagedScript_plan_blocks_existing_unreadable_script_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), "Write-Output 'missing metadata'");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallPlanAction.BlockedExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.Null(plan.ExistingVersion);
        Assert.Contains("Use Force", plan.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallManagedScript_plan_reports_license_acceptance_requirement()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            },
            requireLicenseAcceptance: true);

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallPlanAction.Install, plan.Action);
        Assert.True(plan.LicenseAcceptanceRequired);
        Assert.True(plan.WouldWriteFiles);
    }

    [Fact]
    public void InstallManagedScript_skips_existing_selected_version()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallStatus.SkippedExisting, result.Status);
        Assert.Null(result.Download);
    }

    [Fact]
    public void InstallManagedScript_skips_existing_required_version_without_repository_lookup()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallStatus.SkippedExisting, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.Null(result.Download);
    }

    [Fact]
    public void InstallManagedScript_does_not_skip_existing_when_repository_trust_is_rejected()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not trusted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallManagedScript_plan_verifies_existing_selected_version_when_package_policy_is_requested()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            },
            requireLicenseAcceptance: true);
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("ExpectedPackageSha256", new string('0', 64))
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallPlanAction.VerifyExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.True(plan.WouldVerifyPackage);
        Assert.True(plan.LicenseAcceptanceRequired);
    }

    [Fact]
    public void InstallManagedScript_uses_unix_powershell_script_root_for_desktop_shell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        using var feed = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("HOME");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ShellEdition", ManagedModuleShellEdition.Desktop)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptInstallPlan>(Assert.Single(results).BaseObject);
        Assert.Equal(Path.Combine(home!, ".local", "share", "powershell", "Scripts"), plan.ScriptRoot);
    }

    [Fact]
    public void InstallManagedScript_does_not_satisfy_stable_range_with_existing_prerelease()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.5.0-beta"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("MinimumVersion", "1.0.0")
            .AddParameter("MaximumVersion", "2.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("No versions", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallManagedScript_skips_existing_version_satisfying_range_without_repository_lookup()
    {
        using var feed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.5.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("MinimumVersion", "1.0.0")
            .AddParameter("MaximumVersion", "2.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptInstallStatus.SkippedExisting, result.Status);
        Assert.Equal("1.5.0", result.Version);
        Assert.Null(result.Download);
    }

    [Fact]
    public void InstallManagedScript_uses_script_source_location_for_registered_repository()
    {
        using var moduleFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var scriptRoot = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(scriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript($$"""
            function Get-PSRepository {
                param([string] $Name)
                [pscustomobject]@{
                    Name = $Name
                    SourceLocation = '{{EscapePowerShellSingleQuoted(moduleFeed.Path)}}'
                    ScriptSourceLocation = '{{EscapePowerShellSingleQuoted(scriptFeed.Path)}}'
                    InstallationPolicy = 'Trusted'
                }
            }
            """);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Install-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", "PrivateRepo")
            .AddParameter("ScriptRoot", scriptRoot.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptInstallResult>(Assert.Single(results).BaseObject);
        Assert.Equal(scriptFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(scriptRoot.Path, "Invoke-CompanyTask.ps1")));
    }

    private static PowerShell CreatePowerShellWithModuleImported()
    {
        var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedScriptCommand).Assembly.Location)
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
           .DESCRIPTION
           Test script.
           #>
           Write-Output 'ok'
           """;

    private static string EscapePowerShellSingleQuoted(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}
