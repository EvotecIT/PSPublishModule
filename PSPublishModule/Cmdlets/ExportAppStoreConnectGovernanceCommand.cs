using System;
using System.IO;
using System.Management.Automation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>Exports current Apple commerce and compliance state as a reviewable governance declaration.</summary>
[Cmdlet(VerbsData.Export, "AppStoreConnectGovernance", SupportsShouldProcess = true)]
[OutputType(typeof(FileInfo), typeof(AppStoreConnectGovernanceSpec))]
public sealed class ExportAppStoreConnectGovernanceCommand : AsyncPSCmdlet
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

    /// <summary>Exact App Store Connect app id to export.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string AppId { get; set; } = string.Empty;

    /// <summary>Destination JSON file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Replace an existing snapshot file.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Returns the declaration object instead of the output file.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Reads Apple and writes the reviewable declaration.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var outputPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        if (File.Exists(outputPath) && !Force.IsPresent)
            throw new InvalidOperationException($"Governance snapshot already exists: {outputPath}. Use -Force only after preserving reviewed edits.");
        if (!ShouldProcess(outputPath, $"Export App Store Connect governance for app {AppId}")) return;
        var keyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, keyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var snapshot = await new AppStoreConnectGovernanceService(client).SnapshotAsync(AppId, CancelToken);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var directory = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, options) + Environment.NewLine);
#if NET472
        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(temporaryPath, outputPath);
#else
        File.Move(temporaryPath, outputPath, overwrite: true);
#endif
        WriteObject(PassThru.IsPresent ? snapshot : new FileInfo(outputPath));
    }
}
