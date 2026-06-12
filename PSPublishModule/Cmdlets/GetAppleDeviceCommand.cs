using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Lists Apple devices available through xcrun devicectl.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppleDevice")]
[OutputType(typeof(AppleDeviceInfo))]
public sealed class GetAppleDeviceCommand : PSCmdlet
{
    /// <summary>Optional device name, identifier, or model filter.</summary>
    [Parameter(Position = 0)]
    public string? Device { get; set; }

    /// <summary>xcrun executable name or path.</summary>
    [Parameter]
    public string Xcrun { get; set; } = "xcrun";

    /// <summary>Include unavailable devices.</summary>
    [Parameter]
    public SwitchParameter IncludeUnavailable { get; set; }

    /// <summary>Maximum command runtime in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 1;

    /// <summary>Lists devices.</summary>
    protected override void ProcessRecord()
    {
        var result = new AppleDeviceDeploymentService()
            .GetDevicesAsync(new AppleDeviceListRequest
            {
                Device = Device,
                XcrunExecutable = Xcrun,
                IncludeUnavailable = IncludeUnavailable.IsPresent,
                Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
            })
            .GetAwaiter()
            .GetResult();

        WriteObject(result, enumerateCollection: true);
    }
}
