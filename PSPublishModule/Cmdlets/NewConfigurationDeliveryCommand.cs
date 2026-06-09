using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Configures delivery metadata for bundling and installing internal docs/examples.
/// </summary>
/// <remarks>
/// <para>
/// Delivery configuration is used to bundle “internals” (docs, examples, tools, configuration files) into a module and optionally
/// generate public helper commands (Install-&lt;ModuleName&gt; / Update-&lt;ModuleName&gt;) that can copy these files to a target folder.
/// </para>
/// <para>
/// This is intended for “script packages” where the module contains additional artifacts that should be deployed alongside it.
/// </para>
/// <para>
/// Merge behavior for generated delivery commands can be fine-tuned with <see cref="PreservePaths"/> and
/// <see cref="OverwritePaths"/> so selected relative paths keep local changes or are refreshed during updates.
/// </para>
/// </remarks>
/// <example>
/// <summary>Bundle Internals and generate Install/Update commands</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationDelivery -Enable -InternalsPath 'Internals' -IncludeRootReadme -IncludeRootChangelog -GenerateInstallCommand -GenerateUpdateCommand -Sign</code>
/// <para>Generates public Install/Update helpers, bundles README/CHANGELOG into the module, and requests signing for bundled internals during build.</para>
/// </example>
/// <example>
/// <summary>Configure repository-backed docs display</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationDelivery -Enable -RepositoryPaths 'docs' -RepositoryBranch 'main' -DocumentationOrder '01-Intro.md','02-HowTo.md'</code>
/// <para>Helps modules expose docs from a repository path in a consistent order.</para>
/// </example>
/// <example>
/// <summary>Configure merge policies and custom helper names</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationDelivery -Enable -GenerateInstallCommand -GenerateUpdateCommand -InstallCommandName 'Install-ContosoToolkit' -UpdateCommandName 'Update-ContosoToolkit' -PreservePaths 'Config/**','Data/LocalSettings.json' -OverwritePaths 'Bin/**','Templates/**'</code>
/// <para>Generates custom delivery helpers and preserves selected local files while refreshing binaries and templates during merge installs.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDelivery")]
public sealed class NewConfigurationDeliveryCommand : PSCmdlet
{
    /// <summary>Enables delivery metadata emission.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Relative path inside the module that contains internal deliverables.</summary>
    [Parameter] public string InternalsPath { get; set; } = "Internals";

    /// <summary>
    /// When set, requests signing for files under <see cref="InternalsPath"/> using the configured module signing settings/certificate.
    /// </summary>
    [Parameter] public SwitchParameter Sign { get; set; }

    /// <summary>Include module root README.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootReadme { get; set; }

    /// <summary>Include module root CHANGELOG.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootChangelog { get; set; }

    /// <summary>Include module root LICENSE.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootLicense { get; set; }

    /// <summary>Where to bundle README.* within the built module.</summary>
    [Parameter] public PowerForge.DeliveryBundleDestination ReadmeDestination { get; set; } = PowerForge.DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle CHANGELOG.* within the built module.</summary>
    [Parameter] public PowerForge.DeliveryBundleDestination ChangelogDestination { get; set; } = PowerForge.DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle LICENSE.* within the built module.</summary>
    [Parameter] public PowerForge.DeliveryBundleDestination LicenseDestination { get; set; } = PowerForge.DeliveryBundleDestination.Internals;

    /// <summary>Important links (Title/Url). Accepts legacy hashtable array (@{ Title='..'; Url='..' }) or <see cref="DeliveryImportantLink"/>[].</summary>
    [Parameter]
    [DeliveryImportantLinksTransformation]
    public DeliveryImportantLink[]? ImportantLinks { get; set; }

    /// <summary>Text lines shown to users after Install-ModuleDocumentation completes.</summary>
    [Parameter] public string[]? IntroText { get; set; }

    /// <summary>Text lines with upgrade instructions shown when requested.</summary>
    [Parameter] public string[]? UpgradeText { get; set; }

    /// <summary>Relative path (within the module root) to a Markdown/text file to use as Intro content.</summary>
    [Parameter] public string? IntroFile { get; set; }

    /// <summary>Relative path (within the module root) to a Markdown/text file to use for Upgrade instructions.</summary>
    [Parameter] public string? UpgradeFile { get; set; }

    /// <summary>One or more repository-relative paths from which to display remote documentation files.</summary>
    [Parameter] public string[]? RepositoryPaths { get; set; }

    /// <summary>Optional branch name to use when fetching remote documentation.</summary>
    [Parameter] public string? RepositoryBranch { get; set; }

    /// <summary>Optional file-name order for Internals\\Docs when rendering documentation.</summary>
    [Parameter] public string[]? DocumentationOrder { get; set; }

    /// <summary>
    /// Optional wildcard patterns (relative to Internals) that should be preserved during merge installs by generated Install-/Update- helpers.
    /// Example: <c>Config/**</c>.
    /// </summary>
    [Parameter] public string[]? PreservePaths { get; set; }

    /// <summary>
    /// Optional wildcard patterns (relative to Internals) that should be overwritten during merge installs by generated Install-/Update- helpers.
    /// Example: <c>Artefacts/**</c>.
    /// </summary>
    [Parameter] public string[]? OverwritePaths { get; set; }

    /// <summary>
    /// When set, generates a public Install-&lt;ModuleName&gt; helper function during build that copies Internals to a destination folder.
    /// </summary>
    [Parameter] public SwitchParameter GenerateInstallCommand { get; set; }

    /// <summary>
    /// When set, generates a public Update-&lt;ModuleName&gt; helper function during build that delegates to the install command.
    /// </summary>
    [Parameter] public SwitchParameter GenerateUpdateCommand { get; set; }

    /// <summary>
    /// Optional override name for the generated install command. When empty, defaults to Install-&lt;ModuleName&gt;.
    /// </summary>
    [Parameter] public string? InstallCommandName { get; set; }

    /// <summary>
    /// Optional override name for the generated update command. When empty, defaults to Update-&lt;ModuleName&gt;.
    /// </summary>
    [Parameter] public string? UpdateCommandName { get; set; }

    /// <summary>Emits delivery configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var settings = new DeliveryConfigurationFactory().Create(new DeliveryConfigurationRequest
        {
            Enable = Enable.IsPresent,
            Sign = Sign.IsPresent,
            InternalsPath = InternalsPath,
            IncludeRootReadme = IncludeRootReadme.IsPresent,
            IncludeRootChangelog = IncludeRootChangelog.IsPresent,
            IncludeRootLicense = IncludeRootLicense.IsPresent,
            ReadmeDestination = ReadmeDestination,
            ChangelogDestination = ChangelogDestination,
            LicenseDestination = LicenseDestination,
            ImportantLinks = ImportantLinks,
            IntroText = IntroText,
            UpgradeText = UpgradeText,
            IntroFile = IntroFile,
            UpgradeFile = UpgradeFile,
            RepositoryPaths = RepositoryPaths,
            RepositoryBranch = RepositoryBranch,
            DocumentationOrder = DocumentationOrder,
            PreservePaths = PreservePaths,
            OverwritePaths = OverwritePaths,
            GenerateInstallCommand = GenerateInstallCommand.IsPresent,
            GenerateUpdateCommand = GenerateUpdateCommand.IsPresent,
            InstallCommandName = InstallCommandName,
            UpdateCommandName = UpdateCommandName
        });

        if (settings is not null)
            WriteObject(settings);
    }
}
