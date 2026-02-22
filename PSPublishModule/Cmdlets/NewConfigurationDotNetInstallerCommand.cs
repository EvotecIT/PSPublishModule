using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates installer configuration (MSI prepare/build) for DotNet publish DSL.
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
    /// Source publish target name used for prepare/build.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string PrepareFromTarget { get; set; } = string.Empty;

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
    /// Optional MSI signing policy.
    /// </summary>
    [Parameter]
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional MSI version policy.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiVersionOptions? Versioning { get; set; }

    /// <summary>
    /// Optional client-license injection policy.
    /// </summary>
    [Parameter]
    public DotNetPublishMsiClientLicenseOptions? ClientLicense { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishInstaller"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishInstaller
        {
            Id = Id.Trim(),
            PrepareFromTarget = PrepareFromTarget.Trim(),
            InstallerProjectId = NormalizeNullable(InstallerProjectId),
            InstallerProjectPath = NormalizeNullable(InstallerProjectPath),
            StagingPath = NormalizeNullable(StagingPath),
            ManifestPath = NormalizeNullable(ManifestPath),
            Harvest = Harvest,
            HarvestPath = NormalizeNullable(HarvestPath),
            HarvestDirectoryRefId = NormalizeNullable(HarvestDirectoryRefId),
            HarvestComponentGroupId = NormalizeNullable(HarvestComponentGroupId),
            Sign = Sign,
            Versioning = Versioning,
            ClientLicense = ClientLicense
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
