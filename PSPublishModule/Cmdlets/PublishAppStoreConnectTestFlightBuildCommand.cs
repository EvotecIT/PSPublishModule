using System.Management.Automation;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Distributes a processed App Store Connect build to TestFlight beta groups and optional testers.
/// </summary>
[Cmdlet(VerbsData.Publish, "AppStoreConnectTestFlightBuild", SupportsShouldProcess = true)]
[OutputType(typeof(AppStoreConnectTestFlightDistributionResult))]
public sealed class PublishAppStoreConnectTestFlightBuildCommand : PSCmdlet
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

    /// <summary>App Store marketing version.</summary>
    [Parameter(Mandatory = true)] public string VersionString { get; set; } = string.Empty;

    /// <summary>Uploaded build number.</summary>
    [Parameter(Mandatory = true)] public string BuildNumber { get; set; } = string.Empty;

    /// <summary>Apple platform for the build.</summary>
    [Parameter(Mandatory = true)] public ApplePlatform Platform { get; set; }

    /// <summary>Beta group ids to receive the build.</summary>
    [Parameter] public string[] BetaGroupId { get; set; } = System.Array.Empty<string>();

    /// <summary>Beta group names to resolve and receive the build.</summary>
    [Parameter] public string[] BetaGroupName { get; set; } = System.Array.Empty<string>();

    /// <summary>Tester email addresses to create or resolve and add to target groups.</summary>
    [Parameter] public string[] TesterEmail { get; set; } = System.Array.Empty<string>();

    /// <summary>Do not create testers that do not already exist.</summary>
    [Parameter] public SwitchParameter NoCreateMissingTesters { get; set; }

    /// <summary>Allow a build whose processing state is not VALID.</summary>
    [Parameter] public SwitchParameter AllowUnprocessedBuild { get; set; }

    /// <summary>Distributes a processed build to TestFlight beta groups and optional testers.</summary>
    protected override void ProcessRecord()
    {
        var target = $"{AppId.Trim()} {Platform} {VersionString.Trim()} ({BuildNumber.Trim()})";
        if (!ShouldProcess(target, "Publish App Store Connect TestFlight build to beta groups"))
            return;

        var privateKeyPath = AppStoreConnectCommandSupport.ResolvePrivateKeyPath(SessionState, PrivateKeyPath);
        var credential = AppStoreConnectCommandSupport.CreateCredential(IssuerId, KeyId, PrivateKey, privateKeyPath, TokenLifetimeMinutes);
        using var client = new AppStoreConnectClient(credential);
        var service = new AppStoreConnectTestFlightDistributionService(client);
        var result = service.DistributeAsync(new AppStoreConnectTestFlightDistributionRequest
        {
            AppId = AppId,
            VersionString = VersionString,
            BuildNumber = BuildNumber,
            Platform = Platform,
            BetaGroupIds = BetaGroupId,
            BetaGroupNames = BetaGroupName,
            Testers = TesterEmail
                .Where(static email => !string.IsNullOrWhiteSpace(email))
                .Select(static email => new AppStoreConnectBetaTesterSpec { Email = email.Trim() })
                .ToArray(),
            CreateMissingTesters = !NoCreateMissingTesters.IsPresent,
            RequireValidBuild = !AllowUnprocessedBuild.IsPresent
        }).GetAwaiter().GetResult();

        WriteObject(result);
    }
}
