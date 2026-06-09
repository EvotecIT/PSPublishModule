using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates signing options for DotNet publish targets and installers.
/// </summary>
/// <example>
/// <summary>Enable signing by thumbprint</summary>
/// <code>New-ConfigurationDotNetSign -Enabled -Thumbprint '0123456789ABCDEF' -OnMissingTool Fail -OnSignFailure Fail</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetSign")]
[OutputType(typeof(DotNetPublishSignOptions))]
public sealed class NewConfigurationDotNetSignCommand : PSCmdlet
{
    /// <summary>
    /// Enables Authenticode signing.
    /// </summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>
    /// Optional path to signtool.exe.
    /// </summary>
    [Parameter]
    public string ToolPath { get; set; } = "signtool.exe";

    /// <summary>
    /// Policy applied when signing tool cannot be resolved.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnMissingTool { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy applied when a file signing operation fails.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnSignFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Optional certificate thumbprint.
    /// </summary>
    [Parameter]
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Optional certificate subject name.
    /// </summary>
    [Parameter]
    public string? SubjectName { get; set; }

    /// <summary>
    /// Optional timestamp URL.
    /// </summary>
    [Parameter]
    public string? TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>
    /// Optional signature description.
    /// </summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>
    /// Optional signature URL.
    /// </summary>
    [Parameter]
    public string? Url { get; set; }

    /// <summary>
    /// Optional CSP name.
    /// </summary>
    [Parameter]
    public string? Csp { get; set; }

    /// <summary>
    /// Optional key container name.
    /// </summary>
    [Parameter]
    public string? KeyContainer { get; set; }

    /// <summary>
    /// Emits a <see cref="DotNetPublishSignOptions"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishSignOptions
        {
            Enabled = Enabled.IsPresent,
            ToolPath = ToolPath,
            OnMissingTool = OnMissingTool,
            OnSignFailure = OnSignFailure,
            Thumbprint = NormalizeNullable(Thumbprint),
            SubjectName = NormalizeNullable(SubjectName),
            TimestampUrl = NormalizeNullable(TimestampUrl),
            Description = NormalizeNullable(Description),
            Url = NormalizeNullable(Url),
            Csp = NormalizeNullable(Csp),
            KeyContainer = NormalizeNullable(KeyContainer)
        });
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
