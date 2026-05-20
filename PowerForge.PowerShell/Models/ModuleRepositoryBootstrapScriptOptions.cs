using System;

namespace PowerForge;

internal sealed class ModuleRepositoryBootstrapScriptOptions
{
    /// <summary>Destination directory for generated onboarding files.</summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Generated bootstrap script file name.</summary>
    public string ScriptName { get; set; } = "Initialize-PrivateGallery.ps1";

    /// <summary>Generated non-secret profile JSON file name.</summary>
    public string ProfileFileName { get; set; } = "profiles.json";

    /// <summary>Profiles to include in the generated profile JSON file.</summary>
    public ModuleRepositoryProfile[] Profiles { get; set; } = Array.Empty<ModuleRepositoryProfile>();

    /// <summary>Optional module names pre-populated into the generated bootstrap script.</summary>
    public string[] InstallModules { get; set; } = Array.Empty<string>();

    /// <summary>Overwrite existing generated files.</summary>
    public bool Force { get; set; }
}
