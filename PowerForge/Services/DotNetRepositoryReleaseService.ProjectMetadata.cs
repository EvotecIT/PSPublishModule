using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private IReadOnlyDictionary<string, ProjectMetadataResolution> ResolveProjectMetadata(
        IReadOnlyList<(string Name, string Path)> candidates,
        DotNetRepositoryReleaseSpec spec,
        CancellationToken cancellationToken,
        IProjectBuildProgressReporter? progress)
    {
        if (candidates.Count == 0)
            return new Dictionary<string, ProjectMetadataResolution>(StringComparer.OrdinalIgnoreCase);

        var resolutions = new ProjectMetadataResolution[candidates.Count];
        var completed = 0;
        var reportingSync = new object();
        progress?.PhaseUpdated(
            ProjectBuildProgressPhase.Plan,
            completed,
            candidates.Count,
            "Evaluating project metadata");
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = GetProjectPlanningMaxDegree(candidates.Count)
        };

        Parallel.For(0, candidates.Count, options, index =>
        {
            var candidate = candidates[index];
            var logger = new SynchronizedLogger(_logger, reportingSync);
            try
            {
                resolutions[index] = new ProjectMetadataResolution(
                    ResolvePackageId(candidate.Path, candidate.Name, spec, logger),
                    IsPackable(candidate.Path),
                    error: null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                resolutions[index] = new ProjectMetadataResolution(
                    candidate.Name,
                    IsPackable(candidate.Path),
                    ex);
            }

            lock (reportingSync)
            {
                completed++;
                progress?.PhaseUpdated(
                    ProjectBuildProgressPhase.Plan,
                    completed,
                    candidates.Count,
                    resolutions[index].Error is null
                        ? $"{candidate.Name}: metadata evaluated"
                        : $"{candidate.Name}: metadata evaluation failed");
            }
        });

        var failed = resolutions.FirstOrDefault(static resolution => resolution.Error is not null);
        if (failed?.Error is not null)
            throw failed.Error;

        return candidates
            .Select((candidate, index) => new { candidate.Path, Resolution = resolutions[index] })
            .ToDictionary(
                static item => item.Path,
                static item => item.Resolution,
                StringComparer.OrdinalIgnoreCase);
    }

    private static int GetProjectPlanningMaxDegree(int workItemCount)
        => Math.Min(workItemCount, Math.Min(16, Math.Max(1, Environment.ProcessorCount)));

    private sealed class ProjectMetadataResolution
    {
        internal ProjectMetadataResolution(
            string packageId,
            bool isPackable,
            Exception? error)
        {
            PackageId = packageId;
            IsPackable = isPackable;
            Error = error;
        }

        internal string PackageId { get; }
        internal bool IsPackable { get; }
        internal Exception? Error { get; }
    }
}
