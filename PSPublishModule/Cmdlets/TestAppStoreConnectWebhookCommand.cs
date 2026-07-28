using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Sends a test delivery to an App Store Connect webhook.</summary>
[Cmdlet(VerbsDiagnostic.Test, "AppStoreConnectWebhook", SupportsShouldProcess = true)]
public sealed class TestAppStoreConnectWebhookCommand : AsyncPSCmdlet
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
    /// <summary>Webhook resource id.</summary>
    [Parameter(Mandatory = true)] [ValidateNotNullOrEmpty] public string WebhookId { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async Task ProcessRecordAsync()
    {
        if (!ShouldProcess(WebhookId, "Send App Store Connect webhook test delivery"))
            return;
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        await client.PingWebhookAsync(WebhookId, CancelToken);
        WriteObject(true);
    }
}
