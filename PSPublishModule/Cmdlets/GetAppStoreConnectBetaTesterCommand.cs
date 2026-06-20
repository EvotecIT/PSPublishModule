using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads App Store Connect TestFlight beta testers.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectBetaTester")]
[OutputType(typeof(AppStoreConnectBetaTesterInfo))]
public sealed class GetAppStoreConnectBetaTesterCommand : PSCmdlet
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

    /// <summary>Optional tester email filter.</summary>
    [Parameter] public string? Email { get; set; }

    /// <summary>Maximum result count.</summary>
    [Parameter] public int Limit { get; set; } = 200;

    /// <summary>Reads TestFlight beta testers.</summary>
    protected override void ProcessRecord()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var testers = client.GetBetaTestersAsync(Email, Limit).GetAwaiter().GetResult();
        WriteObject(testers, enumerateCollection: true);
    }
}
