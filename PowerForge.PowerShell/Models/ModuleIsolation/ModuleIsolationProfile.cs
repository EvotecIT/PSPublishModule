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

    /// <summary>Whether copied Authenticode signature blocks should be removed from the appended source body.</summary>
    public bool RemoveSourceSignatureBlock { get; set; }

    /// <summary>Additional PowerShell script lines appended after the loader block and optional source script body.</summary>
    public string[] AdditionalScriptLines { get; set; } = Array.Empty<string>();

    /// <summary>Source script lines containing these fragments are skipped when appending the source body.</summary>
    public string[] SourceLineContainsToSkip { get; set; } = Array.Empty<string>();

    /// <summary>Dependency assemblies to load into the shared context without importing them as PowerShell modules.</summary>
    public string[] DependencyAssemblyImports { get; set; } = Array.Empty<string>();

    /// <summary>Binary module assemblies to import from the script folder through the shared load context.</summary>
    public string[] BinaryImports { get; set; } = Array.Empty<string>();

    /// <summary>Copied script-module imports that should be rewritten to use the shared load context.</summary>
    public string[] CopiedScriptBinaryImports { get; set; } = Array.Empty<string>();

    /// <summary>Additional files the profile requires but does not otherwise import directly as assemblies.</summary>
    public string[] RequiredFiles { get; set; } = Array.Empty<string>();

    /// <summary>Type namespaces exposed to PowerShell type resolution from assemblies loaded in the isolated context.</summary>
    public string[] TypeAcceleratorNamespaces { get; set; } = Array.Empty<string>();

    /// <summary>Stable load-context name used for the isolated module import.</summary>
    public string ContextName { get; set; } = string.Empty;

    /// <summary>Whether copied manifests should clear NestedModules to prevent default-loader imports.</summary>
    public bool RemoveManifestNestedModules { get; set; }

    /// <summary>Returns a built-in profile for ExchangeOnlineManagement.</summary>
    public static ModuleIsolationProfile ExchangeOnlineManagement { get; } = new()
    {
        Name = "ExchangeOnlineManagement",
        ModuleName = "ExchangeOnlineManagement",
        MinimumVersion = new Version(3, 9, 0),
        ScriptRelativePath = "netCore/ExchangeOnlineManagement.psm1",
        IsolatedScriptName = "ExchangeOnlineManagement.ALC.psm1",
        ManifestRelativePath = "ExchangeOnlineManagement.psd1",
        IsolatedManifestName = "ExchangeOnlineManagement.psd1",
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
        IncludeSourceScriptBody = false,
        AdditionalScriptLines =
        [
            "Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Microsoft.Teams.PowerShell.TeamsCmdlets.psd1')) -Force",
            "Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Microsoft.Teams.Policy.Administration.psd1')) -Force",
            "Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Microsoft.Teams.ConfigAPI.Cmdlets.psd1')) -Force"
        ],
        BinaryImports =
        [
            "netcoreapp3.1/Microsoft.TeamsCmdlets.PowerShell.Connect.dll",
            "netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll",
            "netcoreapp3.1/Microsoft.Teams.PowerShell.Module.dll",
            "netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll",
            "netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Providers.PolicyRp.dll",
            "bin/Microsoft.Teams.ConfigAPI.Cmdlets.private.dll"
        ],
        CopiedScriptBinaryImports =
        [
            "netcoreapp3.1/Microsoft.Teams.PowerShell.TeamsCmdlets.dll",
            "netcoreapp3.1/Microsoft.Teams.Policy.Administration.Cmdlets.Core.dll"
        ],
        RequiredFiles =
        [
            "Microsoft.Teams.PowerShell.TeamsCmdlets.psd1",
            "Microsoft.Teams.Policy.Administration.psd1",
            "Microsoft.Teams.ConfigAPI.Cmdlets.psd1"
        ],
        TypeAcceleratorNamespaces =
        [
            "Microsoft.Teams."
        ],
        ContextName = "MicrosoftTeams.ALC"
    };

    /// <summary>Returns a built-in profile for Microsoft.Graph.Authentication.</summary>
    public static ModuleIsolationProfile MicrosoftGraphAuthentication { get; } = new()
    {
        Name = "MicrosoftGraphAuthentication",
        ModuleName = "Microsoft.Graph.Authentication",
        MinimumVersion = new Version(2, 36, 0),
        ScriptRelativePath = "Microsoft.Graph.Authentication.psm1",
        IsolatedScriptName = "Microsoft.Graph.Authentication.ALC.psm1",
        ManifestRelativePath = "Microsoft.Graph.Authentication.psd1",
        IsolatedManifestName = "Microsoft.Graph.Authentication.ALC.psd1",
        RemoveSourceSignatureBlock = true,
        SourceLineContainsToSkip =
        [
            "Import-Module -Name $ModulePath",
            "Export-ModuleMember -Cmdlet (Get-ModuleCmdlet -ModulePath $ModulePath)"
        ],
        AdditionalScriptLines =
        [
            "Export-ModuleMember -Cmdlet @('Add-MgEnvironment', 'Connect-MgGraph', 'Disconnect-MgGraph', 'Get-MgContext', 'Get-MgEnvironment', 'Get-MgGraphOption', 'Get-MgRequestContext', 'Invoke-MgGraphRequest', 'Remove-MgEnvironment', 'Set-MgEnvironment', 'Set-MgGraphOption', 'Set-MgRequestContext') -Alias @('Connect-Graph', 'Disconnect-Graph', 'Invoke-GraphRequest', 'Invoke-MgRestMethod')"
        ],
        RemoveManifestNestedModules = true,
        DependencyAssemblyImports =
        [
            "Dependencies/Core/Azure.Core.dll",
            "Dependencies/Core/Microsoft.Graph.Core.dll",
            "Dependencies/Core/Microsoft.Identity.Client.dll",
            "Dependencies/Core/Microsoft.Identity.Client.Extensions.Msal.dll",
            "Dependencies/Core/Newtonsoft.Json.dll",
            "Dependencies/Azure.Identity.dll",
            "Dependencies/Azure.Identity.Broker.dll",
            "Dependencies/Microsoft.Bcl.AsyncInterfaces.dll",
            "Dependencies/Microsoft.Identity.Client.Broker.dll",
            "Dependencies/Microsoft.Identity.Client.NativeInterop.dll",
            "Dependencies/Microsoft.IdentityModel.Abstractions.dll",
            "Dependencies/Microsoft.Kiota.Abstractions.dll",
            "Dependencies/Microsoft.Kiota.Authentication.Azure.dll",
            "Dependencies/Microsoft.Kiota.Http.HttpClientLibrary.dll",
            "Dependencies/Microsoft.Kiota.Serialization.Form.dll",
            "Dependencies/Microsoft.Kiota.Serialization.Json.dll",
            "Dependencies/Microsoft.Kiota.Serialization.Text.dll",
            "Dependencies/System.Buffers.dll",
            "Dependencies/System.ClientModel.dll",
            "Dependencies/System.Diagnostics.DiagnosticSource.dll",
            "Dependencies/System.IO.Pipelines.dll",
            "Dependencies/System.Memory.Data.dll",
            "Dependencies/System.Memory.dll",
            "Dependencies/System.Text.Encodings.Web.dll",
            "Dependencies/System.Text.Json.dll",
            "Dependencies/System.Threading.Tasks.Extensions.dll"
        ],
        BinaryImports =
        [
            "Microsoft.Graph.Authentication.Core.dll",
            "Microsoft.Graph.Authentication.dll"
        ],
        TypeAcceleratorNamespaces =
        [
            "Microsoft.Graph."
        ],
        ContextName = "Microsoft.Graph.Authentication.ALC"
    };
}
