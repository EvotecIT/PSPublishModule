namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private PowerForgeAppleReleaseReceipt CreateApplePlanReceipt(PowerForgeAppleReleasePlan plan)
    {
        PowerForgeAppleVersionReceipt? versioning = null;
        if (plan.Action == PowerForgeAppleReleaseAction.Version)
            versioning = PlanAppleVersion(plan, whatIf: true);

        var receipt = new PowerForgeAppleReleaseReceipt
        {
            Action = plan.Action,
            PlanOnly = true,
            CheckedAt = DateTimeOffset.UtcNow,
            Success = true,
            ReceiptPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, plan.PlanReceiptPath).Replace('\\', '/'),
            Versioning = versioning,
            Targets = plan.Apps.Select(app => new PowerForgeAppleReleaseTargetReceipt
            {
                Name = app.Name,
                BundleId = app.BundleId,
                Platform = app.Platform,
                DistributionRoute = app.DistributionRoute,
                ProductRole = app.ProductRole,
                ParentTarget = app.ParentTarget,
                Capabilities = app.Capabilities,
                TestFlightPolicy = app.TestFlightPolicy,
                AppId = app.AppStoreConnectAppId,
                AppIdDiscovered = app.AppStoreConnectAppIdDiscovered,
                Version = versioning?.MarketingVersion ?? app.MarketingVersion,
                Build = versioning?.BuildNumber ?? app.BuildNumber,
                SkippedSteps = new[] { "plan-only" }
            }).ToArray(),
            NextActions = new[] { $"Run Apple action '{plan.Action}' without --plan after reviewing this plan receipt." }
        };

        if (plan.Automation.WriteReceipt)
            WriteAppleReceipt(plan.ProjectRoot, plan.PlanReceiptPath, receipt);
        return receipt;
    }

    private PowerForgeAppleVersionReceipt SelectAppleVersion(PowerForgeAppleReleasePlan plan)
    {
        var versioning = PlanAppleVersion(plan, whatIf: false);
        foreach (var app in plan.Apps)
        {
            app.MarketingVersion = versioning.MarketingVersion;
            app.BuildNumber = versioning.BuildNumber;
        }

        return versioning;
    }

    private PowerForgeAppleAppReleaseResult[] RunAppleVersion(PowerForgeAppleReleasePlan plan)
    {
        var generatedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PowerForgeAppleAppReleaseResult>();
        foreach (var app in plan.Apps)
        {
            var result = new PowerForgeAppleAppReleaseResult
            {
                Plan = app,
                Success = true,
                SkippedSteps = new[] { "archive", "upload", "distribution", "review", "release" }
            };
            if (generatedProjects.Add(app.ProjectPath))
            {
                var generationPlan = new PowerForgeAppleAppReleaseTargetPlan
                {
                    Name = app.Name,
                    ProjectPath = app.ProjectPath,
                    GenerateProjectIfMissing = true,
                    RegenerateProject = true,
                    XcodeGenExecutable = app.XcodeGenExecutable,
                    ProjectGenerationTimeoutSeconds = app.ProjectGenerationTimeoutSeconds
                };
                result.ProjectGenerated = _generateAppleProject(generationPlan);
            }
            results.Add(result);
        }

        return results.ToArray();
    }

    private PowerForgeAppleVersionReceipt PlanAppleVersion(PowerForgeAppleReleasePlan plan, bool whatIf)
    {
        if (string.IsNullOrWhiteSpace(plan.VersionSourcePath))
            throw new InvalidOperationException("Apple version source path is required for Version.");
        if (string.IsNullOrWhiteSpace(plan.RequestedMarketingVersion))
            throw new InvalidOperationException("Requested Apple marketing version is required for Version.");

        var source = new AppleReleaseVersionSourceService();
        var current = source.Read(plan.VersionSourcePath!);
        if (!long.TryParse(current.BuildNumber, out var currentBuild) || currentBuild < 0)
            throw new InvalidOperationException($"Apple version source build number '{current.BuildNumber}' is not a non-negative integer.");

        var storeApps = plan.Apps.Where(UsesAppStoreConnect).ToArray();
        var highestRemote = 0L;
        if (storeApps.Length > 0)
        {
            var credential = CreateAppStoreConnectCredential(plan);
            highestRemote = storeApps
                .Select(app => _getHighestAppleBuildNumber(credential, app.AppStoreConnectAppId!, app.Platform))
                .DefaultIfEmpty(0)
                .Max();
        }
        var requestedVersion = plan.RequestedMarketingVersion!.Trim();
        var nextBuild = string.Equals(current.MarketingVersion, requestedVersion, StringComparison.OrdinalIgnoreCase) &&
                        currentBuild > highestRemote
            ? currentBuild
            : checked(Math.Max(currentBuild, highestRemote) + 1);
        var receipt = source.Update(
            plan.VersionSourcePath!,
            requestedVersion,
            nextBuild.ToString(System.Globalization.CultureInfo.InvariantCulture),
            highestRemote,
            whatIf);
        receipt.SourcePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, plan.VersionSourcePath!).Replace('\\', '/');
        return receipt;
    }

    private static long GetHighestAppleBuildNumber(
        AppStoreConnectApiCredential credential,
        string appId,
        ApplePlatform platform)
    {
        using var client = new AppStoreConnectClient(credential);
        return client.GetBuildsAsync(appId, limit: 200, platform: platform)
            .GetAwaiter()
            .GetResult()
            .Select(static build => long.TryParse(build.Version, out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
    }
}
