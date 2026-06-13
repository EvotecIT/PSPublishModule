using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Builds, installs, and optionally launches an Apple app on a physical device.
/// </summary>
[Cmdlet(VerbsData.Publish, "AppleAppToDevice", SupportsShouldProcess = true)]
[OutputType(typeof(AppleAppDeviceDeploymentResult))]
public sealed class PublishAppleAppToDeviceCommand : PSCmdlet
{
    /// <summary>Path to the Xcode project or workspace.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Path", "FullName")]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>ProjectPath points to a workspace instead of a project.</summary>
    [Parameter]
    public SwitchParameter Workspace { get; set; }

    /// <summary>Xcode scheme to build.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Built product name. Defaults to Scheme.</summary>
    [Parameter]
    public string? ProductName { get; set; }

    /// <summary>Build configuration.</summary>
    [Parameter]
    public string Configuration { get; set; } = "Debug";

    /// <summary>Apple platform used to resolve the product directory.</summary>
    [Parameter]
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Explicit xcodebuild destination.</summary>
    [Parameter]
    public string? Destination { get; set; }

    /// <summary>Physical device identifier used for deployment.</summary>
    [Parameter]
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    [Parameter]
    public string? Device { get; set; }

    /// <summary>Bundle identifier used when launching after install.</summary>
    [Parameter]
    public string? BundleIdentifier { get; set; }

    /// <summary>Launch the app after a successful install.</summary>
    [Parameter]
    public SwitchParameter Launch { get; set; }

    /// <summary>DerivedData path.</summary>
    [Parameter]
    public string? DerivedDataPath { get; set; }

    /// <summary>Expected app path.</summary>
    [Parameter]
    public string? AppPath { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    [Parameter]
    public string XcodeBuild { get; set; } = "xcodebuild";

    /// <summary>xcrun executable name or path.</summary>
    [Parameter]
    public string Xcrun { get; set; } = "xcrun";

    /// <summary>Allows Xcode to create or update signing assets during build.</summary>
    [Parameter]
    public SwitchParameter AllowProvisioningUpdates { get; set; } = true;

    /// <summary>Mirror the project root to a local folder before running xcodebuild.</summary>
    [Parameter]
    public SwitchParameter UseBuildMirror { get; set; }

    /// <summary>Root directory to mirror. Defaults to the project/workspace parent.</summary>
    [Parameter]
    public string? BuildRoot { get; set; }

    /// <summary>Mirror directory used when UseBuildMirror is enabled.</summary>
    [Parameter]
    public string? BuildMirrorPath { get; set; }

    /// <summary>rsync executable name or path used for build mirroring.</summary>
    [Parameter]
    public string Rsync { get; set; } = "rsync";

    /// <summary>Additional structured arguments appended to the xcodebuild build command.</summary>
    [Parameter]
    public string[] AdditionalArgument { get; set; } = Array.Empty<string>();

    /// <summary>Maximum runtime per stage in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 60;

    /// <summary>Deploys the app.</summary>
    protected override void ProcessRecord()
    {
        var resolvedProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
        var target = string.IsNullOrWhiteSpace(DeviceIdentifier) ? Device : DeviceIdentifier;
        if (!ShouldProcess(target ?? "Apple device", $"Build and install Apple app for scheme '{Scheme}'"))
            return;

        var result = new AppleDeviceDeploymentService()
            .DeployAsync(CreateRequest(resolvedProjectPath))
            .GetAwaiter()
            .GetResult();

        if (!result.Build.Succeeded)
            ThrowTerminatingError(AppleDeviceCommandSupport.CreateProcessError(result.Build.ProcessResult, "AppleAppBuildFailed", "xcodebuild build failed."));
        if (result.Install is not null && !result.Install.Succeeded)
            ThrowTerminatingError(AppleDeviceCommandSupport.CreateProcessError(result.Install.ProcessResult, "AppleAppInstallFailed", "devicectl install failed."));
        if (result.Launch is not null && !result.Launch.Succeeded)
            ThrowTerminatingError(AppleDeviceCommandSupport.CreateProcessError(result.Launch.ProcessResult, "AppleAppLaunchFailed", "devicectl launch failed."));

        WriteObject(result);
    }

    private AppleAppDeviceDeploymentRequest CreateRequest(string resolvedProjectPath)
        => new()
        {
            ProjectPath = resolvedProjectPath,
            IsWorkspace = Workspace.IsPresent,
            Scheme = Scheme,
            ProductName = ProductName,
            Configuration = Configuration,
            Platform = Platform,
            Destination = Destination,
            DeviceIdentifier = DeviceIdentifier,
            Device = Device,
            BundleIdentifier = BundleIdentifier,
            Launch = Launch.IsPresent,
            DerivedDataPath = DerivedDataPath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(DerivedDataPath),
            AppPath = AppPath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(AppPath),
            XcodeBuildExecutable = XcodeBuild,
            XcrunExecutable = Xcrun,
            AllowProvisioningUpdates = AllowProvisioningUpdates.IsPresent,
            UseBuildMirror = UseBuildMirror.IsPresent,
            BuildRoot = BuildRoot is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(BuildRoot),
            BuildMirrorPath = BuildMirrorPath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(BuildMirrorPath),
            RsyncExecutable = Rsync,
            AdditionalArguments = AdditionalArgument,
            Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
        };
}
