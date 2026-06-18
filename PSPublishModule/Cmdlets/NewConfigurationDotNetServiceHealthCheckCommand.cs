using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an HTTP readiness check for DotNet publish service lifecycle verification.
/// </summary>
/// <example>
/// <summary>Create a service health check</summary>
/// <code>New-ConfigurationDotNetServiceHealthCheck -Id Runtime -Uri 'http://127.0.0.1:58433/runtime' -JsonPath 'status' -ExpectedValue 'Ready'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetServiceHealthCheck")]
[OutputType(typeof(DotNetPublishServiceHealthCheck))]
public sealed class NewConfigurationDotNetServiceHealthCheckCommand : PSCmdlet
{
    /// <summary>
    /// Optional check identifier used in logs and failure messages.
    /// </summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>
    /// Absolute HTTP or HTTPS endpoint to poll.
    /// Supports service lifecycle tokens such as {serviceName}, {outputDir}, and {executablePath}.
    /// </summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Expected HTTP status code. Defaults to 200.
    /// </summary>
    [Parameter]
    [ValidateRange(100, 599)]
    public int ExpectedStatusCode { get; set; } = 200;

    /// <summary>
    /// Optional dot-separated JSON path to validate in the response body.
    /// Array indexes can be expressed as property[0].
    /// </summary>
    [Parameter]
    public string? JsonPath { get; set; }

    /// <summary>
    /// Optional expected JSON scalar value at <see cref="JsonPath"/>.
    /// When omitted, the check only verifies that the path exists.
    /// </summary>
    [Parameter]
    public string? ExpectedValue { get; set; }

    /// <summary>
    /// Maximum time in seconds to wait for the endpoint to pass.
    /// </summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Delay in milliseconds between polling attempts.
    /// </summary>
    [Parameter]
    [ValidateRange(100, int.MaxValue)]
    public int PollIntervalMilliseconds { get; set; } = 500;

    /// <summary>
    /// Policy used when the health check does not pass before timeout.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnFailure { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>
    /// Emits a <see cref="DotNetPublishServiceHealthCheck"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishServiceHealthCheck
        {
            Id = Id,
            Uri = Uri,
            ExpectedStatusCode = ExpectedStatusCode,
            JsonPath = JsonPath,
            ExpectedValue = ExpectedValue,
            TimeoutSeconds = TimeoutSeconds,
            PollIntervalMilliseconds = PollIntervalMilliseconds,
            OnFailure = OnFailure
        });
    }
}
