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
    public void Build_recipe_does_not_generate_feature_specific_tool_modules_as_required_modules()
    {
        var repoRoot = RepoRootLocator.Find();
        var buildScriptPath = Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1");
        var buildScript = File.ReadAllText(buildScriptPath);

        foreach (var optionalModule in OptionalToolModules)
        {
            var pattern = $@"(?im)^\s*New-ConfigurationModule\b[^\r\n]*\b-Type\s+RequiredModule\b[^\r\n]*\b-Name\s+['""]?{Regex.Escape(optionalModule)}['""]?";
            Assert.DoesNotMatch(pattern, buildScript);
        }
    }

    [Fact]
    public void Self_build_forwards_selected_framework_to_json_export()
    {
        var repoRoot = RepoRootLocator.Find();
        var selfBuildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-ModuleSelf.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1"));

        Assert.Contains("Framework      = $Framework", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('auto', 'net10.0', 'net8.0')][string] $Framework = 'auto'", buildScript, StringComparison.Ordinal);
        Assert.Contains("function Resolve-ImportFramework", buildScript, StringComparison.Ordinal);
        Assert.Contains("$tfm = Resolve-ImportFramework -RequestedFramework $Framework", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"PSPublishModule/bin/{0}/{1}/PSPublishModule.dll\" -f $Configuration, $tfm", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_recipe_resolves_script_relative_defaults_after_parameter_binding()
    {
        var repoRoot = RepoRootLocator.Find();
        var buildScriptPath = Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1");
        var buildScript = File.ReadAllText(buildScriptPath);
        var scriptAst = Parser.ParseFile(buildScriptPath, out _, out ParseError[] parseErrors);

        Assert.Empty(parseErrors);

        var jsonPathParameter = Assert.Single(scriptAst.ParamBlock!.Parameters, parameter =>
            string.Equals(parameter.Name.VariablePath.UserPath, "JsonPath", StringComparison.OrdinalIgnoreCase));
        Assert.Null(jsonPathParameter.DefaultValue);
        Assert.Contains("$JsonPath = Join-Path $PSScriptRoot '../../powerforge.json'", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Self_build_defaults_to_build_gate_and_forwards_run_mode_once()
    {
        var repoRoot = RepoRootLocator.Find();
        var wrapperScript = File.ReadAllText(Path.Combine(repoRoot, "Build", "Build-Module.ps1"));
        var selfBuildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-ModuleSelf.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1"));

        Assert.Contains("[string] $RunMode = 'Build'", wrapperScript, StringComparison.Ordinal);
        Assert.Contains("RunMode        = $RunMode", wrapperScript, StringComparison.Ordinal);
        Assert.Contains("[string] $RunMode = 'Build'", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("RunMode        = $RunMode", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("New-ConfigurationGate -Mode $RunMode", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildParams.RunMode", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_entry_point_coordinates_module_nuget_and_native_tool_releases()
    {
        var repoRoot = RepoRootLocator.Find();
        var wrapperScript = File.ReadAllText(Path.Combine(repoRoot, "Build", "Build-Module.ps1"));
        var projectWrapperScript = File.ReadAllText(Path.Combine(repoRoot, "Build", "Build-Project.ps1"));
        var selfBuildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-ModuleSelf.ps1"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "Module", "Build", "Build-Module.ps1"));
        var releaseConfig = File.ReadAllText(Path.Combine(repoRoot, "Build", "release.json"));

        Assert.Contains("New-ConfigurationProjectBuild -Name 'PowerForge' -ConfigPath '../Build/release.json' -BuildBeforeModule -PublishNuget", buildScript, StringComparison.Ordinal);
        Assert.Contains("New-ConfigurationRelease -StageRoot 'Module/Artefacts/UploadReady'", buildScript, StringComparison.Ordinal);
        Assert.Contains("-PublishOrder 'NuGet', 'PowerShellGallery', 'GitHub'", buildScript, StringComparison.Ordinal);
        Assert.Contains("if ($RunMode -in @('Build', 'Publish'))", buildScript, StringComparison.Ordinal);
        Assert.Contains("$PowerForgeReleaseStage", buildScript, StringComparison.Ordinal);
        Assert.Contains("$PowerForgeUnifiedGitHubRelease", buildScript, StringComparison.Ordinal);
        Assert.Contains("-Enabled:(-not $PowerForgeUnifiedGitHubRelease)", buildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-framework', $Framework", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-run-mode', 'Publish'", selfBuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("'--publish-tool-github'", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-certificate-thumbprint', $CertificateThumbprint", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-sign-include-binaries'", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-diagnostics-baseline'", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("'--module-fail-on-new-diagnostics'", selfBuildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Unified publishing does not accept module-only override", selfBuildScript, StringComparison.Ordinal);
        Assert.Contains("[bool] $IncludeProjectPackages = $true", buildScript, StringComparison.Ordinal);
        Assert.Contains("if ($IncludeProjectPackages)", buildScript, StringComparison.Ordinal);
        Assert.Contains("RunMode        = $RunMode", wrapperScript, StringComparison.Ordinal);
        Assert.Contains("$cmdletFramework = if ($PSEdition -eq 'Desktop')", projectWrapperScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$moduleFramework = if ($PSEdition -eq 'Desktop')", projectWrapperScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if ($PSBoundParameters.ContainsKey('PublishNuget')) { $invokeParams.PublishNuget = $PublishNuget.IsPresent }", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("if ($PSBoundParameters.ContainsKey('PublishGitHub')) { $invokeParams.PublishProjectGitHub = $PublishGitHub.IsPresent }", projectWrapperScript, StringComparison.Ordinal);
        Assert.Contains("\"IncludesPackages\": true", releaseConfig, StringComparison.Ordinal);
        using var releaseDocument = JsonDocument.Parse(releaseConfig);
        var releaseRoot = releaseDocument.RootElement;
        Assert.Equal("Module/PSPublishModule.psd1", releaseRoot.GetProperty("Module").GetProperty("ManifestPath").GetString());
        Assert.False(releaseRoot.GetProperty("Packages").GetProperty("PublishGitHub").GetBoolean());
        Assert.False(releaseRoot.GetProperty("Tools").GetProperty("GitHub").GetProperty("Publish").GetBoolean());
        var unifiedGitHub = releaseRoot.GetProperty("GitHub");
        Assert.True(unifiedGitHub.GetProperty("Publish").GetBoolean());
        Assert.Equal("Module", unifiedGitHub.GetProperty("VersionSource").GetString());
        Assert.Equal("v{Version}", unifiedGitHub.GetProperty("TagTemplate").GetString());
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
