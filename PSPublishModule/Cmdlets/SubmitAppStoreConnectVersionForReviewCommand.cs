using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Submits a prepared App Store Connect Distribution version to App Review.
/// </summary>
[Cmdlet(VerbsLifecycle.Submit, "AppStoreConnectVersionForReview", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectReviewSubmissionResult))]
public sealed class SubmitAppStoreConnectVersionForReviewCommand : AsyncPSCmdlet
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

    /// <summary>Localization locale to check during readiness.</summary>
    [Parameter] public string Locale { get; set; } = "en-US";

    /// <summary>Screenshot display types that must have screenshots during readiness.</summary>
    [Parameter] public string[] RequiredScreenshotDisplayTypes { get; set; } = System.Array.Empty<string>();

    /// <summary>Optional screenshot sync config used to derive required screenshot display types during readiness.</summary>
    [Parameter] public string? ScreenshotConfigPath { get; set; }

    /// <summary>Minimum screenshot count for each required display type.</summary>
    [Parameter] public int MinimumScreenshotsPerSet { get; set; } = 1;

    /// <summary>Allow submission without verifying that the requested build is selected on the Distribution version.</summary>
    [Parameter] public SwitchParameter AllowUnselectedBuild { get; set; }

    /// <summary>Allow submission when the build processing state is not VALID.</summary>
    [Parameter] public SwitchParameter AllowUnprocessedBuild { get; set; }

    /// <summary>Skip release readiness checks before submission.</summary>
    [Parameter] public SwitchParameter SkipReadinessCheck { get; set; }

    /// <summary>Do not fail when readiness checks fail.</summary>
    [Parameter] public SwitchParameter AllowNotReady { get; set; }

    /// <summary>Submits the Distribution version to App Review.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var target = $"{AppId.Trim()} {Platform} {VersionString.Trim()} ({BuildNumber.Trim()})";
        if (!ShouldProcess(target, "Submit App Store Connect version to App Review"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        var screenshotSpec = ResolveScreenshotSpec();
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReviewSubmissionService(client);
        var result = await service.SubmitAsync(new AppStoreConnectReviewSubmissionRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platform = Platform,
            RequireSelectedBuild = !AllowUnselectedBuild.IsPresent,
            RequireValidBuild = !AllowUnprocessedBuild.IsPresent,
            CheckReadiness = !SkipReadinessCheck.IsPresent,
            RequireReady = !AllowNotReady.IsPresent,
            ReadinessRequest = new AppStoreConnectReleaseReadinessRequest
            {
                Locale = Locale,
                RequiredScreenshotDisplayTypes = RequiredScreenshotDisplayTypes,
                MinimumScreenshotsPerSet = MinimumScreenshotsPerSet,
                ScreenshotSpec = screenshotSpec
            }
        }, CancelToken);

        WriteObject(result);
    }

    private AppStoreConnectScreenshotSyncSpec? ResolveScreenshotSpec()
    {
        if (string.IsNullOrWhiteSpace(ScreenshotConfigPath))
            return null;

        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ScreenshotConfigPath);
        var json = File.ReadAllText(resolvedPath);
        var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"Unable to deserialize screenshot sync config: {resolvedPath}");

        var specAppId = string.IsNullOrWhiteSpace(spec.AppId) ? null : spec.AppId!.Trim();
        var specVersionString = string.IsNullOrWhiteSpace(spec.VersionString) ? null : spec.VersionString!.Trim();
        var appId = AppId.Trim();
        var versionString = VersionString.Trim();
        if (specAppId is not null &&
            !string.Equals(specAppId, appId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Screenshot config '{resolvedPath}' targets app '{spec.AppId}', not '{AppId}'.");

        if (specVersionString is not null &&
            !string.Equals(specVersionString, versionString, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Screenshot config '{resolvedPath}' targets version '{spec.VersionString}', not '{VersionString}'.");

        if (spec.Platform != Platform)
            throw new InvalidOperationException($"Screenshot config '{resolvedPath}' targets platform '{spec.Platform}', not '{Platform}'.");

        return spec;
    }
}
