using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates or finds an App Store version and selects a processed build for Distribution.
/// </summary>
[Cmdlet(VerbsCommon.Set, "AppStoreConnectVersionBuild", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectReleasePreparationResult))]
public sealed class SetAppStoreConnectVersionBuildCommand : PSCmdlet
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

    /// <summary>App Store marketing version to create or update.</summary>
    [Parameter(Mandatory = true)] public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number to select.</summary>
    [Parameter(Mandatory = true)] public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the App Store version and build.</summary>
    [Parameter(Mandatory = true)] public ApplePlatform Platform { get; set; }

    /// <summary>Do not create the App Store version when it is missing.</summary>
    [Parameter] public SwitchParameter NoCreateVersion { get; set; }

    /// <summary>Do not attach the build to the App Store version.</summary>
    [Parameter] public SwitchParameter NoSelectBuild { get; set; }

    /// <summary>Allow selecting a build before App Store Connect reports VALID processing state.</summary>
    [Parameter] public SwitchParameter AllowUnprocessedBuild { get; set; }

    /// <summary>Creates or finds an App Store version and selects a processed build for Distribution.</summary>
    protected override void ProcessRecord()
    {
        var target = $"{AppId.Trim()} {Platform} {VersionString.Trim()} ({BuildNumber.Trim()})";
        if (!ShouldProcess(target, "Prepare App Store Connect version build"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectReleasePreparationService(client);
        var result = service.PrepareAsync(new AppStoreConnectReleasePreparationRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platform = Platform,
            CreateVersion = !NoCreateVersion.IsPresent,
            SelectBuild = !NoSelectBuild.IsPresent,
            RequireValidBuild = !AllowUnprocessedBuild.IsPresent
        }).GetAwaiter().GetResult();

        WriteObject(result);
    }
}
