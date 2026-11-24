using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static DotNetPackResult SignBatchBuildOutputs(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string packageOutputPath,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest> signAssemblies,
        ISet<DotNetRepositoryProjectResult> onlyPackToolProjects)
    {
        var result = new DotNetPackResult();
        var watch = Stopwatch.StartNew();

        try
        {
            var plans = ResolveBatchSigningPlans(projects, spec, packageOutputPath, logger, onlyPackToolProjects);
            var signedOutputCount = plans.Sum(static plan => plan.OutputDirectoryCount);
            var filePaths = plans
                .SelectMany(static plan => plan.Files)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (filePaths.Length > 0)
                signAssemblies(new DotNetReleaseBuildAssemblySigningRequest
                {
                    ReleasePath = spec.RootPath,
                    LocalStore = spec.CertificateStore,
                    CertificateThumbprint = spec.CertificateThumbprint!.Trim(),
                    TimeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim(),
                    OverwriteSigned = spec.OverwriteSignedAssemblies,
                    IncludePatterns = Array.Empty<string>(),
                    FilePaths = filePaths
                });

            watch.Stop();
            result.Duration = watch.Elapsed;
            result.Success = true;
            logger.Success($"MSBuild batch assembly signing completed for {signedOutputCount} output directorie(s), {filePaths.Length} file(s), in {FormatDuration(watch.Elapsed)}.");
            return result;
        }
        catch (Exception ex)
        {
            var error = ex is AggregateException { InnerExceptions.Count: 1 } aggregate ? aggregate.InnerExceptions[0] : ex;
            watch.Stop();
            result.Duration = watch.Elapsed;
            result.SigningFailed = true;
            result.ErrorMessage = $"MSBuild batch assembly signing failed. {error.Message}";
            return result;
        }
    }

    // Freshness cleanup evaluates each project and deletes stale primary outputs under that project's own roots, so
    // projects are cleaned in parallel. The first failure in project order is reported, as before.
    private static (TimeSpan Duration, bool ForceRebuild, string? Error) RemoveStaleBatchPackageOutputs(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var outcomes = new (bool Removed, bool RemovedIntermediate, TimeSpan Duration, string? Error)[projects.Count];
        var bufferedLoggers = new BufferedLogger?[projects.Count];
        void Clean(int index, ILogger projectLogger)
        {
            var removed = TryRemoveStalePrimaryPackageOutputs(projects[index], configuration, projectLogger, out _, out var removedIntermediate, out var duration, out var error);
            outcomes[index] = (removed, removedIntermediate, duration, error);
        }

        if (projects.Count == 1)
            Clean(0, logger);
        else
        {
            try
            {
                Parallel.For(
                    0,
                    projects.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = GetProjectPlanningMaxDegree(projects.Count) },
                    index =>
                    {
                        var projectLogger = new BufferedLogger { IsVerbose = logger.IsVerbose };
                        bufferedLoggers[index] = projectLogger;
                        Clean(index, projectLogger);
                    });
            }
            finally
            {
                foreach (var projectLogger in bufferedLoggers)
                    ReplayBufferedLogs(logger, projectLogger);
            }
        }

        var total = TimeSpan.Zero;
        var forceRebuild = false;
        foreach (var outcome in outcomes)
        {
            total += outcome.Duration;
            if (!outcome.Removed)
                return (total, forceRebuild, outcome.Error);
            forceRebuild |= !outcome.RemovedIntermediate;
        }
        return (total, forceRebuild, null);
    }

    // Each plan needs its own MSBuild evaluations (include patterns, output folders, pack-tool inputs), so a large
    // repository spent minutes resolving them one project at a time. They are independent: resolve them in parallel and
    // replay each project's log lines in project order.
    private static AssemblySigningPlan[] ResolveBatchSigningPlans(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string packageOutputPath,
        ILogger logger,
        ISet<DotNetRepositoryProjectResult> onlyPackToolProjects)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        if (projects.Count == 1)
            return new[] { ResolveBatchSigningPlan(projects[0], spec, configuration, packageOutputPath, logger, onlyPackToolProjects) };

        var plans = new AssemblySigningPlan[projects.Count];
        var bufferedLoggers = new BufferedLogger[projects.Count];
        var watch = Stopwatch.StartNew();
        logger.Info($"Resolving assembly signing plans for {projects.Count} project(s)...");
        try
        {
            Parallel.For(
                0,
                projects.Count,
                new ParallelOptions { MaxDegreeOfParallelism = GetProjectPlanningMaxDegree(projects.Count) },
                index =>
                {
                    var projectLogger = new BufferedLogger { IsVerbose = logger.IsVerbose };
                    bufferedLoggers[index] = projectLogger;
                    plans[index] = ResolveBatchSigningPlan(projects[index], spec, configuration, packageOutputPath, projectLogger, onlyPackToolProjects);
                });
        }
        finally
        {
            foreach (var projectLogger in bufferedLoggers)
                ReplayBufferedLogs(logger, projectLogger);
        }

        logger.Info($"Resolved assembly signing plans for {projects.Count} project(s) in {FormatDuration(watch.Elapsed)}.");
        return plans;
    }

    private static AssemblySigningPlan ResolveBatchSigningPlan(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        string configuration,
        string packageOutputPath,
        ILogger logger,
        ISet<DotNetRepositoryProjectResult> onlyPackToolProjects)
    {
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var includePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
        var outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
        var signingPlan = BuildAssemblySigningPlan(
            outputDirectories,
            includePatterns,
            ResolvePackToolIntermediateAssemblyPaths(
                project.CsprojPath,
                csprojDir,
                configuration,
                project.ProjectName,
                logger,
                includePatterns,
                spec.SignDependencyAssemblies,
                packageOutputPath,
                toolRuntimeIdentifiers: project.ToolPackageRuntimeIdentifiers),
            recursiveBuildOutputs: !onlyPackToolProjects.Contains(project));
        if (signingPlan.Files.Length == 0 && !spec.SignDependencyAssemblies)
        {
            var evaluatedIncludePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
            if (!SamePatterns(includePatterns, evaluatedIncludePatterns))
            {
                includePatterns = evaluatedIncludePatterns;
                outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                signingPlan = BuildAssemblySigningPlan(
                    outputDirectories,
                    includePatterns,
                    ResolvePackToolIntermediateAssemblyPaths(
                        project.CsprojPath,
                        csprojDir,
                        configuration,
                        project.ProjectName,
                        logger,
                        includePatterns,
                        includePreparedPublishOutput: false,
                        packageOutputPath,
                        toolRuntimeIdentifiers: project.ToolPackageRuntimeIdentifiers));
            }
        }
        logger.Info($"{project.ProjectName}: assembly signing include pattern(s): {string.Join(", ", includePatterns)}.");
        return signingPlan;
    }
}
