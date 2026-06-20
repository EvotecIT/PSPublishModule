using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Submits a prepared App Store Connect Distribution version to App Review.
/// </summary>
[Cmdlet(VerbsLifecycle.Submit, "AppStoreConnectVersionForReview", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectReviewSubmissionResult))]
public sealed class SubmitAppStoreConnectVersionForReviewCommand : PSCmdlet
{
    /// <summary>Issuer ID from App Store Connect API keys.</summary>
    [Parameter(Mandatory = true)] public string IssuerId { get; set; } = string.Empty;

    /// <summary>Key ID associated with the private key.</summary>
    [Parameter(Mandatory = true)] public string KeyId { get; set; } = string.Empty;

    /// <summary>Private key text in PEM format.</summary>
    [Parameter] public string? PrivateKey { get; set; }

    /// <summary>Path to a private key file in PEM format.</summary>
    [Parameter] public string? PrivateKeyPath { get; set; }

    /// <summary>Token lifetime in minutes, up to 20.</summary>
    [Parameter] public int TokenLifetimeMinutes { get; set; } = 15;

    /// <summary>App Store Connect app id.</summary>
    [Parameter(Mandatory = true)] public string AppId { get; set; } = string.Empty;

    /// <summary>App Store marketing version.</summary>
    [Parameter(Mandatory = true)] public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number expected on the Distribution version.</summary>
    [Parameter(Mandatory = true)] public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the Distribution version.</summary>
    [Parameter(Mandatory = true)] public ApplePlatform Platform { get; set; }

    /// <summary>Allow submission without verifying that the requested build is selected on the Distribution version.</summary>
    [Parameter] public SwitchParameter AllowUnselectedBuild { get; set; }

    /// <summary>Allow submission when the build processing state is not VALID.</summary>
    [Parameter] public SwitchParameter AllowUnprocessedBuild { get; set; }

    /// <summary>Skip release readiness checks before submission.</summary>
    [Parameter] public SwitchParameter SkipReadinessCheck { get; set; }

    /// <summary>Do not fail when readiness checks fail.</summary>
    [Parameter] public SwitchParameter AllowNotReady { get; set; }

    /// <summary>Submits the Distribution version to App Review.</summary>
    protected override void ProcessRecord()
    {
        var target = $"{AppId.Trim()} {Platform} {VersionString.Trim()} ({BuildNumber.Trim()})";
        if (!ShouldProcess(target, "Submit App Store Connect version to App Review"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var result = service.SubmitAsync(new AppStoreConnectReviewSubmissionRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platform = Platform,
            RequireSelectedBuild = !AllowUnselectedBuild.IsPresent,
            RequireValidBuild = !AllowUnprocessedBuild.IsPresent,
            CheckReadiness = !SkipReadinessCheck.IsPresent,
            RequireReady = !AllowNotReady.IsPresent
        }).GetAwaiter().GetResult();

        WriteObject(result);
    }
}
