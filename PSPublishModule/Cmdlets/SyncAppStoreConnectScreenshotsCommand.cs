using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Syncs local screenshot folders to App Store Connect screenshot sets.
/// </summary>
[Cmdlet(VerbsData.Sync, "AppStoreConnectScreenshots", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectScreenshotSyncResult))]
public sealed class SyncAppStoreConnectScreenshotsCommand : PSCmdlet
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

    /// <summary>Path to the screenshot sync JSON configuration file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Deletes existing screenshots in each matched set before uploading local files.</summary>
    [Parameter]
    public SwitchParameter ReplaceExisting { get; set; }

    /// <summary>Syncs local screenshot folders to App Store Connect screenshot sets.</summary>
    protected override void ProcessRecord()
    {
        var resolvedConfigPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        if (!ShouldProcess(resolvedConfigPath, "Sync App Store Connect screenshots"))
            return;

        var json = File.ReadAllText(resolvedConfigPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(json, options)
            ?? throw new InvalidOperationException($"Unable to deserialize screenshot sync config: {resolvedConfigPath}");

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectScreenshotSyncService(client);
        var result = service.SyncAsync(new AppStoreConnectScreenshotSyncRequest
        {
            Spec = spec,
            ReplaceExisting = ReplaceExisting.IsPresent,
            BaseDirectory = Path.GetDirectoryName(resolvedConfigPath) ?? SessionState.Path.CurrentFileSystemLocation.Path
        }).GetAwaiter().GetResult();

        WriteObject(result);
    }
}
