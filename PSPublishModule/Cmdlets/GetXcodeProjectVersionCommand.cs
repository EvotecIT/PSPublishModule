using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads version information from an Xcode project.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>MARKETING_VERSION</c> and <c>CURRENT_PROJECT_VERSION</c> values from a
/// <c>.xcodeproj</c> directory or a raw <c>project.pbxproj</c> file.
/// </para>
/// <para>
/// The returned object includes all distinct values so release scripts can detect drift
/// across targets or configurations before uploading to App Store Connect.
/// </para>
/// </remarks>
/// <example>
/// <summary>Read version values from an Xcode project</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-XcodeProjectVersion -Path .\Tactra.xcodeproj</code>
/// <para>Returns the distinct marketing and build version values from the project file.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "XcodeProjectVersion")]
[OutputType(typeof(XcodeProjectVersionInfo))]
public sealed class GetXcodeProjectVersionCommand : PSCmdlet
{
    /// <summary>
    /// Path to a <c>.xcodeproj</c> directory or <c>project.pbxproj</c> file.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ProjectPath", "FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Reads the project version values.
    /// </summary>
    protected override void ProcessRecord()
    {
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var info = new XcodeProjectVersionEditor().Read(resolvedPath);
        WriteObject(info);
    }
}
