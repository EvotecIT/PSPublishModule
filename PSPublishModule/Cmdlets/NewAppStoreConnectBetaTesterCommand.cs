using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates an App Store Connect TestFlight beta tester.
/// </summary>
[Cmdlet(VerbsCommon.New, "AppStoreConnectBetaTester", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectBetaTesterInfo))]
public sealed class NewAppStoreConnectBetaTesterCommand : PSCmdlet
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

    /// <summary>Tester email address.</summary>
    [Parameter(Mandatory = true)] public string Email { get; set; } = string.Empty;

    /// <summary>Optional first name.</summary>
    [Parameter] public string? FirstName { get; set; }

    /// <summary>Optional last name.</summary>
    [Parameter] public string? LastName { get; set; }

    /// <summary>Optional beta group ids to add the tester to during creation.</summary>
    [Parameter] public string[] BetaGroupId { get; set; } = System.Array.Empty<string>();

    /// <summary>Creates a TestFlight beta tester.</summary>
    protected override void ProcessRecord()
    {
        if (!ShouldProcess(Email, "Create App Store Connect beta tester"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var tester = client.CreateBetaTesterAsync(Email, FirstName, LastName, BetaGroupId).GetAwaiter().GetResult();
        WriteObject(tester);
    }
}
