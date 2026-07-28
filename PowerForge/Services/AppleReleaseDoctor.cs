using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Performs deterministic local checks for the configured Apple product topology.
/// </summary>
internal static class AppleReleaseDoctor
{
    internal static PowerForgeAppleReleaseDiagnostic[] Evaluate(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectControlPlaneState? controlPlane = null)
    {
        var diagnostics = new List<PowerForgeAppleReleaseDiagnostic>();
        var usesStore = UsesStore(app);

        if (string.IsNullOrWhiteSpace(app.BundleId) &&
            app.ProductRole != AppleProductRole.Capability)
        {
            diagnostics.Add(Error(
                "configuration",
                "APPLE_BUNDLE_ID_MISSING",
                $"Apple target '{app.Name}' has no bundle id.",
                "Set BundleId to the exact identifier used by Xcode and Apple Developer certificates."));
        }

        if (app.AppStoreConnectAppIdDiscovered)
        {
            diagnostics.Add(Info(
                "onboarding",
                "APPLE_APP_ID_DISCOVERED",
                $"App Store Connect app id '{app.AppStoreConnectAppId}' was discovered from bundle id '{app.BundleId}'.",
                "Persist the discovered AppStoreConnectAppId in powerforge.release.json so later runs avoid a discovery request."));
        }

        if (usesStore && string.IsNullOrWhiteSpace(app.AppStoreConnectAppId))
        {
            diagnostics.Add(Error(
                "onboarding",
                "APPLE_APP_ID_MISSING",
                $"Apple target '{app.Name}' has no App Store Connect app id.",
                "Create the app in the App Store Connect website if needed, then rerun Doctor so PowerForge can discover the id from BundleId."));
        }

        if (app.DistributionRoute == AppleDistributionRoute.DirectNotarized &&
            string.IsNullOrWhiteSpace(app.TeamId))
        {
            diagnostics.Add(Error(
                "notarization",
                "APPLE_TEAM_ID_MISSING",
                $"Direct-notarized target '{app.Name}' has no Apple team id.",
                "Set AppleApps.TeamId to the Developer ID team used for archive export and notarization."));
        }

        if (app.TestFlightPolicy == AppleTestFlightPolicy.External &&
            plan.TestFlightBetaGroupIds.Length == 0 &&
            plan.TestFlightBetaGroupNames.Length == 0)
        {
            diagnostics.Add(Error(
                "testflight",
                "APPLE_TESTFLIGHT_GROUP_MISSING",
                $"Target '{app.Name}' requests external TestFlight but has no beta group.",
                "Configure the exact external beta group id or name before distributing or submitting Beta App Review."));
        }

        if (usesStore && !HasConfiguredPath(plan.MetadataConfigPath, plan.MetadataConfigPaths))
        {
            diagnostics.Add(Warning(
                "metadata",
                "APPLE_METADATA_UNMANAGED",
                $"Version metadata for '{app.Name}' is not managed by PowerForge.",
                "Add a release-version-bound metadata config so descriptions, keywords, URLs, and release notes are checked before review."));
        }
        if (usesStore && !HasConfiguredPath(plan.AppInfoConfigPath, plan.AppInfoConfigPaths))
        {
            diagnostics.Add(Warning(
                "metadata",
                "APPLE_APP_INFO_UNMANAGED",
                $"App information for '{app.Name}' is not managed by PowerForge.",
                "Add an app-info config so name, subtitle, privacy URL, and category localization drift is visible."));
        }
        if (app.DistributionRoute == AppleDistributionRoute.AppStore &&
            !HasConfiguredPath(plan.ScreenshotConfigPath, plan.ScreenshotConfigPaths))
        {
            diagnostics.Add(Warning(
                "screenshots",
                "APPLE_SCREENSHOTS_UNMANAGED",
                $"App Store screenshots for '{app.Name}' are not managed by PowerForge.",
                "Add a release-version-bound screenshot config and approval manifest before the next App Store submission."));
        }

        var source = ReadProjectEvidence(app.ProjectPath);
        foreach (var requiredBundleId in app.RequiredEmbeddedBundleIds)
        {
            if (!source.Contains(requiredBundleId, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(
                    "embedding",
                    "APPLE_EMBEDDED_BUNDLE_MISSING",
                    $"Archive target '{app.Name}' does not reference required embedded bundle id '{requiredBundleId}'.",
                    "Add the companion/helper product to the target's embed phase and verify the final xcarchive before upload."));
            }
        }

        if (app.DistributionRoute != AppleDistributionRoute.DevelopmentOnly &&
            app.Capabilities.Any(static capability => capability.Equals("CarPlay", StringComparison.OrdinalIgnoreCase)) &&
            !source.Contains("com.apple.developer.carplay", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error(
                "capabilities",
                "APPLE_CARPLAY_ENTITLEMENT_MISSING",
                $"Target '{app.Name}' declares CarPlay but no CarPlay entitlement was found in project evidence.",
                "Enable the approved CarPlay capability and verify the exact entitlement in the signed archive."));
        }

        if (app.DistributionRoute == AppleDistributionRoute.EmbeddedCompanion &&
            !string.IsNullOrWhiteSpace(app.BundleId))
        {
            var parent = plan.Apps.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, app.ParentTarget, StringComparison.OrdinalIgnoreCase));
            if (parent is not null &&
                !parent.RequiredEmbeddedBundleIds.Contains(app.BundleId!, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    "embedding",
                    "APPLE_EMBEDDED_CONTRACT_UNDECLARED",
                    $"Embedded target '{app.Name}' is not listed in '{parent.Name}' RequiredEmbeddedBundleIds.",
                    "Add the companion bundle id to the parent target so archive verification cannot silently omit it."));
            }
        }

        if (usesStore && controlPlane is not null)
            AddControlPlaneDiagnostics(app, controlPlane, diagnostics);

        return diagnostics.ToArray();
    }

    private static void AddControlPlaneDiagnostics(
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectControlPlaneState state,
        List<PowerForgeAppleReleaseDiagnostic> diagnostics)
    {
        if (app.DistributionRoute == AppleDistributionRoute.AppStore && !state.ReviewDetailsConfigured)
        {
            var reason = !state.ReviewDetailsExist
                ? "the App Review Details resource is missing"
                : !state.ReviewContactConfigured
                    ? "one or more required contact fields are empty"
                    : !state.ReviewDemoAccountRequirementDeclared
                        ? "the demo-account requirement is not declared"
                        : state.ReviewDemoAccountRequired == true && !state.ReviewDemoCredentialsConfigured
                            ? "the required demo-account credentials are empty"
                            : "the required review details are incomplete";
            diagnostics.Add(Error(
                "review",
                "APPLE_REVIEW_DETAILS_MISSING",
                $"App Review is not ready for '{app.Name}': {reason}.",
                "Configure App Store Review Details for the selected version before submission."));
        }
        if (!state.AgeRatingDeclared)
        {
            diagnostics.Add(Error(
                "compliance",
                "APPLE_AGE_RATING_MISSING",
                $"No age-rating declaration was found for '{app.Name}'.",
                "Complete the age-rating questionnaire in App Store Connect and keep the answers under reviewed configuration."));
        }
        if (app.DistributionRoute == AppleDistributionRoute.AppStore && !state.PriceScheduleConfigured)
        {
            diagnostics.Add(Error(
                "commerce",
                "APPLE_PRICE_SCHEDULE_MISSING",
                $"No app price schedule was found for '{app.Name}'.",
                "Set the base territory and price schedule, including a free price when appropriate."));
        }
        if (app.DistributionRoute == AppleDistributionRoute.AppStore && !state.AvailabilityConfigured)
        {
            diagnostics.Add(Error(
                "commerce",
                "APPLE_AVAILABILITY_MISSING",
                $"No territory availability configuration was found for '{app.Name}'.",
                "Configure storefront availability and verify any business/education distribution choices."));
        }
        if (state.EncryptionDeclarationCount == 0)
        {
            diagnostics.Add(Warning(
                "compliance",
                "APPLE_ENCRYPTION_ATTESTATION_UNTRACKED",
                $"No encryption declaration is registered for '{app.Name}'.",
                "Verify ITSAppUsesNonExemptEncryption in the shipped Info.plist or register the required encryption declaration."));
        }
        if (state.AccessibilityDeclarationCount == 0)
        {
            diagnostics.Add(Warning(
                "accessibility",
                "APPLE_ACCESSIBILITY_UNDECLARED",
                $"No accessibility declaration was found for '{app.Name}'.",
                "Declare verified accessibility features per supported device family before publishing the product page."));
        }
        if (state.WebhookCount == 0)
        {
            diagnostics.Add(Warning(
                "observability",
                "APPLE_WEBHOOK_MISSING",
                $"No App Store Connect webhook is configured for '{app.Name}'.",
                "Create and ping a signed webhook for build upload, TestFlight feedback, and version-state events; retain scheduled polling as fallback."));
        }
        if (state.BetaCrashFeedbackCount > 0)
        {
            diagnostics.Add(Warning(
                "testflight-feedback",
                "APPLE_BETA_CRASH_FEEDBACK",
                $"App Store Connect reports {state.BetaCrashFeedbackCount} TestFlight crash-feedback item(s) for '{app.Name}'.",
                "Fetch and triage the crash logs before promoting this build."));
        }
        if (state.BetaScreenshotFeedbackCount > 0)
        {
            diagnostics.Add(Info(
                "testflight-feedback",
                "APPLE_BETA_SCREENSHOT_FEEDBACK",
                $"App Store Connect reports {state.BetaScreenshotFeedbackCount} TestFlight screenshot-feedback item(s) for '{app.Name}'.",
                "Review the tester screenshots and associated comments in the release issue."));
        }
    }

    private static bool UsesStore(PowerForgeAppleAppReleaseTargetPlan app)
        => app.DistributionRoute == AppleDistributionRoute.AppStore ||
           app.DistributionRoute == AppleDistributionRoute.TestFlightOnly;

    private static bool HasConfiguredPath(string? path, string[]? paths)
        => !string.IsNullOrWhiteSpace(path) ||
           (paths ?? Array.Empty<string>()).Any(static value => !string.IsNullOrWhiteSpace(value));

    private static string ReadProjectEvidence(string projectPath)
    {
        var paths = new List<string>();
        if (File.Exists(projectPath))
            paths.Add(projectPath);
        else if (Directory.Exists(projectPath))
            paths.AddRange(Directory.EnumerateFiles(projectPath, "*", SearchOption.AllDirectories));

        var parent = Directory.Exists(projectPath)
            ? Directory.GetParent(projectPath)?.FullName
            : Path.GetDirectoryName(projectPath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
        {
            paths.AddRange(Directory.EnumerateFiles(parent, "*.entitlements", SearchOption.AllDirectories));
            paths.AddRange(Directory.EnumerateFiles(parent, "project.yml", SearchOption.TopDirectoryOnly));
            paths.AddRange(Directory.EnumerateFiles(parent, "project.yaml", SearchOption.TopDirectoryOnly));
        }
        if (Directory.Exists(projectPath) &&
            projectPath.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
        {
            AddReferencedWorkspaceProjects(projectPath, paths);
        }

        var content = new List<string>();
        foreach (var path in paths
                     .Where(IsRelevantProjectEvidence)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(500))
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 2 * 1024 * 1024)
                    content.Add(File.ReadAllText(path));
            }
            catch
            {
                // Doctor evidence collection is best effort; core path validation reports hard failures.
            }
        }
        return string.Join(Environment.NewLine, content);
    }

    private static void AddReferencedWorkspaceProjects(string workspacePath, List<string> paths)
    {
        var workspaceParent = Directory.GetParent(workspacePath)?.FullName;
        var contentsPath = Path.Combine(workspacePath, "contents.xcworkspacedata");
        if (workspaceParent is null || workspaceParent.Length == 0 || !File.Exists(contentsPath))
            return;

        try
        {
            var document = XDocument.Load(contentsPath, LoadOptions.None);
            foreach (var reference in document.Descendants("FileRef"))
            {
                var location = reference.Attribute("location")?.Value;
                if (location is null || location.Length == 0)
                    continue;
                var separator = location.IndexOf(':');
                var pathPart = separator >= 0 ? location.Substring(separator + 1) : location;
                if (string.IsNullOrWhiteSpace(pathPart))
                    continue;

                var decoded = Uri.UnescapeDataString(pathPart);
                var candidate = Path.GetFullPath(Path.Combine(workspaceParent, decoded));
                if (!IsContainedPath(workspaceParent, candidate))
                    continue;
                if (Directory.Exists(candidate) &&
                    candidate.EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = Path.Combine(candidate, "project.pbxproj");
                }
                if (File.Exists(candidate) && IsRelevantProjectEvidence(candidate))
                    paths.Add(candidate);
            }
        }
        catch
        {
            // Workspace evidence is advisory; malformed workspace XML is diagnosed by Xcode itself.
        }
    }

    private static bool IsContainedPath(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.Equals(root, comparison) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
               candidate.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }

    private static bool IsRelevantProjectEvidence(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return fileName.Equals("project.pbxproj", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("project.yml", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("project.yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".entitlements", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".plist", StringComparison.OrdinalIgnoreCase);
    }

    private static PowerForgeAppleReleaseDiagnostic Error(string category, string code, string summary, string action)
        => Create("error", category, code, summary, action);

    private static PowerForgeAppleReleaseDiagnostic Warning(string category, string code, string summary, string action)
        => Create("warning", category, code, summary, action);

    private static PowerForgeAppleReleaseDiagnostic Info(string category, string code, string summary, string action)
        => Create("info", category, code, summary, action);

    private static PowerForgeAppleReleaseDiagnostic Create(
        string severity,
        string category,
        string code,
        string summary,
        string action)
        => new()
        {
            Severity = severity,
            Category = category,
            Code = code,
            Summary = summary,
            Action = action,
            Retryable = false
        };
}
