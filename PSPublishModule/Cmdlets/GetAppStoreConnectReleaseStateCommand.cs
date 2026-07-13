using System;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads a compact App Store Connect release state summary for App Store and TestFlight release work.
/// </summary>
[Cmdlet(VerbsCommon.Get, "AppStoreConnectReleaseState")]
[OutputType(typeof(AppStoreConnectReleaseStateResult))]
public sealed class GetAppStoreConnectReleaseStateCommand : AsyncPSCmdlet
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

    /// <summary>App Store marketing version to summarize.</summary>
    [Parameter] public string? VersionString { get; set; }

    /// <summary>Uploaded build number to summarize.</summary>
    [Parameter] public string? BuildNumber { get; set; }

    /// <summary>Apple platforms to summarize.</summary>
    [Parameter] public ApplePlatform[] Platform { get; set; } = new[] { ApplePlatform.iOS };

    /// <summary>Beta group ids to include in public-link/tester summary.</summary>
    [Parameter] public string[] BetaGroupId { get; set; } = Array.Empty<string>();

    /// <summary>Beta group names to include in public-link/tester summary.</summary>
    [Parameter] public string[] BetaGroupName { get; set; } = Array.Empty<string>();

    /// <summary>Include every beta group when no beta group filter is supplied.</summary>
    [Parameter] public SwitchParameter IncludeAllBetaGroups { get; set; }

    /// <summary>Reads App Store Connect release state.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReleaseStateService(client);
        var result = await service.GetAsync(new AppStoreConnectReleaseStateRequest
        {
            Credential = credential,
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platforms = Platform,
            BetaGroupIds = BetaGroupId,
            BetaGroupNames = BetaGroupName,
            IncludeAllBetaGroups = IncludeAllBetaGroups.IsPresent
        }, CancelToken);

        WriteObject(result);
    }
}
