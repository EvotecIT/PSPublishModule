using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Uploads an Apple app .xcarchive to App Store Connect using xcodebuild exportArchive.
/// </summary>
[Cmdlet(VerbsData.Publish, "AppleAppArchive", SupportsShouldProcess = true)]
[OutputType(typeof(AppleAppArchiveUploadResult))]
public sealed class PublishAppleAppArchiveCommand : PSCmdlet
{
    /// <summary>Path to the .xcarchive to upload.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("Path", "FullName")]
    [ValidateNotNullOrEmpty]
    public string ArchivePath { get; set; } = string.Empty;

    /// <summary>Apple developer team identifier.</summary>
    [Parameter]
    public string? TeamId { get; set; }

    /// <summary>Temporary export path used by xcodebuild.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>Path to write the generated export options plist.</summary>
    [Parameter]
    public string? ExportOptionsPlistPath { get; set; }

    /// <summary>xcodebuild executable name or path.</summary>
    [Parameter]
    public string XcodeBuild { get; set; } = "xcodebuild";

    /// <summary>Signing style passed to xcodebuild exportArchive.</summary>
    [Parameter]
    public string SigningStyle { get; set; } = "automatic";

    /// <summary>Controls whether App Store Connect manages app version and build numbers during upload.</summary>
    [Parameter]
    public SwitchParameter ManageAppVersionAndBuildNumber { get; set; }

    /// <summary>Controls whether debug symbols are uploaded.</summary>
    [Parameter]
    public SwitchParameter UploadSymbols { get; set; } = true;

    /// <summary>Controls whether xcodebuild generates App Store information.</summary>
    [Parameter]
    public SwitchParameter GenerateAppStoreInformation { get; set; } = true;

    /// <summary>Additional structured arguments appended to the export command.</summary>
    [Parameter]
    public string[] AdditionalArgument { get; set; } = Array.Empty<string>();

    /// <summary>Maximum upload runtime in minutes.</summary>
    [Parameter]
    public int TimeoutMinutes { get; set; } = 60;

    /// <summary>Uploads the archive.</summary>
    protected override void ProcessRecord()
    {
        var resolvedArchivePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArchivePath);
        if (!ShouldProcess(resolvedArchivePath, "Upload Apple app archive to App Store Connect"))
            return;

        var request = new AppleAppArchiveUploadRequest
        {
            ArchivePath = resolvedArchivePath,
            TeamId = TeamId,
            ExportPath = ExportPath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ExportPath),
            ExportOptionsPlistPath = ExportOptionsPlistPath is null ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(ExportOptionsPlistPath),
            XcodeBuildExecutable = XcodeBuild,
            SigningStyle = SigningStyle,
            ManageAppVersionAndBuildNumber = ManageAppVersionAndBuildNumber.IsPresent,
            UploadSymbols = UploadSymbols.IsPresent,
            GenerateAppStoreInformation = GenerateAppStoreInformation.IsPresent,
            AdditionalArguments = AdditionalArgument,
            Timeout = TimeSpan.FromMinutes(Math.Max(1, TimeoutMinutes))
        };

        var result = new AppleAppArchiveService()
            .UploadArchiveAsync(request)
            .GetAwaiter()
            .GetResult();

        if (!result.Succeeded)
            ThrowTerminatingError(CreateProcessError(result.ProcessResult, "AppleAppArchiveUploadFailed", "xcodebuild archive upload failed."));

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
