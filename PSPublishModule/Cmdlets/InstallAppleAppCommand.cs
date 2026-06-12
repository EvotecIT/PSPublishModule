using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs a built Apple .app bundle on a physical device.
/// </summary>
[Cmdlet(VerbsLifecycle.Install, "AppleApp", SupportsShouldProcess = true)]
[OutputType(typeof(AppleAppInstallResult))]
public sealed class InstallAppleAppCommand : PSCmdlet
{
    /// <summary>Path to the built .app bundle.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Path", "FullName")]
    [ValidateNotNullOrEmpty]
    public string AppPath { get; set; } = string.Empty;

    /// <summary>Physical device identifier.</summary>
    [Parameter]
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    [Parameter]
    public string? Device { get; set; }

    /// <summary>xcrun executable name or path.</summary>
    [Parameter]
    public string Xcrun { get; set; } = "xcrun";

    /// <summary>Maximum install runtime in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 10;

    /// <summary>Installs the app.</summary>
    protected override void ProcessRecord()
    {
        var resolvedAppPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(AppPath);
        var target = string.IsNullOrWhiteSpace(DeviceIdentifier) ? Device : DeviceIdentifier;
        if (!ShouldProcess(target ?? "Apple device", $"Install Apple app '{resolvedAppPath}'"))
            return;

        var result = new AppleDeviceDeploymentService()
            .InstallAsync(new AppleAppInstallRequest
            {
                AppPath = resolvedAppPath,
                DeviceIdentifier = DeviceIdentifier,
                Device = Device,
                XcrunExecutable = Xcrun,
                Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
            })
            .GetAwaiter()
            .GetResult();

        if (!result.Succeeded)
            ThrowTerminatingError(AppleDeviceCommandSupport.CreateProcessError(result.ProcessResult, "AppleAppInstallFailed", "devicectl install failed."));

        WriteObject(result);
    }
}
