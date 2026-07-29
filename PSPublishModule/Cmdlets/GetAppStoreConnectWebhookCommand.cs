using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Lists App Store Connect webhooks for an app.</summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectWebhook")]
[OutputType(typeof(AppStoreConnectWebhookInfo))]
public sealed class GetAppStoreConnectWebhookCommand : AsyncPSCmdlet
{
    /// <summary>App Store Connect API issuer id.</summary>
    [Parameter(Mandatory = true)] public string IssuerId { get; set; } = string.Empty;
    /// <summary>App Store Connect API key id.</summary>
    [Parameter(Mandatory = true)] public string KeyId { get; set; } = string.Empty;
    /// <summary>Private key text in PEM format.</summary>
    [Parameter] public string? PrivateKey { get; set; }
    /// <summary>Private key file path.</summary>
    [Parameter] public string? PrivateKeyPath { get; set; }
    /// <summary>JWT token lifetime in minutes.</summary>
    [Parameter] public int TokenLifetimeMinutes { get; set; } = 15;
    /// <summary>App Store Connect app id.</summary>
    [Parameter(Mandatory = true)] [ValidateNotNullOrEmpty] public string AppId { get; set; } = string.Empty;
    /// <summary>Maximum webhook count.</summary>
    [Parameter] [ValidateRange(1, 200)] public int Limit { get; set; } = 200;

    /// <inheritdoc />
    protected override async Task ProcessRecordAsync()
    {
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var webhooks = await client.GetWebhooksAsync(AppId, Limit, CancelToken);
        WriteObject(AppStoreConnectCommandSupport.LimitResults(webhooks, Limit), enumerateCollection: true);
    }
}
