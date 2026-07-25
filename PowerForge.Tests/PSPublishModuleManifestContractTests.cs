using System.Management.Automation.Language;
using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForge;

namespace PowerForge.Tests;

public sealed class PSPublishModuleManifestContractTests
{
    private static readonly string[] OptionalToolModules =
    {
        "Pester",
        "PowerShellGet",
        "Microsoft.PowerShell.PSResourceGet"
    };

    private static readonly string[] EmbeddedDependencyCmdlets =
    {
        "Install-ModuleDependency",
        "Import-ModuleDependency"
    };

    private static readonly string[] DslHelperCmdlets =
    {
        "Get-ConfigurationBoolean",
        "New-ConfigurationGate",
        "New-ConfigurationModuleBuildProfile",
        "New-ConfigurationPackageBuild",
        "New-ConfigurationProjectBuild",
        "New-ConfigurationRelease"
    };

    private static readonly string[] ManagedModuleCmdlets =
    {
        "Compress-ManagedResource",
        "Find-ManagedModule",
        "Get-ManagedModule",
        "Get-ManagedModuleRepository",
        "Initialize-ManagedModuleRepository",
        "Import-ManagedModuleRepository",
        "Install-ManagedModule",
        "Publish-ManagedModule",
        "Register-ManagedModuleRepository",
        "Remove-ManagedModuleRepository",
        "Repair-ManagedModule",
        "Reset-ManagedModuleRepository",
        "Save-ManagedModule",
        "Set-ManagedModuleRepository",
        "Unregister-ManagedModuleRepository",
        "Update-ManagedModule"
    };

    private static readonly string[] ManagedScriptFileInfoCmdlets =
    {
        "Get-ManagedScriptFileInfo",
        "Install-ManagedScript",
        "New-ManagedScriptFileInfo",
        "Save-ManagedScript",
        "Test-ManagedScriptFileInfo",
        "Update-ManagedScriptFileInfo"
    };

    private static readonly string[] UnreleasedModuleStateCmdlets =
    {
        "Get-ModuleState",
        "Get-ModuleStatePlan",
        "Invoke-ModuleState",
        "Invoke-ModuleStatePlan",
        "Test-ModuleState"
    };

    private static readonly string[] DocumentationCmdlets =
    {
        "Get-ModuleDocumentation",
        "Install-ModuleDocumentation",
        "Install-ModuleScript",
        "Set-ModuleDocumentation",
        "Show-ModuleDocumentation"
    };

    private static readonly string[] DocumentationAliases =
    {
        "Install-Documentation",
        "Install-ModuleScripts",
        "Install-Scripts",
        "Set-Documentation",
        "Show-Documentation"
    };

    [Fact]
    public void Manifest_does_not_require_feature_specific_tool_modules_at_import_time()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestPath = Path.Combine(repoRoot, "Module", "PSPublishModule.psd1");

        Assert.True(ManifestEditor.TryGetRequiredModules(manifestPath, out RequiredModuleReference[]? requiredModules));

        var requiredNames = requiredModules!
            .Select(static module => module.ModuleName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var optionalModule in OptionalToolModules)
        {
            Assert.DoesNotContain(requiredNames, name => string.Equals(name, optionalModule, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Json_build_recipe_does_not_declare_feature_specific_tool_modules()
    {
        var repoRoot = RepoRootLocator.Find();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "powerforge.json")));
        var moduleNames = document.RootElement.GetProperty("Segments")
            .EnumerateArray()
            .Where(segment => segment.TryGetProperty("Configuration", out var configuration) &&
                              configuration.TryGetProperty("ModuleName", out _))
            .Select(segment => segment.GetProperty("Configuration").GetProperty("ModuleName").GetString())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        foreach (var optionalModule in OptionalToolModules)
            Assert.DoesNotContain(moduleNames, name => string.Equals(name, optionalModule, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Root_build_bootstraps_local_cmdlets_and_forwards_to_unified_release()
    {
        var repoRoot = RepoRootLocator.Find();
        var entryPoint = File.ReadAllText(Path.Combine(repoRoot, "Build", "Build-Project.ps1"));

        Assert.Contains("$bootstrapFrameworks = if ($PSEdition -eq 'Desktop')", entryPoint, StringComparison.Ordinal);
        Assert.Contains("'net472'", entryPoint, StringComparison.Ordinal);
        Assert.Contains("'net8.0'", entryPoint, StringComparison.Ordinal);
        Assert.Contains("@('net8.0', 'net10.0')", entryPoint, StringComparison.Ordinal);
        Assert.Contains("$desktopChildFramework = if ($ModuleFramework -eq 'auto')", entryPoint, StringComparison.Ordinal);
        Assert.Contains("dotnet build $moduleProject", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Import-Module $moduleBinary", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgeRelease @invokeParams", entryPoint, StringComparison.Ordinal);
        Assert.Contains("if ($null -eq $result)", entryPoint, StringComparison.Ordinal);
        var shouldProcessIndex = entryPoint.IndexOf("$PSCmdlet.ShouldProcess", StringComparison.Ordinal);
        var bootstrapIndex = entryPoint.IndexOf("dotnet build $moduleProject", StringComparison.Ordinal);
        Assert.True(shouldProcessIndex >= 0 && shouldProcessIndex < bootstrapIndex);
        Assert.Contains("Success = $true", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Skipped = $true", entryPoint, StringComparison.Ordinal);
        Assert.Contains("[Alias('ModuleRunMode')]", entryPoint, StringComparison.Ordinal);
        Assert.Contains("[Alias('CertificateThumbprint')]", entryPoint, StringComparison.Ordinal);
        Assert.Contains("[Alias('SignIncludeBinaries')]", entryPoint, StringComparison.Ordinal);
        Assert.Contains("[Alias('DiagnosticsBaselinePath')]", entryPoint, StringComparison.Ordinal);
        Assert.Contains("[Alias('FailOnNewDiagnostics')]", entryPoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Self_build_has_one_canonical_entry_point_and_json_recipe()
    {
        var repoRoot = RepoRootLocator.Find();
        var entryPointPath = Path.Combine(repoRoot, "Build", "Build-Project.ps1");
        var scriptAst = Parser.ParseFile(entryPointPath, out _, out ParseError[] parseErrors);

        Assert.Empty(parseErrors);
        Assert.True(File.Exists(Path.Combine(repoRoot, "powerforge.json")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "Build", "Build-Module.ps1")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "Build", "Build-Release.ps1")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "Module", "Build", "Build-ModuleSelf.ps1")));

        foreach (var consumerPath in new[]
        {
            Path.Combine(repoRoot, ".github", "workflows", "BuildModule.yml"),
            Path.Combine(repoRoot, ".github", "workflows", "private-gallery-live-validation.yml"),
            Path.Combine(repoRoot, "README.MD"),
            Path.Combine(repoRoot, "Docs", "PSPublishModule.UnifiedModuleProjectRelease.md")
        })
        {
            Assert.DoesNotContain(
                @".\Build\Build-Module.ps1",
                File.ReadAllText(consumerPath),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Publish_entry_point_coordinates_module_nuget_and_native_tool_releases()
    {
        var repoRoot = RepoRootLocator.Find();
        var projectWrapperScript = File.ReadAllText(Path.Combine(repoRoot, "Build", "Build-Project.ps1"));
        var releaseConfig = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.json"));
        var moduleConfig = File.ReadAllText(Path.Combine(repoRoot, "powerforge.json"));

        Assert.Contains("$invokeParams.PublishNuget = $true", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("$invokeParams.ModuleSignModule = $true", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("[switch] $ModuleNoDotnetBuild", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("$invokeParams.ModuleRunMode = if ($Publish) { 'Publish' } else { $RunMode }", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("$Publish -or $RunMode -eq 'Publish' -or $PublishNuget", projectWrapperScript, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "$invokeParams.ModuleRunMode = if ($Publish -or $PublishNuget -or $PublishProjectGitHub)",
            projectWrapperScript,
            StringComparison.Ordinal);
        Assert.Contains("\"IncludesPackages\": false", releaseConfig, StringComparison.Ordinal);
        using var releaseDocument = JsonDocument.Parse(releaseConfig);
        var releaseRoot = releaseDocument.RootElement;
        var module = releaseRoot.GetProperty("Module");
        Assert.Equal("powerforge.json", module.GetProperty("ConfigPath").GetString());
        Assert.False(module.TryGetProperty("ScriptPath", out _));
        Assert.Equal("Module/PSPublishModule.psd1", module.GetProperty("ManifestPath").GetString());
        Assert.True(module.GetProperty("SynchronizeVersionWithPackages").GetBoolean());
        Assert.Equal("PowerForge", module.GetProperty("VersionPrimaryProject").GetString());
        Assert.Equal("3.0.X", module.GetProperty("ModuleVersion").GetString());
        Assert.True(releaseRoot.GetProperty("Packages").GetProperty("AlignPackageVersions").GetBoolean());
        Assert.False(releaseRoot.GetProperty("Packages").GetProperty("PublishGitHub").GetBoolean());
        Assert.False(releaseRoot.GetProperty("Tools").GetProperty("GitHub").GetProperty("Publish").GetBoolean());
        var unifiedGitHub = releaseRoot.GetProperty("GitHub");
        Assert.True(unifiedGitHub.GetProperty("Publish").GetBoolean());
        Assert.Equal("Module", unifiedGitHub.GetProperty("VersionSource").GetString());
        Assert.Equal("v{Version}", unifiedGitHub.GetProperty("TagTemplate").GetString());
        using var moduleDocument = JsonDocument.Parse(moduleConfig);
        var unpacked = moduleDocument.RootElement.GetProperty("Segments")
            .EnumerateArray()
            .Single(segment => string.Equals(
                segment.GetProperty("Type").GetString(),
                "Unpacked",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            "Modules",
            unpacked.GetProperty("Configuration")
                .GetProperty("RequiredModules")
                .GetProperty("Path")
                .GetString());
        var segments = moduleDocument.RootElement.GetProperty("Segments").EnumerateArray().ToArray();
        Assert.Contains(segments, segment => segment.GetProperty("Type").GetString() == "Manifest");
        Assert.Contains(segments, segment => segment.GetProperty("Type").GetString() == "BuildLibraries");
        Assert.Contains(segments, segment => segment.GetProperty("Type").GetString() == "GalleryNuget");
        Assert.Contains(segments, segment => segment.GetProperty("Type").GetString() == "Packed");
        Assert.DoesNotContain(segments, segment => segment.GetProperty("Type").GetString() == "GitHubNuget");
    }

    [Fact]
    public void Module_exports_embedded_dependency_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in EmbeddedDependencyCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Module_exports_dsl_helper_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in DslHelperCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Module_exports_managed_module_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in ManagedModuleCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Module_exports_managed_script_file_info_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in ManagedScriptFileInfoCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Module_does_not_export_unreleased_module_state_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in UnreleasedModuleStateCmdlets)
        {
            Assert.DoesNotContain($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.DoesNotContain($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Module_exports_documentation_cmdlets_and_aliases()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in DocumentationCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }

        foreach (var alias in DocumentationAliases)
        {
            Assert.Contains($"'{alias}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{alias}'", bootstrapperText, StringComparison.Ordinal);
        }
    }
}
