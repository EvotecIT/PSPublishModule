using System.Collections.Generic;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void ExecutePublishOperations(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var publishOrder = ResolvePublishOrder(plan);
        var packageNuGetPublished = false;
        var packageGitHubPublished = false;
        var modulePublished = new HashSet<ConfigurationPublishSegment>();

        foreach (var destination in publishOrder)
        {
            switch (destination)
            {
                case ReleasePublishDestination.NuGet:
                    if (!packageNuGetPublished)
                    {
                        ExecutePackageBuildPublishes(plan, session, state, PackageBuildPublishDestination.NuGet);
                        packageNuGetPublished = true;
                    }
                    break;
                case ReleasePublishDestination.PowerShellGallery:
                    ExecuteModulePublishes(plan, session, buildResult, state, modulePublished, PublishDestination.PowerShellGallery);
                    break;
                case ReleasePublishDestination.GitHub:
                    if (!packageGitHubPublished)
                    {
                        ExecutePackageBuildPublishes(plan, session, state, PackageBuildPublishDestination.GitHub);
                        packageGitHubPublished = true;
                    }

                    ExecuteModulePublishes(plan, session, buildResult, state, modulePublished, PublishDestination.GitHub);
                    break;
            }
        }

        if (!packageNuGetPublished)
            ExecutePackageBuildPublishes(plan, session, state, PackageBuildPublishDestination.NuGet);
        if (!packageGitHubPublished)
            ExecutePackageBuildPublishes(plan, session, state, PackageBuildPublishDestination.GitHub);

        ExecuteModulePublishes(plan, session, buildResult, state, modulePublished, PublishDestination.PowerShellGallery);
        ExecuteModulePublishes(plan, session, buildResult, state, modulePublished, PublishDestination.GitHub);
    }

    private void ExecuteModulePublishes(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state,
        HashSet<ConfigurationPublishSegment> completed,
        PublishDestination destination)
    {
        foreach (var publish in plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
        {
            if (publish?.Configuration is null ||
                publish.Configuration.Destination != destination ||
                completed.Contains(publish))
            {
                continue;
            }

            ExecuteModulePublish(plan, session, buildResult, state, publish);
            completed.Add(publish);
        }
    }

    private void ExecuteModulePublish(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state,
        ConfigurationPublishSegment publish)
    {
        var step = session.GetPublishStep(publish);
        session.Start(step);
        try
        {
            state.PublishResults.Add(ShouldPublishUnifiedGitHubRelease(plan, publish.Configuration)
                ? PublishUnifiedGitHubRelease(publish.Configuration, plan, state)
                : _hostedOperations.PublishModule(
                    publish.Configuration,
                    plan,
                    buildResult,
                    state.ArtefactResults,
                    includeScriptFolders: !state.PackageWithoutScriptFolders));
            session.Done(step);
        }
        catch (Exception ex)
        {
            session.Fail(step, ex);
            throw;
        }
    }

    private static ReleasePublishDestination[] ResolvePublishOrder(ModulePipelinePlan plan)
    {
        var configured = plan.Release?.Configuration?.PublishOrder ?? Array.Empty<string>();
        var resolved = new List<ReleasePublishDestination>();
        foreach (var item in configured)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (TryParseReleasePublishDestination(item, out var destination) &&
                !resolved.Contains(destination))
            {
                resolved.Add(destination);
            }
        }

        foreach (var destination in new[]
                 {
                     ReleasePublishDestination.NuGet,
                     ReleasePublishDestination.PowerShellGallery,
                     ReleasePublishDestination.GitHub
                 })
        {
            if (!resolved.Contains(destination))
                resolved.Add(destination);
        }

        return resolved.ToArray();
    }

    private static bool TryParseReleasePublishDestination(string value, out ReleasePublishDestination destination)
    {
        var normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        if (string.Equals(normalized, "NuGet", StringComparison.OrdinalIgnoreCase))
        {
            destination = ReleasePublishDestination.NuGet;
            return true;
        }

        if (string.Equals(normalized, "PowerShellGallery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "PSGallery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Gallery", StringComparison.OrdinalIgnoreCase))
        {
            destination = ReleasePublishDestination.PowerShellGallery;
            return true;
        }

        if (string.Equals(normalized, "GitHub", StringComparison.OrdinalIgnoreCase))
        {
            destination = ReleasePublishDestination.GitHub;
            return true;
        }

        destination = default;
        return false;
    }

    private enum ReleasePublishDestination
    {
        NuGet,
        PowerShellGallery,
        GitHub
    }
}
