using System.Globalization;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecuteAppleVersioningPhase(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        if (plan.AppleApps is { Length: > 0 })
        {
            var editor = new XcodeProjectVersionEditor();
            foreach (var segment in plan.AppleApps)
            {
                var step = session.GetAppleAppStep(segment);
                session.Start(step);
                try
                {
                    var result = PrepareAppleApp(plan, segment, editor);
                    state.AppleAppResults.Add(result);
                    state.XcodeProjectVersionResults.Add(result.XcodeProjectVersionResult);
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }

        if (plan.XcodeProjectVersions is { Length: > 0 })
        {
            var editor = new XcodeProjectVersionEditor();
            foreach (var segment in plan.XcodeProjectVersions)
            {
                var step = session.GetXcodeProjectVersionStep(segment);
                session.Start(step);
                try
                {
                    state.XcodeProjectVersionResults.Add(UpdateXcodeProjectVersion(plan, segment, editor));
                    session.Done(step);
                }
                catch (Exception ex)
                {
                    session.Fail(step, ex);
                    throw;
                }
            }
        }
    }

    private XcodeProjectVersionUpdateResult UpdateXcodeProjectVersion(
        ModulePipelinePlan plan,
        ConfigurationXcodeProjectVersionSegment segment,
        XcodeProjectVersionEditor editor)
    {
        var cfg = segment.Configuration ?? new XcodeProjectVersionConfiguration();
        if (string.IsNullOrWhiteSpace(cfg.Path))
            throw new InvalidOperationException("XcodeProjectVersion.Path is required.");

        var projectPath = ResolvePath(plan.ProjectRoot, cfg.Path);
        var marketingVersion = cfg.UseResolvedVersion
            ? plan.ResolvedVersion
            : cfg.MarketingVersion;

        if (string.IsNullOrWhiteSpace(marketingVersion))
            throw new InvalidOperationException("XcodeProjectVersion.MarketingVersion is required unless UseResolvedVersion is enabled.");

        var result = editor.Update(projectPath, marketingVersion!, cfg.BuildNumber);
        var buildText = string.IsNullOrWhiteSpace(cfg.BuildNumber)
            ? string.Empty
            : $", build {cfg.BuildNumber}";

        if (result.Changed)
            _logger.Success($"Updated Xcode project version: {result.ProjectFilePath} -> {marketingVersion}{buildText}");
        else
            _logger.Info($"Xcode project version already current: {result.ProjectFilePath} -> {marketingVersion}{buildText}");

        return result;
    }

    private AppleAppReleasePreparationResult PrepareAppleApp(
        ModulePipelinePlan plan,
        ConfigurationAppleAppSegment segment,
        XcodeProjectVersionEditor editor)
    {
        var cfg = segment.Configuration ?? new AppleAppConfiguration();
        if (string.IsNullOrWhiteSpace(cfg.ProjectPath))
            throw new InvalidOperationException("AppleApp.ProjectPath is required.");

        var projectPath = ResolvePath(plan.ProjectRoot, cfg.ProjectPath);
        var marketingVersion = cfg.UseResolvedVersion
            ? plan.ResolvedVersion
            : cfg.MarketingVersion;

        if (string.IsNullOrWhiteSpace(marketingVersion))
            throw new InvalidOperationException("AppleApp.MarketingVersion is required unless UseResolvedVersion is enabled.");

        var buildNumber = ResolveAppleBuildNumber(cfg, projectPath, editor);
        var xcodeResult = editor.Update(projectPath, marketingVersion!, buildNumber);

        var label = !string.IsNullOrWhiteSpace(cfg.Name) ? cfg.Name!.Trim() : cfg.Platform.ToString();
        var buildText = string.IsNullOrWhiteSpace(buildNumber) ? string.Empty : $", build {buildNumber}";
        if (xcodeResult.Changed)
            _logger.Success($"Prepared Apple app '{label}' ({cfg.Platform}): {marketingVersion}{buildText}");
        else
            _logger.Info($"Apple app '{label}' ({cfg.Platform}) already current: {marketingVersion}{buildText}");

        return new AppleAppReleasePreparationResult
        {
            Name = cfg.Name,
            BundleId = cfg.BundleId,
            Platform = cfg.Platform,
            Scheme = cfg.Scheme,
            AppStoreConnectAppId = cfg.AppStoreConnectAppId,
            BuildNumberPolicy = cfg.BuildNumberPolicy,
            MarketingVersion = marketingVersion!,
            BuildNumber = buildNumber,
            XcodeProjectVersionResult = xcodeResult
        };
    }

    private static string? ResolveAppleBuildNumber(
        AppleAppConfiguration cfg,
        string projectPath,
        XcodeProjectVersionEditor editor)
    {
        if (!string.IsNullOrWhiteSpace(cfg.BuildNumber))
            return cfg.BuildNumber!.Trim();

        return cfg.BuildNumberPolicy switch
        {
            AppleBuildNumberPolicy.KeepExisting => null,
            AppleBuildNumberPolicy.Explicit => throw new InvalidOperationException("AppleApp.BuildNumber is required when BuildNumberPolicy is Explicit."),
            AppleBuildNumberPolicy.IncrementExisting => IncrementExistingAppleBuildNumber(editor.Read(projectPath)),
            _ => throw new InvalidOperationException($"Unsupported Apple build number policy: {cfg.BuildNumberPolicy}.")
        };
    }

    private static string IncrementExistingAppleBuildNumber(XcodeProjectVersionInfo info)
    {
        if (info.BuildNumber is null)
            throw new InvalidOperationException($"Cannot increment Apple build number because CURRENT_PROJECT_VERSION is missing or inconsistent in '{info.ProjectFilePath}'.");

        if (!long.TryParse(info.BuildNumber, out var current))
            throw new InvalidOperationException($"Cannot increment Apple build number '{info.BuildNumber}' in '{info.ProjectFilePath}'. Only integer build numbers are supported.");

        return (current + 1).ToString(CultureInfo.InvariantCulture);
    }
}
