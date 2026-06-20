using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads App Store Connect TestFlight beta groups for an app.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectBetaGroup")]
[OutputType(typeof(AppStoreConnectBetaGroupInfo))]
public sealed class GetAppStoreConnectBetaGroupCommand : PSCmdlet
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

    /// <summary>Optional beta group name filter.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Maximum result count.</summary>
    [Parameter] public int Limit { get; set; } = 200;

    /// <summary>Reads TestFlight beta groups.</summary>
    protected override void ProcessRecord()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var groups = client.GetBetaGroupsAsync(AppId, Name, Limit).GetAwaiter().GetResult();
        WriteObject(groups, enumerateCollection: true);
    }
}
