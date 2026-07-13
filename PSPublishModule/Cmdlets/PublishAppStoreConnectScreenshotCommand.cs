using System;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Uploads and commits an App Store Connect screenshot file to an existing screenshot set.
/// </summary>
[Cmdlet(VerbsData.Publish, "AppStoreConnectScreenshot", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectScreenshotUploadResult))]
public sealed class PublishAppStoreConnectScreenshotCommand : AsyncPSCmdlet
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

    /// <summary>Existing App Store Connect screenshot set id.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ScreenshotSetId { get; set; } = string.Empty;

    /// <summary>Screenshot file path.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Uploads and commits the screenshot file.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        if (!ShouldProcess(resolvedPath, $"Upload App Store Connect screenshot to set '{ScreenshotSetId}'"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var result = await client.UploadScreenshotAsync(ScreenshotSetId, resolvedPath, CancelToken);
        WriteObject(result);
    }
}
