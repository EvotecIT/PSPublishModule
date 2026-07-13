using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Checks whether an App Store Connect Distribution version is ready for submission.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "AppStoreConnectReleaseReadiness")]
[OutputType(typeof(AppStoreConnectReleaseReadinessResult))]
public sealed class TestAppStoreConnectReleaseReadinessCommand : AsyncPSCmdlet
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

    /// <summary>App Store marketing version to check.</summary>
    [Parameter(Mandatory = true)] public string VersionString { get; set; } = string.Empty;

    /// <summary>Expected selected build number.</summary>
    [Parameter] public string? BuildNumber { get; set; }

    /// <summary>Apple platform for the App Store version.</summary>
    [Parameter(Mandatory = true)] public ApplePlatform Platform { get; set; }

    /// <summary>Localization locale to check.</summary>
    [Parameter] public string Locale { get; set; } = "en-US";

    /// <summary>Screenshot display types that must have screenshots.</summary>
    [Parameter] public string[] RequiredScreenshotDisplayTypes { get; set; } = System.Array.Empty<string>();

    /// <summary>Minimum screenshot count for each required display type.</summary>
    [Parameter] public int MinimumScreenshotsPerSet { get; set; } = 1;

    /// <summary>Require a selected build relationship. Enabled by default.</summary>
    [Parameter] public SwitchParameter NoRequireSelectedBuild { get; set; }

    /// <summary>Do not require the matched build to be VALID and not expired.</summary>
    [Parameter] public SwitchParameter NoRequireValidBuild { get; set; }

    /// <summary>Do not require description.</summary>
    [Parameter] public SwitchParameter NoRequireDescription { get; set; }

    /// <summary>Do not require keywords.</summary>
    [Parameter] public SwitchParameter NoRequireKeywords { get; set; }

    /// <summary>Do not require support URL.</summary>
    [Parameter] public SwitchParameter NoRequireSupportUrl { get; set; }

    /// <summary>Require marketing URL.</summary>
    [Parameter] public SwitchParameter RequireMarketingUrl { get; set; }

    /// <summary>Require promotional text.</summary>
    [Parameter] public SwitchParameter RequirePromotionalText { get; set; }

    /// <summary>Require what's new / release notes text.</summary>
    [Parameter] public SwitchParameter RequireWhatsNew { get; set; }

    /// <summary>Do not require screenshots.</summary>
    [Parameter] public SwitchParameter NoRequireScreenshots { get; set; }

    /// <summary>Do not require checked screenshot assets to be COMPLETE.</summary>
    [Parameter] public SwitchParameter NoRequireCompleteScreenshots { get; set; }

    /// <summary>Checks App Store Connect release readiness.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReleaseReadinessService(client);
        var result = await service.CheckAsync(new AppStoreConnectReleaseReadinessRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platform = Platform,
            Locale = Locale,
            RequiredScreenshotDisplayTypes = RequiredScreenshotDisplayTypes,
            MinimumScreenshotsPerSet = MinimumScreenshotsPerSet,
            RequireSelectedBuild = !NoRequireSelectedBuild.IsPresent,
            RequireValidBuild = !NoRequireValidBuild.IsPresent,
            RequireDescription = !NoRequireDescription.IsPresent,
            RequireKeywords = !NoRequireKeywords.IsPresent,
            RequireSupportUrl = !NoRequireSupportUrl.IsPresent,
            RequireMarketingUrl = RequireMarketingUrl.IsPresent,
            RequirePromotionalText = RequirePromotionalText.IsPresent,
            RequireWhatsNew = RequireWhatsNew.IsPresent,
            RequireScreenshots = !NoRequireScreenshots.IsPresent,
            RequireCompleteScreenshots = !NoRequireCompleteScreenshots.IsPresent
        }, CancelToken);

        WriteObject(result);
    }
}
