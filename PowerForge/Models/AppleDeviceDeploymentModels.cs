namespace PowerForge;

/// <summary>
/// Request to list Apple devices through xcrun devicectl.
/// </summary>
public sealed class AppleDeviceListRequest
{
    /// <summary>xcrun executable name or path.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>Optional device name, identifier, or model filter.</summary>
    public string? Device { get; set; }

    /// <summary>When true, include devices that are not currently available.</summary>
    public bool IncludeUnavailable { get; set; }

    /// <summary>Maximum command runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Apple device discovered by xcrun devicectl.
/// </summary>
public sealed class AppleDeviceInfo
{
    /// <summary>Device display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CoreDevice hostname.</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>CoreDevice identifier.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Raw availability state.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Device model string.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>True when the device state indicates devicectl can target it.</summary>
    public bool IsAvailable =>
        State.StartsWith("available", StringComparison.OrdinalIgnoreCase) ||
        State.StartsWith("connected", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Request to build an Apple app for local installation.
/// </summary>
public class AppleAppBuildRequest
{
    /// <summary>Path to the Xcode project or workspace.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>When true, ProjectPath points to a workspace instead of a project.</summary>
    public bool IsWorkspace { get; set; }

    /// <summary>Xcode scheme to build.</summary>
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Built product name. Defaults to Scheme.</summary>
    public string? ProductName { get; set; }

    /// <summary>Build configuration, typically Debug for local device deployment.</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>Apple platform used to resolve the product directory.</summary>
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Explicit xcodebuild destination.</summary>
    public string? Destination { get; set; }

    /// <summary>Physical device identifier used to build for one device.</summary>
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    public string? Device { get; set; }

    /// <summary>DerivedData path. When omitted, a unique temporary path is generated.</summary>
    public string? DerivedDataPath { get; set; }

    /// <summary>Expected app path. When omitted, DerivedData, Configuration, Platform, and ProductName are used.</summary>
    public string? AppPath { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    public string XcodeBuildExecutable { get; set; } = "xcodebuild";

    /// <summary>xcrun executable name or path, used only when resolving a device by name.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>Allows Xcode to create or update signing assets during build.</summary>
    public bool AllowProvisioningUpdates { get; set; } = true;

    /// <summary>Mirror the project root to a local folder before running xcodebuild.</summary>
    public bool UseBuildMirror { get; set; }

    /// <summary>Root directory to mirror. Defaults to the project/workspace parent.</summary>
    public string? BuildRoot { get; set; }

    /// <summary>Mirror directory used when UseBuildMirror is enabled.</summary>
    public string? BuildMirrorPath { get; set; }

    /// <summary>rsync executable name or path used for build mirroring.</summary>
    public string RsyncExecutable { get; set; } = "rsync";

    /// <summary>Additional structured arguments appended to the xcodebuild build command.</summary>
    public string[] AdditionalArguments { get; set; } = Array.Empty<string>();

    /// <summary>Maximum build runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Result of building an Apple app for local installation.
/// </summary>
public sealed class AppleAppBuildResult
{
    /// <summary>Resolved app path.</summary>
    public string AppPath { get; set; } = string.Empty;

    /// <summary>Resolved xcodebuild destination.</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>Resolved DerivedData path.</summary>
    public string DerivedDataPath { get; set; } = string.Empty;

    /// <summary>Mirror path used for the build, if any.</summary>
    public string? BuildMirrorPath { get; set; }

    /// <summary>Build process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "xcodebuild", TimeSpan.Zero, false);

    /// <summary>True when xcodebuild completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;
}

/// <summary>
/// Request to install an Apple app on a physical device.
/// </summary>
public sealed class AppleAppInstallRequest
{
    /// <summary>Physical device identifier.</summary>
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    public string? Device { get; set; }

    /// <summary>Path to the built .app bundle.</summary>
    public string AppPath { get; set; } = string.Empty;

    /// <summary>xcrun executable name or path.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>Maximum install runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Result of installing an Apple app on a physical device.
/// </summary>
public sealed class AppleAppInstallResult
{
    /// <summary>Resolved physical device identifier.</summary>
    public string DeviceIdentifier { get; set; } = string.Empty;

    /// <summary>Installed .app path.</summary>
    public string AppPath { get; set; } = string.Empty;

    /// <summary>Installed bundle identifier parsed from devicectl output, when available.</summary>
    public string? BundleIdentifier { get; set; }

    /// <summary>Device installation URL parsed from devicectl output, when available.</summary>
    public string? InstallationUrl { get; set; }

    /// <summary>Install process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "xcrun", TimeSpan.Zero, false);

    /// <summary>True when devicectl completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;
}

/// <summary>
/// Request to launch an Apple app on a physical device.
/// </summary>
public sealed class AppleAppLaunchRequest
{
    /// <summary>Physical device identifier.</summary>
    public string? DeviceIdentifier { get; set; }

    /// <summary>Device name, identifier, or model used when DeviceIdentifier is not supplied.</summary>
    public string? Device { get; set; }

    /// <summary>Bundle identifier to launch.</summary>
    public string BundleIdentifier { get; set; } = string.Empty;

    /// <summary>xcrun executable name or path.</summary>
    public string XcrunExecutable { get; set; } = "xcrun";

    /// <summary>Maximum launch runtime.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Result of launching an Apple app on a physical device.
/// </summary>
public sealed class AppleAppLaunchResult
{
    /// <summary>Resolved physical device identifier.</summary>
    public string DeviceIdentifier { get; set; } = string.Empty;

    /// <summary>Launched bundle identifier.</summary>
    public string BundleIdentifier { get; set; } = string.Empty;

    /// <summary>Launch process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "xcrun", TimeSpan.Zero, false);

    /// <summary>True when devicectl completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;

    /// <summary>True when devicectl reported that launch was rejected because the device was locked.</summary>
    public bool DeviceLocked => AppleDeviceLaunchFailureClassifier.IsDeviceLocked(ProcessResult);
}

/// <summary>
/// Request to build, install, and optionally launch an Apple app on a device.
/// </summary>
public sealed class AppleAppDeviceDeploymentRequest : AppleAppBuildRequest
{
    /// <summary>Bundle identifier used when launching after install.</summary>
    public string? BundleIdentifier { get; set; }

    /// <summary>Launch the app after a successful install.</summary>
    public bool Launch { get; set; }
}

/// <summary>
/// Result of a build/install/launch device deployment.
/// </summary>
public sealed class AppleAppDeviceDeploymentResult
{
    /// <summary>Build result.</summary>
    public AppleAppBuildResult Build { get; set; } = new();

    /// <summary>Install result, when the build succeeded.</summary>
    public AppleAppInstallResult? Install { get; set; }

    /// <summary>Launch result, when Launch was requested and install succeeded.</summary>
    public AppleAppLaunchResult? Launch { get; set; }

    /// <summary>True when build and install succeeded, and launch either succeeded or was blocked only because the device was locked.</summary>
    public bool Succeeded => Build.Succeeded && (Install?.Succeeded ?? false) && (Launch is null || Launch.Succeeded || Launch.DeviceLocked);
}
