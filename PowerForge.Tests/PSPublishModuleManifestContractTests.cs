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

    private static readonly string[] ModuleStateCmdlets =
    {
        "Get-ModuleState",
        "Get-ModuleStatePlan",
        "Invoke-ModuleState",
        "Invoke-ModuleStatePlan",
        "Test-ModuleState"
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
        Assert.Contains("$tfm = if ($Framework -ne 'auto')", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"PSPublishModule/bin/{0}/{1}/PSPublishModule.dll\" -f $Configuration, $tfm", buildScript, StringComparison.Ordinal);
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
    public void Module_exports_module_state_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psd1"));
        var bootstrapperText = File.ReadAllText(Path.Combine(repoRoot, "Module", "PSPublishModule.psm1"));

        foreach (var cmdlet in ModuleStateCmdlets)
        {
            Assert.Contains($"'{cmdlet}'", manifestText, StringComparison.Ordinal);
            Assert.Contains($"'{cmdlet}'", bootstrapperText, StringComparison.Ordinal);
        }
    }
}
