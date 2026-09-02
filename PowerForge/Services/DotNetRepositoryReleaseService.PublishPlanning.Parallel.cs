using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private IReadOnlyDictionary<string, IReadOnlyCollection<string>> ResolvePlannedProjectDependencies(
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        string? configuration,
        DotNetRepositoryPackStrategy packStrategy,
        bool includeSymbols,
        string? packageOutputPath,
        int progressCompletedOffset,
        int progressTotalItems,
        IProjectBuildProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var entries = selectedPackages.ToArray();
        if (entries.Length == 0)
            return new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.Plan,
            progressCompletedOffset,
            progressTotalItems,
            "Evaluating package dependency graphs");
        var resolutions = ResolveProjectPlanningItems(
            entries.Length,
            (index, logger) =>
            {
                var entry = entries[index];
                try
                {
                    return new PlannedDependencyResolution(
                        ReadPlannedProjectDependencies(
                            entry.Value,
                            selectedPackages,
                            configuration,
                            packStrategy,
                            includeSymbols,
                            packageOutputPath,
                            logger),
                        error: null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new PlannedDependencyResolution(
                        Array.Empty<string>(),
                        ex);
                }
            },
            (index, resolution, completed) =>
            {
                var entry = entries[index];
                progress?.PhaseUpdated(
                    ProjectBuildProgressPhase.Plan,
                    progressCompletedOffset + completed,
                    progressTotalItems,
                    resolution.Error is null
                        ? $"{entry.Value.ProjectName}: dependency graph evaluated"
                        : $"{entry.Value.ProjectName}: dependency graph evaluation failed");
            },
            cancellationToken);

        var failed = resolutions.FirstOrDefault(static resolution => resolution.Error is not null);
        if (failed?.Error is not null)
            throw failed.Error;

        return entries
            .Select((entry, index) => new { entry.Key, Dependencies = resolutions[index].Dependencies })
            .ToDictionary(
                static item => item.Key,
                static item => item.Dependencies,
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PlannedDependencyResolution
    {
        internal PlannedDependencyResolution(
            IReadOnlyCollection<string> dependencies,
            Exception? error)
        {
            Dependencies = dependencies;
            Error = error;
        }

        internal IReadOnlyCollection<string> Dependencies { get; }
        internal Exception? Error { get; }
    }
}
