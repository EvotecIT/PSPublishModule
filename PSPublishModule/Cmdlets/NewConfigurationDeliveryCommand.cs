using System.Collections;
using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Configures delivery metadata for bundling and installing internal docs/examples.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationDelivery")]
public sealed class NewConfigurationDeliveryCommand : PSCmdlet
{
    /// <summary>Enables delivery metadata emission.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Relative path inside the module that contains internal deliverables.</summary>
    [Parameter] public string InternalsPath { get; set; } = "Internals";

    /// <summary>Include module root README.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootReadme { get; set; }

    /// <summary>Include module root CHANGELOG.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootChangelog { get; set; }

    /// <summary>Include module root LICENSE.* during installation.</summary>
    [Parameter] public SwitchParameter IncludeRootLicense { get; set; }

    /// <summary>Where to bundle README.* within the built module.</summary>
    [Parameter] public DeliveryBundleDestination ReadmeDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle CHANGELOG.* within the built module.</summary>
    [Parameter] public DeliveryBundleDestination ChangelogDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle LICENSE.* within the built module.</summary>
    [Parameter] public DeliveryBundleDestination LicenseDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>One or more key/value pairs that represent important links (Title/Url).</summary>
    [Parameter] public IDictionary[]? ImportantLinks { get; set; }

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

    /// <summary>Emits delivery configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        if (!Enable.IsPresent) return;

        var delivery = new OrderedDictionary
        {
            ["Enable"] = true,
            ["InternalsPath"] = InternalsPath,
            ["IncludeRootReadme"] = IncludeRootReadme.IsPresent,
            ["IncludeRootChangelog"] = IncludeRootChangelog.IsPresent,
            ["ReadmeDestination"] = ReadmeDestination.ToString(),
            ["ChangelogDestination"] = ChangelogDestination.ToString(),
            ["LicenseDestination"] = LicenseDestination.ToString(),
            ["IncludeRootLicense"] = IncludeRootLicense.IsPresent,
            ["ImportantLinks"] = ImportantLinks,
            ["IntroText"] = IntroText,
            ["UpgradeText"] = UpgradeText,
            ["IntroFile"] = IntroFile,
            ["UpgradeFile"] = UpgradeFile,
            ["RepositoryPaths"] = RepositoryPaths,
            ["RepositoryBranch"] = RepositoryBranch,
            ["DocumentationOrder"] = DocumentationOrder,
            ["Schema"] = "1.3"
        };

        var cfg = new OrderedDictionary
        {
            ["Type"] = "Options",
            ["Options"] = new OrderedDictionary
            {
                ["Delivery"] = delivery
            }
        };

        WriteObject(cfg);
    }
}

