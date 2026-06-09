using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates service recovery options for DotNet publish service targets.
/// </summary>
/// <example>
/// <summary>Create service recovery options</summary>
/// <code>New-ConfigurationDotNetServiceRecovery -Enabled -ResetPeriodSeconds 86400 -RestartDelaySeconds 60 -ApplyToNonCrashFailures</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationDotNetServiceRecovery")]
[OutputType(typeof(DotNetPublishServiceRecoveryOptions))]
public sealed class NewConfigurationDotNetServiceRecoveryCommand : PSCmdlet
{
    /// <summary>
    /// Enables applying recovery policy.
    /// </summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>
    /// Failure reset period in seconds.
    /// </summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int ResetPeriodSeconds { get; set; } = 86400;

    /// <summary>
    /// Restart delay in seconds.
    /// </summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int RestartDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Applies recovery actions for non-crash failures.
    /// </summary>
    [Parameter]
    public bool ApplyToNonCrashFailures { get; set; } = true;

    /// <summary>
    /// Policy on recovery command failures.
    /// </summary>
    [Parameter]
    public DotNetPublishPolicyMode OnFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Emits a <see cref="DotNetPublishServiceRecoveryOptions"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new DotNetPublishServiceRecoveryOptions
        {
            Enabled = Enabled.IsPresent,
            ResetPeriodSeconds = ResetPeriodSeconds,
            RestartDelaySeconds = RestartDelaySeconds,
            ApplyToNonCrashFailures = ApplyToNonCrashFailures,
            OnFailure = OnFailure
        });
    }
}

