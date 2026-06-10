using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for updating Xcode project version values during a build pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Emits an Xcode project version configuration segment consumed by <c>Invoke-ModuleBuild</c> /
/// <c>Build-Module</c>. Use it when a release pipeline should keep Apple app project versions in
/// sync with the build recipe.
/// </para>
/// </remarks>
/// <example>
/// <summary>Set an explicit app version and build number</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationXcodeProjectVersion -Path .\Tactra.xcodeproj -MarketingVersion 1.0.0 -BuildNumber 4</code>
/// <para>Updates MARKETING_VERSION and CURRENT_PROJECT_VERSION before the module build is staged.</para>
/// </example>
/// <example>
/// <summary>Use the resolved pipeline version as the app marketing version</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationXcodeProjectVersion -Path .\Tactra.xcodeproj -UseResolvedVersion -BuildNumber 4</code>
/// <para>Uses the build pipeline's resolved version for MARKETING_VERSION.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationXcodeProjectVersion", DefaultParameterSetName = "ExplicitVersion")]
public sealed class NewConfigurationXcodeProjectVersionCommand : PSCmdlet
{
    /// <summary>
    /// Path to a <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// Relative paths resolve from the pipeline project root.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProjectPath", "FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The value to assign to all <c>MARKETING_VERSION</c> entries.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = "ExplicitVersion")]
    [ValidateNotNullOrEmpty]
    public string? MarketingVersion { get; set; }

    /// <summary>
    /// Uses the pipeline resolved version as the <c>MARKETING_VERSION</c> value.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = "ResolvedVersion")]
    public SwitchParameter UseResolvedVersion { get; set; }

    /// <summary>
    /// Optional value to assign to all <c>CURRENT_PROJECT_VERSION</c> entries.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? BuildNumber { get; set; }

    /// <summary>
    /// Disable this configuration entry without removing it from a build script.
    /// </summary>
    [Parameter]
    public SwitchParameter Disabled { get; set; }

    /// <summary>
    /// Emits Xcode project version configuration for the build pipeline.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationXcodeProjectVersionSegment
        {
            Configuration = new XcodeProjectVersionConfiguration
            {
                Enabled = !Disabled.IsPresent,
                Path = Path,
                MarketingVersion = MarketingVersion,
                UseResolvedVersion = UseResolvedVersion.IsPresent,
                BuildNumber = BuildNumber
            }
        });
    }
}
