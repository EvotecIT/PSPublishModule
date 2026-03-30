using System;
using System.Collections.Generic;
using PowerForge;

namespace PSPublishModule;

internal sealed class PowerForgeReleaseInvocationOptions
{
    public bool PlanOnly { get; set; }

    public bool ValidateOnly { get; set; }

    public bool PackagesOnly { get; set; }

    public bool ModuleOnly { get; set; }

    public bool ToolsOnly { get; set; }

    public bool? PublishNuget { get; set; }

    public bool? PublishProjectGitHub { get; set; }

    public bool? PublishToolGitHub { get; set; }

    public string? Configuration { get; set; }

    public bool? ModuleNoDotnetBuild { get; set; }

    public string? ModuleVersion { get; set; }

    public string? ModulePreReleaseTag { get; set; }

    public bool? ModuleNoSign { get; set; }

    public bool? ModuleSignModule { get; set; }

    public bool SkipWorkspaceValidation { get; set; }

    public string? WorkspaceConfigPath { get; set; }

    public string? WorkspaceProfile { get; set; }

    public string[] WorkspaceEnableFeatures { get; set; } = System.Array.Empty<string>();

    public string[] WorkspaceDisableFeatures { get; set; } = System.Array.Empty<string>();

    public bool SkipRestore { get; set; }

    public bool SkipBuild { get; set; }

    public string[] Targets { get; set; } = System.Array.Empty<string>();

    public string[] Runtimes { get; set; } = System.Array.Empty<string>();

    public string[] Frameworks { get; set; } = System.Array.Empty<string>();

    public DotNetPublishStyle[] Styles { get; set; } = System.Array.Empty<DotNetPublishStyle>();

    public PowerForgeToolReleaseFlavor[] Flavors { get; set; } = System.Array.Empty<PowerForgeToolReleaseFlavor>();

    public PowerForgeReleaseToolOutputKind[] ToolOutputs { get; set; } = System.Array.Empty<PowerForgeReleaseToolOutputKind>();

    public PowerForgeReleaseToolOutputKind[] SkipToolOutputs { get; set; } = System.Array.Empty<PowerForgeReleaseToolOutputKind>();

    public string? OutputRoot { get; set; }

    public string? StageRoot { get; set; }

    public string? ManifestJsonPath { get; set; }

    public string? ChecksumsPath { get; set; }

    public bool SkipReleaseChecksums { get; set; }

    public bool? KeepSymbols { get; set; }

    public bool? EnableSigning { get; set; }

    public string? SignProfile { get; set; }

    public string? SignToolPath { get; set; }

    public string? SignThumbprint { get; set; }

    public string? SignSubjectName { get; set; }

    public DotNetPublishPolicyMode? SignOnMissingTool { get; set; }

    public DotNetPublishPolicyMode? SignOnFailure { get; set; }

    public string? SignTimestampUrl { get; set; }

    public string? SignDescription { get; set; }

    public string? SignUrl { get; set; }

    public string? SignCsp { get; set; }

    public string? SignKeyContainer { get; set; }

    public string? PackageSignThumbprint { get; set; }

    public string? PackageSignStore { get; set; }

    public string? PackageSignTimestampUrl { get; set; }

    public Dictionary<string, string> InstallerMsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
