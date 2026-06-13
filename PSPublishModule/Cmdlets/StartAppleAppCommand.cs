using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Launches an installed Apple app on a physical device.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "AppleApp", SupportsShouldProcess = true)]
[OutputType(typeof(AppleAppLaunchResult))]
public sealed class StartAppleAppCommand : PSCmdlet
{
    /// <summary>Bundle identifier to launch.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string BundleIdentifier { get; set; } = string.Empty;

    /// <summary>Physical device identifier.</summary>
    [Parameter]
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    [Parameter]
    public string? Device { get; set; }

    /// <summary>xcrun executable name or path.</summary>
    [Parameter]
    public string Xcrun { get; set; } = "xcrun";

    /// <summary>Maximum launch runtime in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 2;

    /// <summary>Launches the app.</summary>
    protected override void ProcessRecord()
    {
        var target = string.IsNullOrWhiteSpace(DeviceIdentifier) ? Device : DeviceIdentifier;
        if (!ShouldProcess(target ?? "Apple device", $"Launch Apple app '{BundleIdentifier}'"))
            return;

        var result = new AppleDeviceDeploymentService()
            .LaunchAsync(new AppleAppLaunchRequest
            {
                BundleIdentifier = BundleIdentifier,
                DeviceIdentifier = DeviceIdentifier,
                Device = Device,
                XcrunExecutable = Xcrun,
                Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
            })
            .GetAwaiter()
            .GetResult();

        if (!result.Succeeded)
            ThrowTerminatingError(AppleDeviceCommandSupport.CreateProcessError(result.ProcessResult, "AppleAppLaunchFailed", "devicectl launch failed."));

        WriteObject(result);
    }
}
