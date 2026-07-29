using System;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Creates an App Store Connect webhook.</summary>
/// <example>
/// <code>New-AppStoreConnectWebhook -IssuerId $issuerId -KeyId $keyId -PrivateKeyPath './AuthKey.p8' -AppId '6778025328' -Name 'Build status' -Url 'https://releases.example.com/apple/webhook' -Secret 'a-strong-webhook-secret' -EventType 'BUILD_UPLOAD_STATE_UPDATED'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "AppStoreConnectWebhook", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectWebhookInfo))]
public sealed class NewAppStoreConnectWebhookCommand : AsyncPSCmdlet
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
    /// <summary>Webhook display name.</summary>
    [Parameter(Mandatory = true)] [ValidateNotNullOrEmpty] public string Name { get; set; } = string.Empty;
    /// <summary>Public HTTPS receiver URL.</summary>
    [Parameter(Mandatory = true)] [ValidateNotNullOrEmpty] public string Url { get; set; } = string.Empty;
    /// <summary>HMAC signing secret. The cmdlet never writes it to output.</summary>
    [Parameter(Mandatory = true)] [ValidateLength(16, 4096)] public string Secret { get; set; } = string.Empty;
    /// <summary>App Store Connect event types.</summary>
    [Parameter(Mandatory = true)] [ValidateNotNullOrEmpty] public string[] EventType { get; set; } = Array.Empty<string>();
    /// <summary>Whether Apple should deliver events.</summary>
    [Parameter] public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    protected override async Task ProcessRecordAsync()
    {
        if (!ShouldProcess(Url, $"Create App Store Connect webhook '{Name}'"))
            return;
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        WriteObject(await client.CreateWebhookAsync(new AppStoreConnectWebhookSpec
        {
            AppId = AppId,
            Name = Name,
            Url = Url,
            Secret = Secret,
            EventTypes = EventType,
            Enabled = Enabled
        }, CancelToken));
    }
}
