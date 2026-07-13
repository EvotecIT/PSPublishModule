using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Adds App Store Connect beta testers to a TestFlight beta group.
/// </summary>
[Cmdlet(VerbsCommon.Add, "AppStoreConnectBetaTesterToGroup", SupportsShouldProcess = true)]
public sealed class AddAppStoreConnectBetaTesterToGroupCommand : AsyncPSCmdlet
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

    /// <summary>Beta group id.</summary>
    [Parameter(Mandatory = true)] public string BetaGroupId { get; set; } = string.Empty;

    /// <summary>Beta tester id to add to the beta group.</summary>
    [Parameter(Mandatory = true)] public string[] BetaTesterId { get; set; } = System.Array.Empty<string>();

    /// <summary>Adds beta testers to a TestFlight beta group.</summary>
    protected override async Task ProcessRecordAsync()
    {
        if (!ShouldProcess(BetaGroupId, "Add App Store Connect beta tester(s) to beta group"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        await client.AddBetaTestersToBetaGroupAsync(BetaGroupId, BetaTesterId, CancelToken);
    }
}
