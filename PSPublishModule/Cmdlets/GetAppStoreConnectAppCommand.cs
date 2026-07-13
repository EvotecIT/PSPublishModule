using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads app information from App Store Connect.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectApp")]
[OutputType(typeof(AppStoreConnectAppInfo))]
public sealed class GetAppStoreConnectAppCommand : AsyncPSCmdlet
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
    [Parameter(ParameterSetName = "ById", Mandatory = true)] public string? AppId { get; set; }

    /// <summary>Bundle identifier filter.</summary>
    [Parameter(ParameterSetName = "Find")] public string? BundleId { get; set; }

    /// <summary>Name filter.</summary>
    [Parameter(ParameterSetName = "Find")] public string? Name { get; set; }

    /// <summary>Platform filter.</summary>
    [Parameter(ParameterSetName = "Find")] public ApplePlatform? Platform { get; set; }

    /// <summary>Maximum result count for filtered searches.</summary>
    [Parameter(ParameterSetName = "Find")] public int Limit { get; set; } = 20;

    /// <summary>Reads app information from App Store Connect.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);

        if (ParameterSetName == "ById")
        {
            var app = await client.GetAppAsync(AppId!, CancelToken);
            if (app is not null) WriteObject(app);
            return;
        }

        var apps = await client.FindAppsAsync(BundleId, Name, Platform, Limit, CancelToken);
        WriteObject(apps, enumerateCollection: true);
    }
}
