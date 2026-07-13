using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tests local Xcode project version values against App Store Connect.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "AppleAppReleaseDrift")]
[OutputType(typeof(bool), typeof(AppleAppReleaseDriftReport))]
public sealed class TestAppleAppReleaseDriftCommand : AsyncPSCmdlet
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

    /// <summary>Path to a .xcodeproj directory or project.pbxproj file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProjectPath", "FullName")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>App Store Connect app id.</summary>
    [Parameter] public string? AppId { get; set; }

    /// <summary>Bundle identifier filter used when AppId is omitted.</summary>
    [Parameter] public string? BundleId { get; set; }

    /// <summary>Platform filter.</summary>
    [Parameter] public ApplePlatform? Platform { get; set; }

    /// <summary>Return the full drift report instead of a Boolean.</summary>
    [Parameter] public SwitchParameter PassThru { get; set; }

    /// <summary>Suppress warnings for drift messages.</summary>
    [Parameter] public SwitchParameter Quiet { get; set; }

    /// <summary>Tests local Xcode project version values against App Store Connect.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);

        var report = await client.TestReleaseDriftAsync(resolvedPath, AppId, BundleId, Platform, CancelToken);
        if (!Quiet.IsPresent)
        {
            foreach (var message in report.Messages)
                WriteWarning(message);
        }

        WriteObject(PassThru.IsPresent ? report : report.IsMatch);
    }
}
