using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Adds an external asset bundle that is prepared before module staging.
/// </summary>
/// <remarks>
/// <para>
/// External asset bundles are for files that should be carried inside a module package but are not authored as normal
/// source files, such as offline installers, tooling archives, generated payloads, or mirrored third-party packages.
/// PowerForge downloads or copies the declared files before staging, computes SHA256 values, and writes a manifest
/// alongside the bundle.
/// </para>
/// </remarks>
/// <example>
/// <summary>Prepare an offline payload before staging</summary>
/// <code>New-ConfigurationExternalAsset -Name VendorTool -OutputPath 'Artefacts\VendorTool' -Files @(New-ConfigurationExternalAssetFile -Runtime win-x64 -FileName tool.zip -Uri 'https://example.test/tool.zip')</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationExternalAsset")]
[OutputType(typeof(ConfigurationExternalAssetSegment))]
public sealed class NewConfigurationExternalAssetCommand : PSCmdlet
{
    /// <summary>Friendly bundle name written to the generated manifest.</summary>
    [Parameter(Mandatory = true)]
    public string? Name { get; set; }

    /// <summary>Optional bundle version written to the generated manifest.</summary>
    [Parameter]
    public string? Version { get; set; }

    /// <summary>Output directory for the downloaded or copied files. Relative paths resolve from the project root.</summary>
    [Parameter(Mandatory = true)]
    public string? OutputPath { get; set; }

    /// <summary>Optional manifest path. Relative paths resolve from the project root; when omitted, manifest.json is written under OutputPath.</summary>
    [Parameter]
    public string? ManifestPath { get; set; }

    /// <summary>Optional source URI or project URL written to the generated manifest.</summary>
    [Parameter]
    public string? Source { get; set; }

    /// <summary>Optional license expression or label written to the generated manifest.</summary>
    [Parameter]
    public string? License { get; set; }

    /// <summary>Files that make up the external asset bundle.</summary>
    [Parameter(Mandatory = true)]
    public ExternalAssetFileConfiguration[]? Files { get; set; }

    /// <summary>When set, existing files are used and missing files fail the build instead of downloading or copying sources.</summary>
    [Parameter]
    public SwitchParameter SkipDownload { get; set; }

    /// <summary>When set, disables the bundle while keeping it in configuration.</summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>Emits the external asset configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationExternalAssetSegment
        {
            Configuration = new ExternalAssetConfiguration
            {
                Enabled = !Disabled.IsPresent,
                Name = Name,
                Version = Version,
                OutputPath = OutputPath,
                ManifestPath = ManifestPath,
                Source = Source,
                License = License,
                SkipDownload = SkipDownload.IsPresent,
                Files = Files ?? Array.Empty<ExternalAssetFileConfiguration>()
            }
        });
    }
}
