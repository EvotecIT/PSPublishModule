namespace PowerForge;

internal sealed class DeliveryConfigurationRequest
{
    public bool Enable { get; set; }
    public string InternalsPath { get; set; } = "Internals";
    public bool Sign { get; set; }
    public bool IncludeRootReadme { get; set; }
    public bool IncludeRootChangelog { get; set; }
    public bool IncludeRootLicense { get; set; }
    public DeliveryBundleDestination ReadmeDestination { get; set; } = DeliveryBundleDestination.Internals;
    public DeliveryBundleDestination ChangelogDestination { get; set; } = DeliveryBundleDestination.Internals;
    public DeliveryBundleDestination LicenseDestination { get; set; } = DeliveryBundleDestination.Internals;
    public DeliveryImportantLink[]? ImportantLinks { get; set; }
    public string[]? IntroText { get; set; }
    public string[]? UpgradeText { get; set; }
    public string? IntroFile { get; set; }
    public string? UpgradeFile { get; set; }
    public string[]? RepositoryPaths { get; set; }
    public string? RepositoryBranch { get; set; }
    public string[]? DocumentationOrder { get; set; }
    public string[]? PreservePaths { get; set; }
    public string[]? OverwritePaths { get; set; }
    public bool GenerateInstallCommand { get; set; }
    public bool GenerateUpdateCommand { get; set; }
    public string? InstallCommandName { get; set; }
    public string? UpdateCommandName { get; set; }
}
