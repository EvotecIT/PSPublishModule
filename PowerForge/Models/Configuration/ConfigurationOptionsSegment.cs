namespace PowerForge;

/// <summary>
/// Configuration segment that contains optional configuration groups (Signing, Delivery, etc.).
/// </summary>
public sealed class ConfigurationOptionsSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Options";

    /// <summary>Options payload.</summary>
    public ConfigurationOptions Options { get; set; } = new();
}

/// <summary>
/// Options payload for <see cref="ConfigurationOptionsSegment"/>.
/// </summary>
public sealed class ConfigurationOptions
{
    /// <summary>Signing options (code-signing).</summary>
    public SigningOptionsConfiguration? Signing { get; set; }

    /// <summary>Delivery options (Internals bundle metadata).</summary>
    public DeliveryOptionsConfiguration? Delivery { get; set; }
}

/// <summary>
/// Signing options configuration (legacy: Options.Signing).
/// </summary>
public sealed class SigningOptionsConfiguration
{
    /// <summary>When true, include Internals folder scripts in signing.</summary>
    public bool? IncludeInternals { get; set; }

    /// <summary>
    /// When false, exclude binary files from signing.
    /// Default behavior includes <c>*.dll</c> and <c>*.cat</c>.
    /// </summary>
    public bool? IncludeBinaries { get; set; }

    /// <summary>When true, include .exe files in signing.</summary>
    public bool? IncludeExe { get; set; }

    /// <summary>Custom include patterns passed to the signer.</summary>
    public string[]? Include { get; set; }

    /// <summary>Additional path substrings to exclude from signing.</summary>
    public string[]? ExcludePaths { get; set; }

    /// <summary>
    /// When true, re-sign files even if they already have a signature (overwrites existing signatures).
    /// Default behavior signs only files that are <c>NotSigned</c>.
    /// </summary>
    public bool? OverwriteSigned { get; set; }

    /// <summary>Thumbprint of a code-signing certificate from the local cert store.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Path to a PFX containing a code-signing certificate.</summary>
    public string? CertificatePFXPath { get; set; }

    /// <summary>Base64 string of a PFX containing a code-signing certificate.</summary>
    public string? CertificatePFXBase64 { get; set; }

    /// <summary>Password for the PFX provided via CertificatePFXPath or CertificatePFXBase64.</summary>
    public string? CertificatePFXPassword { get; set; }
}

/// <summary>
/// Delivery options configuration (legacy: Options.Delivery).
/// </summary>
public sealed class DeliveryOptionsConfiguration
{
    /// <summary>Enables delivery metadata.</summary>
    public bool Enable { get; set; }

    /// <summary>Relative path inside the module that contains internal deliverables.</summary>
    public string InternalsPath { get; set; } = "Internals";

    /// <summary>Include module root README.* during installation.</summary>
    public bool IncludeRootReadme { get; set; }

    /// <summary>Include module root CHANGELOG.* during installation.</summary>
    public bool IncludeRootChangelog { get; set; }

    /// <summary>Include module root LICENSE.* during installation.</summary>
    public bool IncludeRootLicense { get; set; }

    /// <summary>Where to bundle README.* within the built module.</summary>
    public DeliveryBundleDestination ReadmeDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle CHANGELOG.* within the built module.</summary>
    public DeliveryBundleDestination ChangelogDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>Where to bundle LICENSE.* within the built module.</summary>
    public DeliveryBundleDestination LicenseDestination { get; set; } = DeliveryBundleDestination.Internals;

    /// <summary>Important link entries (Title/Url).</summary>
    public DeliveryImportantLink[]? ImportantLinks { get; set; }

    /// <summary>Text lines shown to users after Install-ModuleDocumentation completes.</summary>
    public string[]? IntroText { get; set; }

    /// <summary>Text lines with upgrade instructions shown when requested.</summary>
    public string[]? UpgradeText { get; set; }

    /// <summary>Repository-relative path to a Markdown/text file to use as Intro content.</summary>
    public string? IntroFile { get; set; }

    /// <summary>Repository-relative path to a Markdown/text file to use for Upgrade instructions.</summary>
    public string? UpgradeFile { get; set; }

    /// <summary>Repository-relative paths used for remote documentation.</summary>
    public string[]? RepositoryPaths { get; set; }

    /// <summary>Optional branch name to use when fetching remote documentation.</summary>
    public string? RepositoryBranch { get; set; }

    /// <summary>Optional file-name order for Internals\\Docs when rendering documentation.</summary>
    public string[]? DocumentationOrder { get; set; }

    /// <summary>
    /// When true, generates a public <c>Install-&lt;ModuleName&gt;</c> helper function during build that copies Internals
    /// to a destination folder (script-package workflow).
    /// </summary>
    public bool GenerateInstallCommand { get; set; }

    /// <summary>
    /// When true, generates a public <c>Update-&lt;ModuleName&gt;</c> helper function during build that delegates to the install command.
    /// </summary>
    public bool GenerateUpdateCommand { get; set; }

    /// <summary>
    /// Optional override name for the generated install command. When empty, defaults to <c>Install-&lt;ModuleName&gt;</c>.
    /// </summary>
    public string? InstallCommandName { get; set; }

    /// <summary>
    /// Optional override name for the generated update command. When empty, defaults to <c>Update-&lt;ModuleName&gt;</c>.
    /// </summary>
    public string? UpdateCommandName { get; set; }

    /// <summary>Schema version string used by delivery metadata.</summary>
    public string Schema { get; set; } = "1.3";
}

/// <summary>
/// Represents a Title/Url entry in delivery metadata.
/// </summary>
public sealed class DeliveryImportantLink
{
    /// <summary>Link title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Link URL.</summary>
    public string Url { get; set; } = string.Empty;
}
