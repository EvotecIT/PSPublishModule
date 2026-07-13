using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Requests release of an approved App Store Connect version in Pending Developer Release.
/// </summary>
[Cmdlet(VerbsData.Publish, "AppStoreConnectApprovedVersion", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectVersionReleaseResult))]
public sealed class PublishAppStoreConnectApprovedVersionCommand : AsyncPSCmdlet
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

    /// <summary>Apple platform for the App Store version.</summary>
    [Parameter(Mandatory = true)] public ApplePlatform Platform { get; set; }

    /// <summary>Allow requesting release when the version state is not Pending Developer Release.</summary>
    [Parameter] public SwitchParameter AllowNonPendingDeveloperRelease { get; set; }

    /// <summary>Requests release of an approved App Store version.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var target = $"{AppId.Trim()} {Platform} {VersionString.Trim()}";
        if (!ShouldProcess(target, "Publish approved App Store Connect version"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectVersionReleaseService(client);
        var result = await service.ReleaseAsync(new AppStoreConnectVersionReleaseRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            Platform = Platform,
            RequirePendingDeveloperRelease = !AllowNonPendingDeveloperRelease.IsPresent
        }, CancelToken);

        WriteObject(result);
    }
}
