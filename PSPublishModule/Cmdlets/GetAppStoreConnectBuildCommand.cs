using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads build information from App Store Connect.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectBuild")]
[OutputType(typeof(AppStoreConnectBuildInfo))]
public sealed class GetAppStoreConnectBuildCommand : AsyncPSCmdlet
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

    /// <summary>Build number filter.</summary>
    [Parameter] public string? BuildNumber { get; set; }

    /// <summary>Marketing version filter from the related pre-release version.</summary>
    [Parameter] public string? MarketingVersion { get; set; }

    /// <summary>Platform filter from the related pre-release version.</summary>
    [Parameter] public ApplePlatform? Platform { get; set; }

    /// <summary>Maximum result count.</summary>
    [Parameter] public int Limit { get; set; } = 20;

    /// <summary>Reads build information from App Store Connect.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var builds = await client.GetBuildsAsync(
            AppId,
            BuildNumber,
            Limit,
            MarketingVersion,
            Platform,
            CancelToken);
        WriteObject(builds, enumerateCollection: true);
    }
}
