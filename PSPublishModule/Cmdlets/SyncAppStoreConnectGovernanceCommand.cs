using System.Management.Automation;
using System.Linq;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Converges reviewed App Store commerce and compliance state through an approval-gated plan.</summary>
[Cmdlet(VerbsData.Sync, "AppStoreConnectGovernance", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
[OutputType(typeof(AppStoreConnectGovernanceApplyResult), typeof(AppStoreConnectGovernancePlan))]
public sealed class SyncAppStoreConnectGovernanceCommand : AsyncPSCmdlet
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

    /// <summary>Safety bound for one convergence run.</summary>
    [Parameter]
    [ValidateRange(1, 1000)]
    public int MaximumChanges { get; set; } = 500;

    /// <summary>Plans, confirms, and applies the declared state.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        var spec = new AppStoreConnectGovernanceConfiguration().Load(path);
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectGovernanceService(client);
        var plan = await service.PlanAsync(spec, CancelToken);
        if (plan.IsConverged || plan.Findings.Any(finding => finding.IsError) || plan.BlockedCount > 0)
        {
            WriteObject(plan);
            return;
        }
        if (!ShouldProcess(spec.AppId, $"Apply {plan.DriftCount} App Store commerce/compliance change(s)"))
        {
            WriteObject(plan);
            return;
        }
        WriteObject(await service.ApplyAsync(new AppStoreConnectGovernanceApplyRequest
        {
            Spec = spec,
            ConfirmApply = true,
            MaximumChanges = MaximumChanges,
            ReviewedPlan = plan
        }, CancelToken));
    }
}
