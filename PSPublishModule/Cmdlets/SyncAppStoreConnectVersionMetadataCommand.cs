using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Syncs localized App Store version metadata from a JSON configuration file.
/// </summary>
[Cmdlet(VerbsData.Sync, "AppStoreConnectVersionMetadata", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectVersionMetadataSyncResult))]
public sealed class SyncAppStoreConnectVersionMetadataCommand : AsyncPSCmdlet
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

    /// <summary>Path to the App Store version metadata JSON configuration file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Syncs localized App Store version metadata.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var resolvedConfigPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        if (!ShouldProcess(resolvedConfigPath, "Sync App Store Connect version metadata"))
            return;

        var json = File.ReadAllText(resolvedConfigPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<AppStoreConnectVersionMetadataSpec>(json, options)
            ?? throw new InvalidOperationException($"Unable to deserialize App Store version metadata config: {resolvedConfigPath}");

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectVersionMetadataSyncService(client);
        var result = await service.SyncAsync(new AppStoreConnectVersionMetadataSyncRequest
        {
            Spec = spec
        }, CancelToken);

        WriteObject(result);
    }
}
