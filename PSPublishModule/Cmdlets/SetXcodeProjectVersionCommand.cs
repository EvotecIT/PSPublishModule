using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates version information in an Xcode project.
/// </summary>
/// <remarks>
/// <para>
/// Updates all <c>MARKETING_VERSION</c> values in a <c>.xcodeproj</c> directory or
/// raw <c>project.pbxproj</c> file. When <c>-BuildNumber</c> is provided, it also
/// updates all <c>CURRENT_PROJECT_VERSION</c> values.
/// </para>
/// <para>
/// This command intentionally edits local Xcode project metadata only. App Store
/// Connect metadata and build selection belong to higher-level Apple release commands.
/// </para>
/// </remarks>
/// <example>
/// <summary>Set an app marketing version and build number</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Set-XcodeProjectVersion -Path .\Tactra.xcodeproj -MarketingVersion 1.0.0 -BuildNumber 4 -PassThru</code>
/// <para>Updates all matching version assignments and returns the before/after summary.</para>
/// </example>
[Cmdlet(VerbsCommon.Set, "XcodeProjectVersion", SupportsShouldProcess = true)]
[OutputType(typeof(XcodeProjectVersionUpdateResult))]
public sealed class SetXcodeProjectVersionCommand : PSCmdlet
{
    /// <summary>
    /// Path to a <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ProjectPath", "FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// The value to assign to all <c>MARKETING_VERSION</c> entries.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string MarketingVersion { get; set; } = string.Empty;

    /// <summary>
    /// Optional value to assign to all <c>CURRENT_PROJECT_VERSION</c> entries.
    /// </summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? BuildNumber { get; set; }

    /// <summary>
    /// Returns the before/after update result.
    /// </summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Updates the project version values.
    /// </summary>
    protected override void ProcessRecord()
    {
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var action = string.IsNullOrWhiteSpace(BuildNumber)
            ? $"Set MARKETING_VERSION to '{MarketingVersion}'"
            : $"Set MARKETING_VERSION to '{MarketingVersion}' and CURRENT_PROJECT_VERSION to '{BuildNumber}'";

        var shouldWrite = ShouldProcess(resolvedPath, action);
        var result = new XcodeProjectVersionEditor().Update(
            resolvedPath,
            MarketingVersion,
            BuildNumber,
            whatIf: !shouldWrite);

        if (PassThru)
            WriteObject(result);
    }
}
