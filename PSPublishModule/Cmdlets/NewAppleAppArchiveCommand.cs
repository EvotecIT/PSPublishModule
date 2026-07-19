using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an Apple app .xcarchive using xcodebuild.
/// </summary>
[Cmdlet(VerbsCommon.New, "AppleAppArchive", SupportsShouldProcess = true)]
[OutputType(typeof(AppleAppArchiveResult))]
public sealed class NewAppleAppArchiveCommand : PSCmdlet
{
    /// <summary>Path to the Xcode project or workspace.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Path", "FullName")]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>ProjectPath points to a workspace instead of a project.</summary>
    [Parameter]
    public SwitchParameter Workspace { get; set; }

    /// <summary>Xcode scheme to archive.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Scheme { get; set; } = string.Empty;

    /// <summary>Build configuration.</summary>
    [Parameter]
    public string Configuration { get; set; } = "Release";

    /// <summary>Apple platform used to resolve the generic destination.</summary>
    [Parameter]
    public ApplePlatform Platform { get; set; } = ApplePlatform.iOS;

    /// <summary>Optional archive destination variant, such as Mac Catalyst.</summary>
    [Parameter]
    public AppleArchiveVariant ArchiveVariant { get; set; } = AppleArchiveVariant.Default;

    /// <summary>Explicit xcodebuild destination.</summary>
    [Parameter]
    public string? Destination { get; set; }

    /// <summary>Output .xcarchive path.</summary>
    [Parameter]
    public string? ArchivePath { get; set; }

    /// <summary>Directory used for generated archive paths.</summary>
    [Parameter]
    public string? ArchiveRoot { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    [Parameter]
    public string XcodeBuild { get; set; } = "xcodebuild";

    /// <summary>Allows Xcode to create or update signing assets during archive.</summary>
    [Parameter]
    public SwitchParameter AllowProvisioningUpdates { get; set; } = true;

    /// <summary>Additional structured arguments appended to the archive command.</summary>
    [Parameter]
    public string[] AdditionalArgument { get; set; } = Array.Empty<string>();

    /// <summary>Maximum archive runtime in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 60;

    /// <summary>Creates the archive.</summary>
    protected override void ProcessRecord()
    {
        var resolvedProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
        if (!ShouldProcess(resolvedProjectPath, $"Create Apple app archive for scheme '{Scheme}'"))
            return;

        var request = new AppleAppArchiveRequest
        {
            ProjectPath = resolvedProjectPath,
            IsWorkspace = Workspace.IsPresent,
            Scheme = Scheme,
            Configuration = Configuration,
            Platform = Platform,
            ArchiveVariant = ArchiveVariant,
            Destination = Destination,
            ArchivePath = ArchivePath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArchivePath),
            ArchiveRoot = ArchiveRoot is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArchiveRoot),
            XcodeBuildExecutable = XcodeBuild,
            AllowProvisioningUpdates = AllowProvisioningUpdates.IsPresent,
            AdditionalArguments = AdditionalArgument,
            Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
        };

        var result = new AppleAppArchiveService()
            .CreateArchiveAsync(request)
            .GetAwaiter()
            .GetResult();

        if (!result.Succeeded)
            ThrowTerminatingError(CreateProcessError(result.ProcessResult, "AppleAppArchiveFailed", "xcodebuild archive failed."));

        WriteObject(result);
    }

    private static ErrorRecord CreateProcessError(ProcessRunResult result, string errorId, string message)
    {
        var detail = string.Join(Environment.NewLine, new[] { result.StdErr, result.StdOut }
            .Where(static value => !string.IsNullOrWhiteSpace(value)));
        var errorMessage = string.IsNullOrWhiteSpace(detail)
            ? $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}."
            : $"{message} ExitCode={result.ExitCode}. TimedOut={result.TimedOut}.{Environment.NewLine}{detail}";

        return new ErrorRecord(
            new InvalidOperationException(errorMessage),
            errorId,
            ErrorCategory.OperationStopped,
            result);
    }
}
