namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private void DiscoverAppStoreConnectAppId(
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectApiCredential credential,
        PowerForgeAppleReleaseAction action)
    {
        if (string.IsNullOrWhiteSpace(app.BundleId))
        {
            throw new InvalidOperationException(
                $"Apple action '{action}' cannot discover AppStoreConnectAppId for '{app.Name}' because BundleId is missing.");
        }

        var matches = _findAppleApps(credential, app.BundleId!)
            .Where(candidate => string.Equals(candidate.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (matches.Length == 0)
        {
            if (action == PowerForgeAppleReleaseAction.Doctor)
                return;
            throw new InvalidOperationException(
                $"App Store Connect has no app record for bundle id '{app.BundleId}' used by '{app.Name}'. " +
                "Create the app record in the App Store Connect website, then rerun this action; the API cannot create apps.");
        }
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"App Store Connect returned multiple app records for bundle id '{app.BundleId}' used by '{app.Name}': " +
                string.Join(", ", matches.Select(static candidate => candidate.Id)) +
                ". Configure AppStoreConnectAppId explicitly.");
        }

        app.AppStoreConnectAppId = matches[0].Id;
        app.AppStoreConnectAppIdDiscovered = true;
    }

    private static void ValidateAppleProductTopology(PowerForgeAppleAppReleaseTargetPlan[] apps)
    {
        var names = apps.Select(static app => app.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var embedded = app.DistributionRoute == AppleDistributionRoute.EmbeddedCompanion;
            if (embedded && string.IsNullOrWhiteSpace(app.ParentTarget))
                throw new InvalidOperationException($"Embedded Apple target '{app.Name}' requires ParentTarget.");
            if (!string.IsNullOrWhiteSpace(app.ParentTarget) && !names.Contains(app.ParentTarget!))
                throw new InvalidOperationException($"Apple target '{app.Name}' references unknown ParentTarget '{app.ParentTarget}'.");
            if (string.Equals(app.Name, app.ParentTarget, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Apple target '{app.Name}' cannot be its own ParentTarget.");
            if (app.DistributionRoute == AppleDistributionRoute.DirectNotarized && app.Platform != ApplePlatform.macOS)
                throw new InvalidOperationException($"Direct-notarized Apple target '{app.Name}' must use Platform macOS.");
            if (app.DistributionRoute == AppleDistributionRoute.TestFlightOnly &&
                app.TestFlightPolicy == AppleTestFlightPolicy.Disabled)
            {
                throw new InvalidOperationException(
                    $"TestFlight-only Apple target '{app.Name}' cannot disable TestFlight.");
            }
        }

        var appsByName = apps.ToDictionary(static app => app.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { app.Name };
            var current = app;
            while (!string.IsNullOrWhiteSpace(current.ParentTarget))
            {
                if (!chain.Add(current.ParentTarget!))
                {
                    throw new InvalidOperationException(
                        $"Apple target '{app.Name}' has a cyclic ParentTarget chain involving '{current.ParentTarget}'.");
                }

                current = appsByName[current.ParentTarget!];
            }

            if (app.DistributionRoute == AppleDistributionRoute.EmbeddedCompanion &&
                !IsIndependentReleaseTarget(current))
            {
                throw new InvalidOperationException(
                    $"Embedded Apple target '{app.Name}' must ultimately reference an App Store, TestFlight-only, or direct-notarized archive owner.");
            }
        }
    }

    /// <summary>
    /// Rejects declared Mac App Store extension identities before archive/upload,
    /// including nested companions omitted by a parent-only target selection.
    /// </summary>
    private static void ValidateMacAppStoreExtensionIdentifiers(
        PowerForgeAppleAppReleaseTargetPlan[] selectedApps,
        AppleAppConfiguration[] configuredApps)
    {
        var roots = new HashSet<string>(selectedApps.Where(static app => app.Platform == ApplePlatform.macOS &&
                app.DistributionRoute is AppleDistributionRoute.AppStore or AppleDistributionRoute.TestFlightOnly)
            .Select(static app => app.Name), StringComparer.OrdinalIgnoreCase);
        if (roots.Count == 0)
            return;

        var appsByName = configuredApps.Where(static app => !string.IsNullOrWhiteSpace(app.Name))
            .ToDictionary(static app => app.Name!.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var extension in configuredApps.Where(static app => app.Platform == ApplePlatform.macOS &&
                     app.DistributionRoute == AppleDistributionRoute.EmbeddedCompanion &&
                     app.ProductRole == AppleProductRole.Extension))
        {
            if (!appsByName.TryGetValue(extension.ParentTarget?.Trim() ?? string.Empty, out var parent) ||
                !HasSelectedMacStoreAncestor(parent, appsByName, roots))
                continue;

            // Missing identifiers retain their existing project/identity diagnostics.
            if (string.IsNullOrWhiteSpace(parent.BundleId) || string.IsNullOrWhiteSpace(extension.BundleId))
                continue;

            // App Store validation ITMS-90347 is stricter than general CFBundleIdentifier syntax.
            var prefix = parent.BundleId!.Trim() + ".";
            var bundleId = extension.BundleId!.Trim();
            if (!bundleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                bundleId.Length == prefix.Length || bundleId.Substring(prefix.Length).IndexOf('.') >= 0)
            {
                throw new InvalidOperationException(
                    $"Mac App Store extension '{extension.Name}' bundle identifier '{bundleId}' must be " +
                    $"'{parent.BundleId}.<extension>' with one nonempty suffix and no additional periods.");
            }
        }
    }

    /// <summary>Follows only the configured embedding chain into a selected Mac store owner.</summary>
    private static bool HasSelectedMacStoreAncestor(
        AppleAppConfiguration app,
        IReadOnlyDictionary<string, AppleAppConfiguration> appsByName,
        HashSet<string> roots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(app.Name) && visited.Add(app.Name!.Trim()))
        {
            if (roots.Contains(app.Name!.Trim()))
                return true;
            if (app.DistributionRoute != AppleDistributionRoute.EmbeddedCompanion ||
                !appsByName.TryGetValue(app.ParentTarget?.Trim() ?? string.Empty, out var parent))
                return false;
            app = parent;
        }
        return false;
    }
}
