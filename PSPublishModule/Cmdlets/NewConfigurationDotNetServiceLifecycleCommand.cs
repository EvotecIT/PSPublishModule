using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates service lifecycle execution options for DotNet publish service targets.
/// </summary>
/// <example>
/// <summary>Create lifecycle options</summary>
/// <code>New-ConfigurationDotNetServiceLifecycle -Enabled -Mode Step -StopIfExists -DeleteIfExists -Install -Start -Verify</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetServiceLifecycle")]
[OutputType(typeof(DotNetPublishServiceLifecycleOptions))]
public sealed class NewConfigurationDotNetServiceLifecycleCommand : PSCmdlet
{
    /// <summary>
    /// Enables lifecycle execution.
    /// </summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>
    /// Lifecycle execution mode.
    /// </summary>
    [Parameter]
    public DotNetPublishServiceLifecycleMode Mode { get; set; } = DotNetPublishServiceLifecycleMode.Step;

    /// <summary>
    /// Stop existing service before reinstall.
    /// </summary>
    [Parameter]
    public bool StopIfExists { get; set; } = true;

    /// <summary>
    /// Delete existing service before reinstall.
    /// </summary>
    [Parameter]
    public bool DeleteIfExists { get; set; } = true;

    /// <summary>
    /// Install or reinstall service.
    /// </summary>
    [Parameter]
    public bool Install { get; set; } = true;

    /// <summary>
    /// Start service after install.
    /// </summary>
    [Parameter]
    public bool Start { get; set; } = true;

    /// <summary>
    /// Verify service status after actions.
    /// </summary>
    [Parameter]
    public bool Verify { get; set; } = true;

    /// <summary>
    /// Stop timeout in seconds.
    /// </summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int StopTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Simulates lifecycle actions.
    /// </summary>
    [Parameter]
    public SwitchParameter WhatIfMode { get; set; }

    /// <summary>
    /// Policy on non-Windows platforms.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnUnsupportedPlatform { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy on lifecycle execution failures.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnExecutionFailure { get; set; } = DotNetPublishPolicyMode.Fail;

    /// <summary>
    /// Emits a <see cref="DotNetPublishServiceLifecycleOptions"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishServiceLifecycleOptions
        {
            Enabled = Enabled.IsPresent,
            Mode = Mode,
            StopIfExists = StopIfExists,
            DeleteIfExists = DeleteIfExists,
            Install = Install,
            Start = Start,
            Verify = Verify,
            StopTimeoutSeconds = StopTimeoutSeconds,
            WhatIf = WhatIfMode.IsPresent,
            OnUnsupportedPlatform = OnUnsupportedPlatform,
            OnExecutionFailure = OnExecutionFailure
        });
    }
}

