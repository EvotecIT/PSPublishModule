using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates signing defaults for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectSigning")]
[OutputType(typeof(ConfigurationProjectSigning))]
public sealed class NewConfigurationProjectSigningCommand : PSCmdlet
{
    /// <summary>
    /// Signing activation mode.
    /// </summary>
    [Parameter]
    public ConfigurationProjectSigningMode Mode { get; set; } = ConfigurationProjectSigningMode.OnDemand;

    /// <summary>
    /// Optional path to the signing tool.
    /// </summary>
    [Parameter]
    public string? ToolPath { get; set; }

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
    /// Policy when the signing tool is missing.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnMissingTool { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy when signing a file fails.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnFailure { get; set; } = DotNetPublishPolicyMode.Warn;

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
    /// Emits a <see cref="ConfigurationProjectSigning"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectSigning
        {
            Mode = Mode,
            ToolPath = NormalizeNullable(ToolPath),
            Thumbprint = NormalizeNullable(Thumbprint),
            SubjectName = NormalizeNullable(SubjectName),
            OnMissingTool = OnMissingTool,
            OnFailure = OnFailure,
            TimestampUrl = NormalizeNullable(TimestampUrl),
            Description = NormalizeNullable(Description),
            Url = NormalizeNullable(Url),
            Csp = NormalizeNullable(Csp),
            KeyContainer = NormalizeNullable(KeyContainer)
        });
    }

    private static string? NormalizeNullable(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
