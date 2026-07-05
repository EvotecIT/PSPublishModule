using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

[Collection("ModuleRepositoryProfileEnvironment")]
public sealed class SaveManagedScriptCommandTests
{
    [Fact]
    public void SaveManagedScript_defaults_to_script_gallery_endpoint()
    {
        var command = new SaveManagedScriptCommand();

        Assert.Equal("PSGallery", command.RepositoryName);
        Assert.Equal("https://www.powershellgallery.com/api/v2/items/psscript", command.Repository);
    }

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
        var scriptPath = Path.Combine(destination.Path, "Invoke-CompanyTask.ps1");
        Assert.True(File.Exists(scriptPath));
        Assert.Equal("1.0.0", result.ScriptInfo?.Version);
        Assert.Equal(scriptPath, result.ScriptInfo?.Path);
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
    public void SaveManagedScript_plan_blocks_existing_different_version_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSavePlanAction.BlockedExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.Equal("1.0.0", plan.ExistingVersion);
        Assert.Contains("Use Force", plan.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_plan_range_resolves_latest_before_skipping_existing_script()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("MinimumVersion", "1.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSavePlanAction.BlockedExisting, plan.Action);
        Assert.Equal("2.0.0", plan.Version);
        Assert.Equal("1.0.0", plan.ExistingVersion);
    }

    [Fact]
    public void SaveManagedScript_plan_blocks_existing_unreadable_script_without_force()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), "Write-Output 'missing metadata'");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSavePlanAction.BlockedExisting, plan.Action);
        Assert.False(plan.WouldWriteFiles);
        Assert.Null(plan.ExistingVersion);
        Assert.Contains("Use Force", plan.BlockReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_conflicting_version_selectors()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("MinimumVersion", "2.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Version cannot be combined", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_unsafe_required_version_before_comparing_existing_script()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("0.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "../bad");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Unsafe package version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_malformed_existing_version_before_fast_skip()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0/bad"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_plus_prefixed_existing_version_before_fast_skip()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("+1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "0.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MinimumVersion", "../bad")]
    [InlineData("MaximumVersion", "1.0.0/bad")]
    [InlineData("VersionPolicy", ">=1.0.0/bad")]
    public void SaveManagedScript_rejects_unsafe_range_versions_before_comparing_existing_script(string parameterName, string parameterValue)
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter(parameterName, parameterValue);

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Unsafe package version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_plan_rejects_missing_exact_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Plan");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("Version '1.0.0'", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("was not found", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void SaveManagedScript_rejects_repository_package_with_non_parseable_version_before_skip()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.bad.nupkg"),
            "Invoke-CompanyTask",
            "bad",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("0.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("0.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path);

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_incomplete_existing_metadata_before_skip()
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
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScriptWithMinimalMetadata("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("metadata is incomplete", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_skips_existing_required_version_without_repository_lookup()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
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
        Assert.Equal("1.0.0", result.Version);
        Assert.Null(result.Download);
    }

    [Fact]
    public void SaveManagedScript_force_replaces_existing_script_without_temp_leftovers()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("2.0.0", new ManagedScriptFileInfoService().Read(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")).Version);
        Assert.Empty(Directory.EnumerateFiles(destination.Path, ".*.tmp*"));
    }

    [Fact]
    public void SaveManagedScript_force_replaces_existing_script_with_malformed_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0/bad"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("2.0.0", new ManagedScriptFileInfoService().Read(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")).Version);
    }

    [Fact]
    public void SaveManagedScript_force_replaces_existing_script_with_matching_sidecar_and_incomplete_metadata()
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
        var scriptPath = Path.Combine(destination.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, "Write-Output 'missing metadata'");
        File.WriteAllText(
            scriptPath + ".powerforge.json",
            $$"""{"Name":"Invoke-CompanyTask","Version":"1.0.0","ScriptSha256":"{{TestHash.ComputeSha256(scriptPath)}}","RepositoryName":"Local","RepositorySource":"{{feed.Path.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}""");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("1.0.0", new ManagedScriptFileInfoService().Read(scriptPath).Version);
    }

    [Fact]
    public void SaveManagedScript_force_replaces_existing_script_with_unsafe_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.2.0.0.nupkg"),
            "Invoke-CompanyTask",
            "2.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("2.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0-bad/evil"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "2.0.0")
            .AddParameter("Force");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("2.0.0", new ManagedScriptFileInfoService().Read(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")).Version);
    }

    [Fact]
    public void SaveManagedScript_verifies_package_policy_before_skipping_existing_selected_version()
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
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("ExpectedPackageSha256", new string('0', 64));

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("SHA256", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_repairs_existing_selected_version_when_verified_payload_differs()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg");
        var cleanScript = CreateScript("1.0.0");
        TestPackageFactory.Create(
            packagePath,
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = cleanScript
            });
        var scriptPath = Path.Combine(destination.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, cleanScript + Environment.NewLine + "Write-Output 'tampered'");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("ExpectedPackageSha256", TestHash.ComputeSha256(packagePath));
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.NotNull(result.Download);
        Assert.DoesNotContain("tampered", File.ReadAllText(scriptPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_plan_verifies_existing_selected_version_when_package_policy_is_requested()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            },
            requireLicenseAcceptance: true);
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("ExpectedPackageSha256", TestHash.ComputeSha256(packagePath))
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var plan = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSavePlanAction.VerifyExisting, plan.Action);
        Assert.True(plan.WouldWriteFiles);
        Assert.True(plan.WouldVerifyPackage);
        Assert.True(plan.LicenseAcceptanceRequired);
    }

    [Fact]
    public void SaveManagedScript_plan_verifies_package_hash_before_fresh_save()
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
            .AddParameter("ExpectedPackageSha256", new string('0', 64))
            .AddParameter("Plan");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("SHA256", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_plan_does_not_write_to_caller_package_cache_when_verifying_policy()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var packageCache = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
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
            .AddParameter("ExpectedPackageSha256", TestHash.ComputeSha256(packagePath))
            .AddParameter("PackageCacheDirectory", packageCache.Path)
            .AddParameter("Plan");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        _ = Assert.IsType<ManagedScriptSavePlan>(Assert.Single(results).BaseObject);
        Assert.Empty(Directory.EnumerateFileSystemEntries(packageCache.Path));
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_plan_verifies_author_policy_before_forced_reinstall()
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
            },
            authors: "Unexpected");
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0"));

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("AllowedAuthor", new[] { "Evotec" })
            .AddParameter("Force")
            .AddParameter("Plan");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("allowed author", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_uses_script_source_location_for_registered_repository()
    {
        using var moduleFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var scriptSourceLocation = scriptFeed.Path.Replace('\\', '/') + "/items/psscript/";
        var scriptEndpointPath = Path.Combine(scriptFeed.Path, "items", "psscript");
        Directory.CreateDirectory(scriptEndpointPath);
        TestPackageFactory.Create(
            Path.Combine(scriptEndpointPath, "Invoke-CompanyTask.1.0.0.nupkg"),
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
                    ScriptSourceLocation = '{{EscapePowerShellSingleQuoted(scriptSourceLocation)}}'
                    InstallationPolicy = 'Trusted'
                }
            }
            function Get-PSResourceRepository {
                param([string] $Name)
                [pscustomobject]@{
                    Name = $Name
                    Uri = '{{EscapePowerShellSingleQuoted(moduleFeed.Path)}}'
                    Trusted = $true
                }
            }
            """);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", "PrivateRepo")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(Path.GetFullPath(scriptEndpointPath), Path.GetFullPath(result.RepositorySource));
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_uses_registered_psgallery_script_source_location()
    {
        using var moduleFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
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

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", "PSGallery")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(scriptFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void ManagedModuleCommandSupport_preserves_explicit_script_source_endpoint()
    {
        var source = ManagedModuleCommandSupport.ResolveScriptRepositorySource(
            null!,
            "https://packages.example.test/api/v2/items/psscript/",
            out var resolvedRegisteredRepositoryName,
            out var trusted);

        Assert.Equal("https://packages.example.test/api/v2/items/psscript", source);
        Assert.Null(resolvedRegisteredRepositoryName);
        Assert.False(trusted);
    }

    [Fact]
    public void ManagedModuleCommandSupport_defaults_psgallery_script_repository_to_script_feed()
    {
        var source = ManagedModuleCommandSupport.ResolveScriptRepositorySource(
            null!,
            "PSGallery",
            out var resolvedRegisteredRepositoryName,
            out var trusted);

        Assert.Equal("https://www.powershellgallery.com/api/v2/items/psscript", source);
        Assert.Null(resolvedRegisteredRepositoryName);
        Assert.False(trusted);
    }

    [Fact]
    public void SaveManagedScript_profile_name_prefers_registered_script_source_location()
    {
        using var moduleFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(scriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = moduleFeed.Path,
            Trusted = true
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

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal(scriptFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_profile_name_matches_registered_repository_source_uri()
    {
        using var psResourceFeed = new TemporaryDirectory();
        using var powerShellGetFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(scriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = psResourceFeed.Path,
            RepositorySourceUri = powerShellGetFeed.Path,
            Trusted = true
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript($$"""
            function Get-PSRepository {
                param([string] $Name)
                [pscustomobject]@{
                    Name = $Name
                    SourceLocation = '{{EscapePowerShellSingleQuoted(powerShellGetFeed.Path)}}'
                    ScriptSourceLocation = '{{EscapePowerShellSingleQuoted(scriptFeed.Path)}}'
                    InstallationPolicy = 'Trusted'
                }
            }
            """);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal(scriptFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_profile_name_keeps_saved_untrusted_profile_policy()
    {
        using var moduleFeed = new TemporaryDirectory();
        using var scriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(scriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = moduleFeed.Path,
            Trusted = false
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

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not trusted", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_profile_name_falls_back_to_saved_source_when_registered_script_source_is_untrusted()
    {
        using var trustedFeed = new TemporaryDirectory();
        using var otherScriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(trustedFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        TestPackageFactory.Create(
            Path.Combine(otherScriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = trustedFeed.Path,
            Trusted = true
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript($$"""
            function Get-PSRepository {
                param([string] $Name)
                [pscustomobject]@{
                    Name = $Name
                    SourceLocation = '{{EscapePowerShellSingleQuoted(trustedFeed.Path)}}'
                    ScriptSourceLocation = '{{EscapePowerShellSingleQuoted(otherScriptFeed.Path)}}'
                    InstallationPolicy = 'Untrusted'
                }
            }
            """);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal(trustedFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_profile_name_falls_back_to_saved_source_when_registered_source_mismatches()
    {
        using var trustedFeed = new TemporaryDirectory();
        using var otherModuleFeed = new TemporaryDirectory();
        using var otherScriptFeed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        using var profileRoot = new TemporaryDirectory();
        using var profileScope = UseProfileStore(profileRoot.Path);
        TestPackageFactory.Create(
            Path.Combine(trustedFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        TestPackageFactory.Create(
            Path.Combine(otherScriptFeed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        new ModuleRepositoryProfileStore().SaveProfile(new ModuleRepositoryProfile
        {
            Name = "Company",
            Provider = PrivateGalleryProvider.NuGet,
            RepositoryName = "CompanyModules",
            RepositoryUri = trustedFeed.Path,
            Trusted = true
        });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddScript($$"""
            function Get-PSRepository {
                param([string] $Name)
                [pscustomobject]@{
                    Name = $Name
                    SourceLocation = '{{EscapePowerShellSingleQuoted(otherModuleFeed.Path)}}'
                    ScriptSourceLocation = '{{EscapePowerShellSingleQuoted(otherScriptFeed.Path)}}'
                    InstallationPolicy = 'Untrusted'
                }
            }
            """);
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("ProfileName", "Company")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0")
            .AddParameter("RequireTrustedRepository");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal(trustedFeed.Path, result.RepositorySource);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_rejects_package_without_expected_script_payload()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Helper.ps1"] = CreateScript("1.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("expected script payload", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_rejects_package_with_malformed_script_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0/bad")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_allows_stale_script_metadata_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(feed.Path, "Invoke-CompanyTask.1.2.0.nupkg");
        TestPackageFactory.Create(
            packagePath,
            "Invoke-CompanyTask",
            "1.2.0",
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
            .AddParameter("RequiredVersion", "1.2.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.Saved, result.Status);
        Assert.Equal("1.2.0", result.Version);
        var scriptInfo = Assert.IsType<ManagedScriptFileInfo>(result.ScriptInfo);
        Assert.Equal("1.0.0", scriptInfo.Version);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));

        File.Delete(packagePath);

        ps.Commands.Clear();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.2.0");
        var skippedResults = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var skipped = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(skippedResults).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.SkippedExisting, skipped.Status);
        Assert.Equal("1.2.0", skipped.Version);
        Assert.NotNull(skipped.ScriptInfo);
        Assert.Equal("1.0.0", skipped.ScriptInfo.Version);
        Assert.Null(skipped.Download);

        TestPackageFactory.Create(
            packagePath,
            "Invoke-CompanyTask",
            "1.2.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("1.0.0")
            });
        File.WriteAllText(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1"), CreateScript("1.0.0") + Environment.NewLine + "Write-Output 'tampered'");

        ps.Commands.Clear();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.2.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("already exists with version '1.0.0'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_ignores_non_parseable_install_record_version_before_skip()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var scriptPath = Path.Combine(destination.Path, "Invoke-CompanyTask.ps1");
        File.WriteAllText(scriptPath, CreateScript("0.0.0"));
        File.WriteAllText(
            scriptPath + ".powerforge.json",
            $$"""{"Name":"Invoke-CompanyTask","Version":"bad","ScriptSha256":"{{TestHash.ComputeSha256(scriptPath)}}","RepositoryName":"Local","RepositorySource":"{{feed.Path.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}""");

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "0.0.0");
        var results = ps.Invoke();

        AssertNoPowerShellErrors(ps);
        var result = Assert.IsType<ManagedScriptSaveResult>(Assert.Single(results).BaseObject);
        Assert.Equal(ManagedScriptSaveStatus.SkippedExisting, result.Status);
        Assert.Equal("0.0.0", result.Version);
        Assert.Null(result.Download);
    }

    [Theory]
    [InlineData("RequiredVersion", "garbage")]
    [InlineData("MinimumVersion", "garbage")]
    [InlineData("MaximumVersion", "garbage")]
    [InlineData("VersionPolicy", ">= 2.0.0")]
    [InlineData("RequiredVersion", "+1.0.0")]
    public void SaveManagedScript_rejects_non_parseable_version_selectors(string parameterName, string parameterValue)
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter(parameterName, parameterValue);

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManagedScript_rejects_repository_package_with_non_parseable_version()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.bad.nupkg"),
            "Invoke-CompanyTask",
            "bad",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = CreateScript("0.0.0")
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path);

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("not a valid", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
    }

    [Fact]
    public void SaveManagedScript_rejects_package_with_incomplete_script_metadata()
    {
        using var feed = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(feed.Path, "Invoke-CompanyTask.1.0.0.nupkg"),
            "Invoke-CompanyTask",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Invoke-CompanyTask.ps1"] = """
                    <#PSScriptInfo
                    .VERSION 1.0.0
                    .GUID 00000000-0000-0000-0000-000000000001
                    #>
                    Write-Output 'ok'
                    """
            });

        using var ps = CreatePowerShellWithModuleImported();
        ps.AddCommand("Save-ManagedScript")
            .AddParameter("Name", "Invoke-CompanyTask")
            .AddParameter("Repository", feed.Path)
            .AddParameter("RepositoryName", "Local")
            .AddParameter("Path", destination.Path)
            .AddParameter("RequiredVersion", "1.0.0");

        var ex = Assert.Throws<CmdletInvocationException>(() => ps.Invoke());
        Assert.Contains("metadata is incomplete", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(destination.Path, "Invoke-CompanyTask.ps1")));
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
           .DESCRIPTION
           Test script.
           #>
           Write-Output 'ok'
           """;

    private static string CreateScriptWithMinimalMetadata(string version)
        => $$"""
           <#PSScriptInfo
           .VERSION {{version}}
           .GUID 00000000-0000-0000-0000-000000000001
           #>

           Write-Output 'ok'
           """;

    private static string EscapePowerShellSingleQuoted(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static IDisposable UseProfileStore(string root)
    {
        Directory.CreateDirectory(root);
        return new TestEnvironmentVariable(
            "POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH",
            Path.Combine(root, "profiles.json"));
    }

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }

    private sealed class TestEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        internal TestEnvironmentVariable(string name, string value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}
