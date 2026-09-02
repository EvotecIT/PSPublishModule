using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private IReadOnlyDictionary<string, IReadOnlyCollection<string>> ResolvePlannedProjectDependencies(
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        string? configuration,
        DotNetRepositoryPackStrategy packStrategy,
        bool includeSymbols,
        string? packageOutputPath,
        IProjectBuildProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var entries = selectedPackages.ToArray();
        if (entries.Length == 0)
            return new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        var resolutions = new PlannedDependencyResolution[entries.Length];
        var completed = 0;
        var reportingSync = new object();
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.Plan,
            completed,
            entries.Length,
            "Evaluating package dependency graphs");
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = GetProjectPlanningMaxDegree(entries.Length)
        };

        Parallel.For(0, entries.Length, options, index =>
        {
            var entry = entries[index];
            var logger = new SynchronizedLogger(_logger, reportingSync);
            try
            {
                resolutions[index] = new PlannedDependencyResolution(
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
                resolutions[index] = new PlannedDependencyResolution(
                    Array.Empty<string>(),
                    ex);
            }

            lock (reportingSync)
            {
                completed++;
                progress?.PhaseUpdated(
                    ProjectBuildProgressPhase.Plan,
                    completed,
                    entries.Length,
                    resolutions[index].Error is null
                        ? $"{entry.Value.ProjectName}: dependency graph evaluated"
                        : $"{entry.Value.ProjectName}: dependency graph evaluation failed");
            }
        });

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
