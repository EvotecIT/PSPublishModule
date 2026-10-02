namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void ValidateDotNetAppleTarget(AppleAppConfiguration app, string configPath, string? teamId)
    {
        if (app.Platform != ApplePlatform.macOS || app.DistributionRoute != AppleDistributionRoute.AppStore ||
            app.ArchiveVariant != AppleArchiveVariant.Default)
            throw new InvalidOperationException("DotNetPublishInstallerId supports native macOS App Store targets only.");
        if (app.GenerateProjectIfMissing || app.RegenerateProject || app.UseResolvedVersion ||
            app.BuildNumberPolicy != AppleBuildNumberPolicy.KeepExisting)
            throw new InvalidOperationException("Native .NET targets use explicit versions in the MacApp publish config; Xcode generation and version mutation do not apply.");
        var spec = DotNetPublishConfiguration.Load(configPath);
        var installer = spec.Installers.SingleOrDefault(item => item.Id == app.DotNetPublishInstallerId)
            ?? throw new InvalidOperationException($"MacApp installer '{app.DotNetPublishInstallerId}' was not found.");
        var mac = installer.MacApp;
        if (installer.Kind != DotNetPublishInstallerKind.MacApp || mac?.AppStore != true)
            throw new InvalidOperationException("The selected installer must be a MacApp with AppStore=true.");
        if (mac.BundleIdentifier != app.BundleId || mac.TeamId != teamId ||
            mac.Version != app.MarketingVersion || mac.BuildNumber != app.BuildNumber)
            throw new InvalidOperationException("The .NET MacApp bundle, team, marketing version and build must match the Apple release target.");
    }

    private static IEnumerable<string> EnumerateDotNetAppleInputs(PowerForgeAppleReleasePlan plan)
    {
        foreach (var app in plan.Apps.Where(app => !string.IsNullOrWhiteSpace(app.DotNetPublishInstallerId)))
        {
            var configured = DotNetPublishReleaseArtifactVerifier.ReadConfiguredPublishSpecWithInputs(app.ProjectPath);
            foreach (string input in configured.InputPaths) yield return input;
            var spec = DotNetPublishConfiguration.Load(app.ProjectPath);
            var mac = spec.Installers.Single(item => item.Id == app.DotNetPublishInstallerId).MacApp!;
            foreach (string? path in new[] { mac.EntitlementsPath, mac.IconPath, mac.ProvisioningProfilePath, mac.ThirdPartyNoticesManifestPath })
                if (!string.IsNullOrWhiteSpace(path)) yield return Path.GetFullPath(Path.Combine(spec.DotNet.ProjectRoot!, path!));
        }
    }
}
