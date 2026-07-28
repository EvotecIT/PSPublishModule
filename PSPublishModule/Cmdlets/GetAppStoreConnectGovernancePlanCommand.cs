using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Reads Apple state and produces a non-mutating commerce and compliance drift plan.</summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectGovernancePlan")]
[OutputType(typeof(AppStoreConnectGovernancePlan))]
public sealed class GetAppStoreConnectGovernancePlanCommand : AsyncPSCmdlet
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

    /// <summary>Path to the governance JSON configuration.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Produces the remote drift plan.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        var spec = new AppStoreConnectGovernanceConfiguration().Load(path);
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        WriteObject(await new AppStoreConnectGovernanceService(client).PlanAsync(spec, CancelToken));
    }
}
