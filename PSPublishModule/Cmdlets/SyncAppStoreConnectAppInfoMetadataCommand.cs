using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Syncs localized app-level App Store information from a JSON configuration file.
/// </summary>
[Cmdlet(VerbsData.Sync, "AppStoreConnectAppInfoMetadata", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectAppInfoMetadataSyncResult))]
public sealed class SyncAppStoreConnectAppInfoMetadataCommand : AsyncPSCmdlet
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

    /// <summary>Path to the App Information metadata JSON configuration file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Syncs localized app-level App Store information.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var resolvedConfigPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        if (!ShouldProcess(resolvedConfigPath, "Sync App Store Connect App Information metadata"))
            return;

        var json = File.ReadAllText(resolvedConfigPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var spec = JsonSerializer.Deserialize<AppStoreConnectAppInfoMetadataSpec>(json, options)
            ?? throw new InvalidOperationException($"Unable to deserialize App Information metadata config: {resolvedConfigPath}");

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectAppInfoMetadataSyncService(client);
        var result = await service.SyncAsync(new AppStoreConnectAppInfoMetadataSyncRequest { Spec = spec }, CancelToken);

        WriteObject(result);
    }
}
