using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for preparing an Apple app target in a release pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Emits an Apple app configuration segment consumed by <c>Invoke-ModuleBuild</c> / <c>Build-Module</c>.
/// The segment prepares local Xcode project version metadata. App Store Connect metadata is kept as
/// configuration for future read-only checks and publish/review commands.
/// </para>
/// </remarks>
/// <example>
/// <summary>Prepare an iOS app using the resolved pipeline version</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationAppleApp -Name Tactra -Platform iOS -ProjectPath .\Tactra.xcodeproj -Scheme Tactra -BundleId com.example.Tactra -UseResolvedVersion -BuildNumberPolicy IncrementExisting</code>
/// <para>Sets MARKETING_VERSION from the pipeline version and increments CURRENT_PROJECT_VERSION.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationAppleApp", DefaultParameterSetName = "ExplicitVersion")]
public sealed class NewConfigurationAppleAppCommand : PSCmdlet
{
    /// <summary>Friendly app name used in logs and reports.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Bundle identifier, e.g. com.example.Tactra.</summary>
    [Parameter]
    public string? BundleId { get; set; }

    /// <summary>Apple platform for this app target.</summary>
    [Parameter]
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>
    /// Path to a <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// Relative paths resolve from the pipeline project root.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Path", "FullName")]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Xcode scheme name for future archive/export automation.</summary>
    [Parameter]
    public string? Scheme { get; set; }

    /// <summary>Optional App Store Connect app id for future remote metadata checks.</summary>
    [Parameter]
    public string? AppStoreConnectAppId { get; set; }

    /// <summary>The value to assign to all <c>MARKETING_VERSION</c> entries.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ExplicitVersion")]
    [ValidateNotNullOrEmpty]
    public string? MarketingVersion { get; set; }

    /// <summary>Uses the pipeline resolved version as the <c>MARKETING_VERSION</c> value.</summary>
    [Parameter(Mandatory = true, ParameterSetName = "ResolvedVersion")]
    public SwitchParameter UseResolvedVersion { get; set; }

    /// <summary>Optional explicit build number.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? BuildNumber { get; set; }

    /// <summary>Build number policy used when preparing the local Xcode project.</summary>
    [Parameter]
    public AppleBuildNumberPolicy BuildNumberPolicy { get; set; } = AppleBuildNumberPolicy.KeepExisting;

    /// <summary>Disable this configuration entry without removing it from a build script.</summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>Emits Apple app configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var buildNumberPolicy = BuildNumberPolicy;
        if (!string.IsNullOrWhiteSpace(BuildNumber))
            buildNumberPolicy = AppleBuildNumberPolicy.Explicit;

        WriteObject(new ConfigurationAppleAppSegment
        {
            Configuration = new AppleAppConfiguration
            {
                Enabled = !Disabled.IsPresent,
                Name = Name,
                BundleId = BundleId,
                Platform = Platform,
                ProjectPath = ProjectPath,
                Scheme = Scheme,
                AppStoreConnectAppId = AppStoreConnectAppId,
                MarketingVersion = MarketingVersion,
                UseResolvedVersion = UseResolvedVersion.IsPresent,
                BuildNumber = BuildNumber,
                BuildNumberPolicy = buildNumberPolicy
            }
        });
    }
}
