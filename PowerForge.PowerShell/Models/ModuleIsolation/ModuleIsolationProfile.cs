using System;

namespace PowerForge;

/// <summary>
/// Describes how a known PowerShell module should be copied, patched, and loaded through a module-scoped AssemblyLoadContext.
/// </summary>
public sealed class ModuleIsolationProfile
{
    /// <summary>Name used by callers to select the profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>PowerShell module name resolved when no explicit path is provided.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Optional minimum module version the profile is known to support.</summary>
    public Version? MinimumVersion { get; set; }

    /// <summary>Relative script module path inside the module base that should be patched.</summary>
    public string ScriptRelativePath { get; set; } = string.Empty;

    /// <summary>Name of the generated script module inside the copied module folder.</summary>
    public string IsolatedScriptName { get; set; } = string.Empty;

    /// <summary>Optional relative module manifest path whose export contract should be preserved.</summary>
    public string ManifestRelativePath { get; set; } = string.Empty;

    /// <summary>Optional generated manifest name used when <see cref="ManifestRelativePath"/> is set.</summary>
    public string IsolatedManifestName { get; set; } = string.Empty;

    /// <summary>Number of source script lines to remove before appending the remaining script body.</summary>
    public int SourceLinesToSkip { get; set; }

    /// <summary>Whether the generated wrapper should append the remaining source script body after the loader block.</summary>
    public bool IncludeSourceScriptBody { get; set; } = true;

    /// <summary>Binary module assemblies to import from the script folder through the shared load context.</summary>
    public string[] BinaryImports { get; set; } = Array.Empty<string>();

    /// <summary>Type namespaces exposed to PowerShell type resolution from assemblies loaded in the isolated context.</summary>
    public string[] TypeAcceleratorNamespaces { get; set; } = Array.Empty<string>();

    /// <summary>Stable load-context name used for the isolated module import.</summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>Returns a built-in profile for ExchangeOnlineManagement.</summary>
    public static ModuleIsolationProfile ExchangeOnlineManagement { get; } = new()
    {
        Name = "ExchangeOnlineManagement",
        ModuleName = "ExchangeOnlineManagement",
        MinimumVersion = new Version(3, 9, 0),
        ScriptRelativePath = "netCore/ExchangeOnlineManagement.psm1",
        IsolatedScriptName = "ExchangeOnlineManagement.ALC.psm1",
        SourceLinesToSkip = 8,
        BinaryImports =
        [
            "Microsoft.Exchange.Management.RestApiClient.dll",
            "Microsoft.Exchange.Management.ExoPowershellGalleryModule.dll"
        ],
        TypeAcceleratorNamespaces =
        [
            "Microsoft.Exchange.Management.",
            "Microsoft.Online.CSE.RestApiPowerShellModule."
        ],
        ContextName = "ExchangeOnlineManagement.ALC"
    };

    /// <summary>Returns a built-in profile for MicrosoftTeams.</summary>
    public static ModuleIsolationProfile MicrosoftTeams { get; } = new()
    {
        Name = "MicrosoftTeams",
        ModuleName = "MicrosoftTeams",
        MinimumVersion = new Version(7, 8, 0),
        ScriptRelativePath = "MicrosoftTeams.psm1",
        IsolatedScriptName = "MicrosoftTeams.ALC.psm1",
        ManifestRelativePath = "MicrosoftTeams.psd1",
        IsolatedManifestName = "MicrosoftTeams.ALC.psd1",
        BinaryImports =
        [
            "netcoreapp3.1/Microsoft.TeamsCmdlets.PowerShell.Connect.dll",
            "netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll",
            "netcoreapp3.1/Microsoft.Teams.PowerShell.Module.dll",
            "netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll",
            "netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Providers.PolicyRp.dll",
            "bin/Microsoft.Teams.ConfigAPI.Cmdlets.private.dll"
        ],
        TypeAcceleratorNamespaces =
        [
            "Microsoft.Teams."
        ],
        ContextName = "MicrosoftTeams.ALC"
    };
}
