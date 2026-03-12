using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Orchestrates repository cache/artifact cleanup and optional runner housekeeping from one spec.
/// </summary>
public sealed class GitHubHousekeepingService
{
    private readonly ILogger _logger;
    private readonly GitHubArtifactCleanupService _artifactService;
    private readonly GitHubActionsCacheCleanupService _cacheService;
    private readonly RunnerHousekeepingService _runnerService;

    /// <summary>
    /// Creates a housekeeping orchestrator.
    /// </summary>
    public GitHubHousekeepingService(
        ILogger logger,
        GitHubArtifactCleanupService? artifactService = null,
        GitHubActionsCacheCleanupService? cacheService = null,
        RunnerHousekeepingService? runnerService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _artifactService = artifactService ?? new GitHubArtifactCleanupService(logger);
        _cacheService = cacheService ?? new GitHubActionsCacheCleanupService(logger);
        _runnerService = runnerService ?? new RunnerHousekeepingService(logger);
    }

    /// <summary>
    /// Executes the configured housekeeping sections.
    /// </summary>
    public GitHubHousekeepingResult Run(GitHubHousekeepingSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var requested = new List<string>();
        var completed = new List<string>();
        var failed = new List<string>();
        static string? NormalizeNullable(string? value)
        {
            var text = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Trim();
        }

        static string NormalizeRequired(string? value)
        {
            var text = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim();
        }

        if (spec.Artifacts.Enabled) requested.Add("artifacts");
        if (spec.Caches.Enabled) requested.Add("caches");
        if (spec.Runner.Enabled) requested.Add("runner");

        var repositoryValue = spec.Repository;
        var tokenValue = spec.Token;

        var result = new GitHubHousekeepingResult
        {
            Repository = NormalizeNullable(repositoryValue),
            DryRun = spec.DryRun,
            RequestedSections = requested.ToArray()
        };

        if (requested.Count == 0)
        {
            result.Message = "No housekeeping sections are enabled.";
            return result;
        }

        var requiresRemoteIdentity = spec.Artifacts.Enabled || spec.Caches.Enabled;
        var repository = NormalizeRequired(repositoryValue);
        var token = NormalizeRequired(tokenValue);

        if (requiresRemoteIdentity)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                failed.Add("artifacts");
                if (spec.Caches.Enabled)
                    failed.Add("caches");
                result.Success = false;
                result.Message = "Repository is required for GitHub artifact/cache cleanup.";
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                if (spec.Artifacts.Enabled && !failed.Contains("artifacts", StringComparer.OrdinalIgnoreCase))
                    failed.Add("artifacts");
                if (spec.Caches.Enabled && !failed.Contains("caches", StringComparer.OrdinalIgnoreCase))
                    failed.Add("caches");
                result.Success = false;
                result.Message = string.IsNullOrWhiteSpace(result.Message)
                    ? "GitHub token is required for GitHub artifact/cache cleanup."
                    : result.Message + " GitHub token is required for GitHub artifact/cache cleanup.";
            }
        }

        if (spec.Artifacts.Enabled && !failed.Contains("artifacts", StringComparer.OrdinalIgnoreCase))
        {
            var artifactsResult = _artifactService.Prune(new GitHubArtifactCleanupSpec
            {
                ApiBaseUrl = spec.ApiBaseUrl,
                Repository = repository,
                Token = token,
                IncludeNames = spec.Artifacts.IncludeNames ?? Array.Empty<string>(),
                ExcludeNames = spec.Artifacts.ExcludeNames ?? Array.Empty<string>(),
                KeepLatestPerName = spec.Artifacts.KeepLatestPerName,
                MaxAgeDays = spec.Artifacts.MaxAgeDays,
                MaxDelete = spec.Artifacts.MaxDelete,
                PageSize = spec.Artifacts.PageSize,
                DryRun = spec.DryRun,
                FailOnDeleteError = spec.Artifacts.FailOnDeleteError
            });

            result.Artifacts = artifactsResult;
            if (artifactsResult.Success) completed.Add("artifacts"); else failed.Add("artifacts");
        }

        if (spec.Caches.Enabled && !failed.Contains("caches", StringComparer.OrdinalIgnoreCase))
        {
            var cachesResult = _cacheService.Prune(new GitHubActionsCacheCleanupSpec
            {
                ApiBaseUrl = spec.ApiBaseUrl,
                Repository = repository,
                Token = token,
                IncludeKeys = spec.Caches.IncludeKeys ?? Array.Empty<string>(),
                ExcludeKeys = spec.Caches.ExcludeKeys ?? Array.Empty<string>(),
                KeepLatestPerKey = spec.Caches.KeepLatestPerKey,
                MaxAgeDays = spec.Caches.MaxAgeDays,
                MaxDelete = spec.Caches.MaxDelete,
                PageSize = spec.Caches.PageSize,
                DryRun = spec.DryRun,
                FailOnDeleteError = spec.Caches.FailOnDeleteError
            });

            result.Caches = cachesResult;
            if (cachesResult.Success) completed.Add("caches"); else failed.Add("caches");
        }

        if (spec.Runner.Enabled)
        {
            var runnerResult = _runnerService.Clean(new RunnerHousekeepingSpec
            {
                RunnerTempPath = spec.Runner.RunnerTempPath,
                WorkRootPath = spec.Runner.WorkRootPath,
                RunnerRootPath = spec.Runner.RunnerRootPath,
                DiagnosticsRootPath = spec.Runner.DiagnosticsRootPath,
                ToolCachePath = spec.Runner.ToolCachePath,
                MinFreeGb = spec.Runner.MinFreeGb,
                AggressiveThresholdGb = spec.Runner.AggressiveThresholdGb,
                DiagnosticsRetentionDays = spec.Runner.DiagnosticsRetentionDays,
                ActionsRetentionDays = spec.Runner.ActionsRetentionDays,
                ToolCacheRetentionDays = spec.Runner.ToolCacheRetentionDays,
                DryRun = spec.DryRun,
                Aggressive = spec.Runner.Aggressive,
                CleanDiagnostics = spec.Runner.CleanDiagnostics,
                CleanRunnerTemp = spec.Runner.CleanRunnerTemp,
                CleanActionsCache = spec.Runner.CleanActionsCache,
                CleanToolCache = spec.Runner.CleanToolCache,
                ClearDotNetCaches = spec.Runner.ClearDotNetCaches,
                PruneDocker = spec.Runner.PruneDocker,
                IncludeDockerVolumes = spec.Runner.IncludeDockerVolumes,
                AllowSudo = spec.Runner.AllowSudo
            });

            result.Runner = runnerResult;
            if (runnerResult.Success) completed.Add("runner"); else failed.Add("runner");
        }

        result.CompletedSections = completed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        result.FailedSections = failed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        result.Success = result.FailedSections.Length == 0;

        if (!result.Success && string.IsNullOrWhiteSpace(result.Message))
            result.Message = $"Housekeeping failed for section(s): {string.Join(", ", result.FailedSections)}.";

        _logger.Info($"GitHub housekeeping requested: {string.Join(", ", result.RequestedSections)}.");
        _logger.Info($"GitHub housekeeping completed: {string.Join(", ", result.CompletedSections)}.");
        if (result.FailedSections.Length > 0)
            _logger.Warn($"GitHub housekeeping failed: {string.Join(", ", result.FailedSections)}.");

        return result;
    }
}
