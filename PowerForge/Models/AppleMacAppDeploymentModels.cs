namespace PowerForge;

/// <summary>
/// Request to build, install, and optionally launch a macOS app locally.
/// </summary>
public sealed class AppleMacAppDeploymentRequest : AppleAppBuildRequest
{
    /// <summary>Directory that receives the built app. Defaults to /Applications.</summary>
    public string InstallRoot { get; set; } = "/Applications";

    /// <summary>Launch the installed app after replacement.</summary>
    public bool Launch { get; set; } = true;

    /// <summary>Environment variables supplied to the launched app.</summary>
    public Dictionary<string, string> LaunchEnvironment { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Arguments supplied to the launched app.</summary>
    public string[] LaunchArguments { get; set; } = Array.Empty<string>();

    /// <summary>Terminate processes running from the installed bundle before applying the launch profile.</summary>
    public bool TerminateExisting { get; set; } = true;

    /// <summary>
    /// ditto executable used to preserve the application bundle while copying.
    /// Exact-source deployment accepts only ditto or /usr/bin/ditto.
    /// </summary>
    public string DittoExecutable { get; set; } = "/usr/bin/ditto";

    /// <summary>
    /// open executable used to launch the installed app. Exact-source deployment
    /// accepts only open or /usr/bin/open.
    /// </summary>
    public string OpenExecutable { get; set; } = "/usr/bin/open";

    /// <summary>
    /// pkill executable used to terminate prior instances from the installed bundle.
    /// Exact-source deployment accepts only pkill or /usr/bin/pkill.
    /// </summary>
    public string PkillExecutable { get; set; } = "/usr/bin/pkill";
}

/// <summary>
/// Result of installing a macOS app bundle.
/// </summary>
public sealed class AppleMacAppInstallResult
{
    /// <summary>Durable source app bundle retained from the exact xcodebuild product.</summary>
    public string SourceAppPath { get; set; } = string.Empty;

    /// <summary>Final installed app bundle path.</summary>
    public string InstalledAppPath { get; set; } = string.Empty;

    /// <summary>Copy process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "/usr/bin/ditto", TimeSpan.Zero, false);

    /// <summary>True when the replacement completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Non-fatal cleanup warning after a successful replacement.</summary>
    public string? Warning { get; set; }
}

/// <summary>
/// Result of launching an installed macOS app.
/// </summary>
public sealed class AppleMacAppLaunchResult
{
    /// <summary>Installed app bundle path.</summary>
    public string AppPath { get; set; } = string.Empty;

    /// <summary>Launch process result.</summary>
    public ProcessRunResult ProcessResult { get; set; } = new(0, string.Empty, string.Empty, "/usr/bin/open", TimeSpan.Zero, false);

    /// <summary>True when open completed successfully.</summary>
    public bool Succeeded => ProcessResult.Succeeded;
}

/// <summary>
/// Result of a local macOS build/install/launch deployment.
/// </summary>
public sealed class AppleMacAppDeploymentResult
{
    /// <summary>Build result.</summary>
    public AppleAppBuildResult Build { get; set; } = new();

    /// <summary>Install result, when the build succeeded.</summary>
    public AppleMacAppInstallResult? Install { get; set; }

    /// <summary>Launch result, when requested and install succeeded.</summary>
    public AppleMacAppLaunchResult? Launch { get; set; }

    /// <summary>True when every requested stage succeeded.</summary>
    public bool Succeeded => Build.Succeeded && (Install?.Succeeded ?? false) && (Launch?.Succeeded ?? true);
}
