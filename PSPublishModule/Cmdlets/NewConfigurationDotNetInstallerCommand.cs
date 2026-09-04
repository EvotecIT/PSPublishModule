using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates MSI, Debian, or macOS app-bundle installer configuration for the DotNet publish DSL.
/// </summary>
/// <example>
/// <summary>Create MSI installer mapping</summary>
/// <code>New-ConfigurationDotNetInstaller -Id 'service.msi' -PrepareFromTarget 'My.Service' -InstallerProjectPath 'Installer/My.Service.wixproj' -Harvest Auto</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetInstaller")]
[OutputType(typeof(DotNetPublishInstaller))]
public sealed class NewConfigurationDotNetInstallerCommand : PSCmdlet
{
    /// <summary>
    /// Installer identifier.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Installer format. Defaults to MSI.
    /// </summary>
    [Parameter]
    public DotNetPublishInstallerKind Kind { get; set; } = DotNetPublishInstallerKind.Msi;

    /// <summary>
    /// Source publish target name used for prepare/build.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string PrepareFromTarget { get; set; } = string.Empty;

    /// <summary>
    /// Optional bundle identifier used as the installer payload source.
    /// </summary>
    [Parameter]
    public string? PrepareFromBundleId { get; set; }

    /// <summary>
    /// Optional runtime filter for installer generation.
    /// </summary>
    [Parameter]
    public string[]? Runtimes { get; set; }

    /// <summary>
    /// Optional target-framework filter for installer generation.
    /// </summary>
    [Parameter]
    public string[]? Frameworks { get; set; }

    /// <summary>
    /// Optional publish-style filter for installer generation.
    /// </summary>
    [Parameter]
    public DotNetPublishStyle[]? Styles { get; set; }

    /// <summary>
    /// Optional installer project catalog identifier.
    /// </summary>
    [Parameter]
    public string? InstallerProjectId { get; set; }

    /// <summary>
    /// Optional path to installer project file (*.wixproj).
    /// </summary>
    [Parameter]
    public string? InstallerProjectPath { get; set; }

    /// <summary>
    /// Optional PowerForge-owned installer authoring model used to generate a WiX SDK project.
    /// </summary>
    [Parameter]
    public PowerForgeInstallerDefinition? Authoring { get; set; }

    /// <summary>
    /// Optional staging path template for MSI payload.
    /// </summary>
    [Parameter]
    public string? StagingPath { get; set; }

    /// <summary>
    /// Optional manifest path template for MSI prepare output.
    /// </summary>
    [Parameter]
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Optional installer output directory template.
    /// </summary>
    [Parameter]
    public string? OutputPath { get; set; }

    /// <summary>
    /// Optional installer output file-name template.
    /// </summary>
    [Parameter]
    public string? OutputName { get; set; }

    /// <summary>
    /// Harvest behavior for payload tree.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiHarvestMode Harvest { get; set; } = DotNetPublishMsiHarvestMode.None;

    /// <summary>
    /// Optional harvest output path template.
    /// </summary>
    [Parameter]
    public string? HarvestPath { get; set; }

    /// <summary>
    /// Optional WiX directory reference id for generated harvest fragment.
    /// </summary>
    [Parameter]
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>
    /// Optional WiX component group id template for generated harvest fragment.
    /// </summary>
    [Parameter]
    public string? HarvestComponentGroupId { get; set; }

    /// <summary>
    /// Optional wildcard patterns excluded from MSI harvesting.
    /// </summary>
    [Parameter]
    public string[]? HarvestExcludePatterns { get; set; }

    /// <summary>
    /// Optional MSI signing policy.
    /// </summary>
    [Parameter]
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional named signing profile.
    /// </summary>
    [Parameter]
    public string? SignProfile { get; set; }

    /// <summary>
    /// Optional signing-profile overrides.
    /// </summary>
    [Parameter]
    public DotNetPublishSignPatch? SignOverrides { get; set; }

    /// <summary>
    /// Optional MSI version policy.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiVersionOptions? Versioning { get; set; }

    /// <summary>
    /// Optional installer-specific MSBuild properties passed to <c>msi.build</c>.
    /// </summary>
    [Parameter]
    public Hashtable? MsBuildProperties { get; set; }

    /// <summary>
    /// Optional client-license injection policy.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiClientLicenseOptions? ClientLicense { get; set; }

    /// <summary>
    /// Debian package metadata used when <see cref="Kind"/> is Debian.
    /// </summary>
    [Parameter]
    public DotNetPublishDebianOptions? Debian { get; set; }

    /// <summary>
    /// macOS app-bundle metadata used when <see cref="Kind"/> is MacApp.
    /// </summary>
    [Parameter]
    public DotNetPublishMacAppOptions? MacApp { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishInstaller"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishInstaller
        {
            Id = Id.Trim(),
            Kind = Kind,
            PrepareFromTarget = PrepareFromTarget.Trim(),
            PrepareFromBundleId = NormalizeNullable(PrepareFromBundleId),
            Runtimes = NormalizeArray(Runtimes),
            Frameworks = NormalizeArray(Frameworks),
            Styles = (Styles ?? Array.Empty<DotNetPublishStyle>()).Distinct().ToArray(),
            InstallerProjectId = NormalizeNullable(InstallerProjectId),
            InstallerProjectPath = NormalizeNullable(InstallerProjectPath),
            Authoring = Authoring,
            StagingPath = NormalizeNullable(StagingPath),
            ManifestPath = NormalizeNullable(ManifestPath),
            OutputPath = NormalizeNullable(OutputPath),
            OutputName = NormalizeNullable(OutputName),
            Harvest = Harvest,
            HarvestPath = NormalizeNullable(HarvestPath),
            HarvestDirectoryRefId = NormalizeNullable(HarvestDirectoryRefId),
            HarvestComponentGroupId = NormalizeNullable(HarvestComponentGroupId),
            HarvestExcludePatterns = NormalizeArray(HarvestExcludePatterns),
            Sign = Sign,
            SignProfile = NormalizeNullable(SignProfile),
            SignOverrides = SignOverrides,
            Versioning = Versioning,
            MsBuildProperties = NormalizeHashtable(MsBuildProperties),
            ClientLicense = ClientLicense,
            Debian = Debian,
            MacApp = MacApp
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string[] NormalizeArray(IEnumerable<string>? values) =>
        (values ?? Array.Empty<string>())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static Dictionary<string, string>? NormalizeHashtable(Hashtable? values)
    {
        if (values is null || values.Count == 0)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in values)
        {
            var key = entry.Key?.ToString()?.Trim();
            var value = entry.Value?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            result[key!] = value!;
        }

        return result.Count == 0 ? null : result;
    }
}
