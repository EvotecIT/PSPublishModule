using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a configuration manifest for a PowerShell module.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationManifest")]
public sealed class NewConfigurationManifestCommand : PSCmdlet
{
    /// <summary>Specifies the version of the module.</summary>
    [Parameter(Mandatory = true)] public string ModuleVersion { get; set; } = string.Empty;

    /// <summary>Specifies the module's compatible PowerShell editions.</summary>
    [Parameter] public string[] CompatiblePSEditions { get; set; } = new[] { "Desktop", "Core" };

    /// <summary>Specifies a unique identifier for the module.</summary>
    [Parameter(Mandatory = true)]
    public string Guid { get; set; } = string.Empty;

    /// <summary>Identifies the module author.</summary>
    [Parameter(Mandatory = true)] public string Author { get; set; } = string.Empty;

    /// <summary>Identifies the company or vendor who created the module.</summary>
    [Parameter] public string? CompanyName { get; set; }

    /// <summary>Specifies a copyright statement for the module.</summary>
    [Parameter] public string? Copyright { get; set; }

    /// <summary>Describes the module at a high level.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Specifies the minimum version of PowerShell this module requires.</summary>
    [Parameter] public string PowerShellVersion { get; set; } = "5.1";

    /// <summary>Specifies tags for the module.</summary>
    [Parameter] public string[]? Tags { get; set; }

    /// <summary>Specifies the URI for the module's icon.</summary>
    [Parameter] public string? IconUri { get; set; }

    /// <summary>Specifies the URI for the module's project page.</summary>
    [Parameter] public string? ProjectUri { get; set; }

    /// <summary>Specifies the minimum version of the Microsoft .NET Framework that the module requires.</summary>
    [Parameter] public string? DotNetFrameworkVersion { get; set; }

    /// <summary>Specifies the URI for the module's license.</summary>
    [Parameter] public string? LicenseUri { get; set; }

    /// <summary>When set, indicates the module requires explicit user license acceptance (PowerShellGet).</summary>
    [Parameter] public SwitchParameter RequireLicenseAcceptance { get; set; }

    /// <summary>Specifies the prerelease tag for the module.</summary>
    [Parameter]
    [Alias("PrereleaseTag")]
    public string? Prerelease { get; set; }

    /// <summary>Overrides functions to export in the module manifest.</summary>
    [Parameter] public string[]? FunctionsToExport { get; set; }

    /// <summary>Overrides cmdlets to export in the module manifest.</summary>
    [Parameter] public string[]? CmdletsToExport { get; set; }

    /// <summary>Overrides aliases to export in the module manifest.</summary>
    [Parameter] public string[]? AliasesToExport { get; set; }

    /// <summary>Specifies formatting files (.ps1xml) that run when the module is imported.</summary>
    [Parameter] public string[]? FormatsToProcess { get; set; }

    /// <summary>Emits manifest configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationManifestSegment
        {
            Configuration = new ManifestConfiguration
            {
                ModuleVersion = ModuleVersion,
                CompatiblePSEditions = CompatiblePSEditions ?? Array.Empty<string>(),
                Guid = Guid,
                Author = Author,
                CompanyName = CompanyName,
                Copyright = Copyright,
                Description = Description,
                PowerShellVersion = PowerShellVersion,
                Tags = Tags,
                IconUri = IconUri,
                ProjectUri = ProjectUri,
                DotNetFrameworkVersion = DotNetFrameworkVersion,
                LicenseUri = LicenseUri,
                RequireLicenseAcceptance = RequireLicenseAcceptance.IsPresent,
                Prerelease = Prerelease,
                FunctionsToExport = FunctionsToExport,
                CmdletsToExport = CmdletsToExport,
                AliasesToExport = AliasesToExport,
                FormatsToProcess = FormatsToProcess
            }
        });
    }
}
