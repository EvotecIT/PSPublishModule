using PowerForge;
using PowerForge.Cli;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private const string GitHubArtifactsPruneUsage = "Usage: powerforge github artifacts prune [--repo <owner/repo>] [--api-base-url <Url>] [--token-env <ENV>] [--token <TOKEN>] [--name <pattern[,pattern...]>] [--exclude <pattern[,pattern...]>] [--keep <N>] [--max-age-days <N>] [--max-delete <N>] [--dry-run|--apply] [--fail-on-delete-error] [--output json]";
    private const string GitHubCachesPruneUsage = "Usage: powerforge github caches prune [--repo <owner/repo>] [--api-base-url <Url>] [--token-env <ENV>] [--token <TOKEN>] [--key <pattern[,pattern...]>] [--exclude <pattern[,pattern...]>] [--keep <N>] [--max-age-days <N>] [--max-delete <N>] [--dry-run|--apply] [--fail-on-delete-error] [--output json]";
    private const string GitHubHousekeepingUsage = "Usage: powerforge github housekeeping [--config <file>] [--repo <owner/repo>] [--api-base-url <Url>] [--token-env <ENV>] [--token <TOKEN>] [--runner-min-free-gb <N>] [--dry-run|--apply] [--output json]";
    private const string GitHubRunnerCleanupUsage = "Usage: powerforge github runner cleanup [--runner-temp <path>] [--work-root <path>] [--runner-root <path>] [--diag-root <path>] [--tool-cache <path>] [--min-free-gb <N>] [--aggressive-threshold-gb <N>] [--diag-retention-days <N>] [--actions-retention-days <N>] [--workspaces-retention-days <N>] [--tool-cache-retention-days <N>] [--dry-run|--apply] [--aggressive] [--allow-sudo] [--clean-workspaces] [--skip-diagnostics] [--skip-runner-temp] [--skip-actions-cache] [--skip-workspaces] [--skip-tool-cache] [--skip-dotnet-cache] [--skip-docker] [--no-docker-volumes] [--output json]";

    private static int CommandGitHub(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubArtifactsPruneUsage);
            Console.WriteLine(GitHubCachesPruneUsage);
            Console.WriteLine(GitHubHousekeepingUsage);
            Console.WriteLine(GitHubRunnerCleanupUsage);
            return 2;
        }

        return argv[0].ToLowerInvariant() switch
        {
            "artifacts" => CommandGitHubArtifacts(argv.Skip(1).ToArray(), cli, logger),
            "caches" => CommandGitHubCaches(argv.Skip(1).ToArray(), cli, logger),
            "housekeeping" => CommandGitHubHousekeeping(argv.Skip(1).ToArray(), cli, logger),
            "runner" => CommandGitHubRunner(argv.Skip(1).ToArray(), cli, logger),
            _ => UnknownGitHubCommand()
        };
    }

    private static int CommandGitHubArtifacts(string[] argv, CliOptions cli, ILogger logger)
    {
        if (argv.Length == 0 || IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubArtifactsPruneUsage);
            return 2;
        }

        if (!argv[0].Equals("prune", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubArtifactsPruneUsage);
            return 2;
        }

        var pruneArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(pruneArgs);

        GitHubArtifactCleanupSpec spec;
        try
        {
            spec = ParseGitHubArtifactPruneArgs(pruneArgs);
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandArgumentError(outputJson, "github.artifacts.prune", ex.Message, GitHubArtifactsPruneUsage, logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new GitHubArtifactCleanupService(cmdLogger);
            var statusText = spec.DryRun ? "Planning GitHub artifact cleanup" : "Pruning GitHub artifacts";
            var result = RunWithStatus(outputJson, cli, statusText, () => service.Prune(spec));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.artifacts.prune",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.GitHubArtifactCleanupResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            var mode = result.DryRun ? "Dry run" : "Applied";
            logger.Info($"{mode}: {result.Repository}");
            logger.Info($"Scanned: {result.ScannedArtifacts}, matched: {result.MatchedArtifacts}");
            logger.Info($"Planned deletes: {result.PlannedDeletes} ({result.PlannedDeleteBytes} bytes)");
            if (!result.DryRun)
            {
                logger.Info($"Deleted: {result.DeletedArtifacts} ({result.DeletedBytes} bytes)");
                if (result.FailedDeletes > 0)
                    logger.Warn($"Failed deletes: {result.FailedDeletes}");
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
                logger.Warn(result.Message!);

            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandFailure(outputJson, "github.artifacts.prune", ex.Message, logger);
        }
    }

    private static int CommandGitHubCaches(string[] argv, CliOptions cli, ILogger logger)
    {
        if (argv.Length == 0 || IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubCachesPruneUsage);
            return 2;
        }

        if (!argv[0].Equals("prune", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubCachesPruneUsage);
            return 2;
        }

        var pruneArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(pruneArgs);

        GitHubActionsCacheCleanupSpec spec;
        try
        {
            spec = ParseGitHubCachesPruneArgs(pruneArgs);
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandArgumentError(outputJson, "github.caches.prune", ex.Message, GitHubCachesPruneUsage, logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new GitHubActionsCacheCleanupService(cmdLogger);
            var statusText = spec.DryRun ? "Planning GitHub cache cleanup" : "Pruning GitHub caches";
            var result = RunWithStatus(outputJson, cli, statusText, () => service.Prune(spec));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.caches.prune",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.GitHubActionsCacheCleanupResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            var mode = result.DryRun ? "Dry run" : "Applied";
            logger.Info($"{mode}: {result.Repository}");
            if (result.UsageBefore is not null)
                logger.Info($"GitHub cache usage before cleanup: {result.UsageBefore.ActiveCachesCount} caches, {result.UsageBefore.ActiveCachesSizeInBytes} bytes");
            logger.Info($"Scanned: {result.ScannedCaches}, matched: {result.MatchedCaches}");
            logger.Info($"Planned deletes: {result.PlannedDeletes} ({result.PlannedDeleteBytes} bytes)");
            if (!result.DryRun)
            {
                logger.Info($"Deleted: {result.DeletedCaches} ({result.DeletedBytes} bytes)");
                if (result.FailedDeletes > 0)
                    logger.Warn($"Failed deletes: {result.FailedDeletes}");
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
                logger.Warn(result.Message!);

            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandFailure(outputJson, "github.caches.prune", ex.Message, logger);
        }
    }

    private static int CommandGitHubRunner(string[] argv, CliOptions cli, ILogger logger)
    {
        if (argv.Length == 0 || IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubRunnerCleanupUsage);
            return 2;
        }

        if (!argv[0].Equals("cleanup", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubRunnerCleanupUsage);
            return 2;
        }

        var cleanupArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(cleanupArgs);

        RunnerHousekeepingSpec spec;
        try
        {
            spec = ParseGitHubRunnerCleanupArgs(cleanupArgs);
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandArgumentError(outputJson, "github.runner.cleanup", ex.Message, GitHubRunnerCleanupUsage, logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new RunnerHousekeepingService(cmdLogger);
            var statusText = spec.DryRun ? "Planning runner housekeeping" : "Cleaning runner working sets";
            var result = RunWithStatus(outputJson, cli, statusText, () => service.Clean(spec));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.runner.cleanup",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.RunnerHousekeepingResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            var mode = result.DryRun ? "Dry run" : "Applied";
            logger.Info($"{mode}: {result.RunnerRootPath}");
            logger.Info($"Free before: {result.FreeBytesBefore} bytes");
            logger.Info($"Free after: {result.FreeBytesAfter} bytes");
            logger.Info($"Aggressive cleanup: {(result.AggressiveApplied ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(result.Message))
                logger.Warn(result.Message!);

            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandFailure(outputJson, "github.runner.cleanup", ex.Message, logger);
        }
    }

    private static int CommandGitHubHousekeeping(string[] argv, CliOptions cli, ILogger logger)
    {
        var outputJson = IsJsonOutput(argv);
        var outputOptions = GetGitHubHousekeepingOutputOptions();
        var reportService = new GitHubHousekeepingReportService();
        if (argv.Length > 0 && IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubHousekeepingUsage);
            return 2;
        }

        GitHubHousekeepingSpec spec;
        try
        {
            spec = ParseGitHubHousekeepingArgs(argv);
        }
        catch (Exception ex)
        {
            WriteGitHubHousekeepingOutputs(reportService, reportService.CreateFailureReport(2, ex.Message), outputOptions);
            return WriteGitHubCommandArgumentError(outputJson, "github.housekeeping", ex.Message, GitHubHousekeepingUsage, logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new GitHubHousekeepingService(cmdLogger);
            var statusText = spec.DryRun ? "Planning GitHub housekeeping" : "Running GitHub housekeeping";
            var result = RunWithStatus(outputJson, cli, statusText, () => service.Run(spec));
            var exitCode = result.Success ? 0 : 1;
            WriteGitHubHousekeepingOutputs(reportService, reportService.CreateSuccessReport(result), outputOptions);

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.housekeeping",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.GitHubHousekeepingResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            var mode = result.DryRun ? "Dry run" : "Applied";
            logger.Info($"{mode}: {result.Repository ?? "(runner-only)"}");
            logger.Info($"Requested sections: {string.Join(", ", result.RequestedSections)}");
            if (result.CompletedSections.Length > 0)
                logger.Info($"Completed sections: {string.Join(", ", result.CompletedSections)}");
            if (result.FailedSections.Length > 0)
                logger.Warn($"Failed sections: {string.Join(", ", result.FailedSections)}");

            if (result.Caches?.UsageBefore is not null)
                logger.Info($"GitHub cache usage before cleanup: {result.Caches.UsageBefore.ActiveCachesCount} caches, {result.Caches.UsageBefore.ActiveCachesSizeInBytes} bytes");
            if (result.Caches?.UsageAfter is not null)
                logger.Info($"GitHub cache usage after cleanup: {result.Caches.UsageAfter.ActiveCachesCount} caches, {result.Caches.UsageAfter.ActiveCachesSizeInBytes} bytes");

            if (!string.IsNullOrWhiteSpace(result.Message))
                logger.Warn(result.Message!);

            return exitCode;
        }
        catch (Exception ex)
        {
            WriteGitHubHousekeepingOutputs(reportService, reportService.CreateFailureReport(1, ex.Message), outputOptions);
            return WriteGitHubCommandFailure(outputJson, "github.housekeeping", ex.Message, logger);
        }
    }

    private static int UnknownGitHubCommand()
    {
        Console.WriteLine(GitHubArtifactsPruneUsage);
        Console.WriteLine(GitHubCachesPruneUsage);
        Console.WriteLine(GitHubHousekeepingUsage);
        Console.WriteLine(GitHubRunnerCleanupUsage);
        return 2;
    }

    private static int WriteGitHubCommandArgumentError(bool outputJson, string command, string error, string usage, ILogger logger)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = 2,
                Error = error
            });
            return 2;
        }

        logger.Error(error);
        Console.WriteLine(usage);
        return 2;
    }

    private static int WriteGitHubCommandFailure(bool outputJson, string command, string error, ILogger logger)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = command,
                Success = false,
                ExitCode = 1,
                Error = error
            });
            return 1;
        }

        logger.Error(error);
        return 1;
    }

    private static GitHubArtifactCleanupSpec ParseGitHubArtifactPruneArgs(string[] argv)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        var spec = new GitHubArtifactCleanupSpec();
        var tokenEnv = "GITHUB_TOKEN";
        var repoEnv = "GITHUB_REPOSITORY";

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg.ToLowerInvariant())
            {
                case "--repo":
                case "--repository":
                    spec.Repository = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token":
                    spec.Token = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token-env":
                    tokenEnv = ++i < argv.Length ? argv[i] : tokenEnv;
                    break;
                case "--repo-env":
                    repoEnv = ++i < argv.Length ? argv[i] : repoEnv;
                    break;
                case "--api-base-url":
                case "--api-url":
                    spec.ApiBaseUrl = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--name":
                case "--names":
                case "--include":
                    if (++i < argv.Length)
                        include.AddRange(SplitCsv(argv[i]));
                    break;
                case "--exclude":
                case "--exclude-name":
                case "--exclude-names":
                    if (++i < argv.Length)
                        exclude.AddRange(SplitCsv(argv[i]));
                    break;
                case "--keep":
                case "--keep-latest":
                    spec.KeepLatestPerName = ParseRequiredInt(argv, ref i, "--keep", minimum: 0);
                    break;
                case "--max-age-days":
                case "--age-days":
                    spec.MaxAgeDays = ParseOptionalPositiveInt(argv, ref i, "--max-age-days");
                    break;
                case "--max-delete":
                case "--limit":
                    spec.MaxDelete = ParseRequiredInt(argv, ref i, "--max-delete", minimum: 1);
                    break;
                case "--page-size":
                case "--per-page":
                    spec.PageSize = ParseRequiredInt(argv, ref i, "--page-size", minimum: 1);
                    break;
                case "--dry-run":
                    spec.DryRun = true;
                    break;
                case "--apply":
                    spec.DryRun = false;
                    break;
                case "--fail-on-delete-error":
                    spec.FailOnDeleteError = true;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        spec.IncludeNames = NormalizeCsvValues(include);
        spec.ExcludeNames = NormalizeCsvValues(exclude);
        ResolveGitHubIdentity(spec, tokenEnv, repoEnv);
        return spec;
    }

    private static GitHubActionsCacheCleanupSpec ParseGitHubCachesPruneArgs(string[] argv)
    {
        var include = new List<string>();
        var exclude = new List<string>();
        var spec = new GitHubActionsCacheCleanupSpec();
        var tokenEnv = "GITHUB_TOKEN";
        var repoEnv = "GITHUB_REPOSITORY";

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg.ToLowerInvariant())
            {
                case "--repo":
                case "--repository":
                    spec.Repository = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token":
                    spec.Token = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token-env":
                    tokenEnv = ++i < argv.Length ? argv[i] : tokenEnv;
                    break;
                case "--repo-env":
                    repoEnv = ++i < argv.Length ? argv[i] : repoEnv;
                    break;
                case "--api-base-url":
                case "--api-url":
                    spec.ApiBaseUrl = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--key":
                case "--keys":
                case "--include":
                    if (++i < argv.Length)
                        include.AddRange(SplitCsv(argv[i]));
                    break;
                case "--exclude":
                case "--exclude-key":
                case "--exclude-keys":
                    if (++i < argv.Length)
                        exclude.AddRange(SplitCsv(argv[i]));
                    break;
                case "--keep":
                case "--keep-latest":
                    spec.KeepLatestPerKey = ParseRequiredInt(argv, ref i, "--keep", minimum: 0);
                    break;
                case "--max-age-days":
                case "--age-days":
                    spec.MaxAgeDays = ParseOptionalPositiveInt(argv, ref i, "--max-age-days");
                    break;
                case "--max-delete":
                case "--limit":
                    spec.MaxDelete = ParseRequiredInt(argv, ref i, "--max-delete", minimum: 1);
                    break;
                case "--page-size":
                case "--per-page":
                    spec.PageSize = ParseRequiredInt(argv, ref i, "--page-size", minimum: 1);
                    break;
                case "--dry-run":
                    spec.DryRun = true;
                    break;
                case "--apply":
                    spec.DryRun = false;
                    break;
                case "--fail-on-delete-error":
                    spec.FailOnDeleteError = true;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        spec.IncludeKeys = NormalizeCsvValues(include);
        spec.ExcludeKeys = NormalizeCsvValues(exclude);
        ResolveGitHubIdentity(spec, tokenEnv, repoEnv);
        return spec;
    }

    private static GitHubHousekeepingSpec ParseGitHubHousekeepingArgs(string[] argv)
    {
        var configPath = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultGitHubHousekeepingConfig(Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(configPath))
            throw new InvalidOperationException("Housekeeping config file not found. Provide --config or add .powerforge/github-housekeeping.json.");

        var (spec, fullConfigPath) = LoadGitHubHousekeepingSpecWithPath(configPath!);
        ResolveGitHubHousekeepingSpecPaths(spec, fullConfigPath);

        var tokenEnv = string.IsNullOrWhiteSpace(spec.TokenEnvName) ? "GITHUB_TOKEN" : spec.TokenEnvName.Trim();

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg.ToLowerInvariant())
            {
                case "--config":
                    i++;
                    break;
                case "--repo":
                case "--repository":
                    spec.Repository = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token":
                    spec.Token = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--token-env":
                    tokenEnv = ++i < argv.Length ? argv[i] : tokenEnv;
                    break;
                case "--api-base-url":
                case "--api-url":
                    spec.ApiBaseUrl = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--runner-min-free-gb":
                    spec.Runner.MinFreeGb = ParseOptionalPositiveInt(argv, ref i, "--runner-min-free-gb");
                    break;
                case "--dry-run":
                    spec.DryRun = true;
                    break;
                case "--apply":
                    spec.DryRun = false;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        if ((spec.Artifacts?.Enabled ?? false) || (spec.Caches?.Enabled ?? false))
        {
            if (string.IsNullOrWhiteSpace(spec.Repository))
                spec.Repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(spec.Token) && !string.IsNullOrWhiteSpace(tokenEnv))
                spec.Token = Environment.GetEnvironmentVariable(tokenEnv)?.Trim() ?? string.Empty;
        }

        spec.TokenEnvName = tokenEnv;
        return spec;
    }

    private static RunnerHousekeepingSpec ParseGitHubRunnerCleanupArgs(string[] argv)
    {
        var spec = new RunnerHousekeepingSpec();

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg.ToLowerInvariant())
            {
                case "--runner-temp":
                    spec.RunnerTempPath = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--work-root":
                    spec.WorkRootPath = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--runner-root":
                    spec.RunnerRootPath = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--diag-root":
                case "--diagnostics-root":
                    spec.DiagnosticsRootPath = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--tool-cache":
                case "--tool-cache-path":
                    spec.ToolCachePath = ++i < argv.Length ? argv[i] : string.Empty;
                    break;
                case "--min-free-gb":
                    spec.MinFreeGb = ParseOptionalPositiveInt(argv, ref i, "--min-free-gb");
                    break;
                case "--aggressive-threshold-gb":
                    spec.AggressiveThresholdGb = ParseOptionalPositiveInt(argv, ref i, "--aggressive-threshold-gb");
                    break;
                case "--diag-retention-days":
                    spec.DiagnosticsRetentionDays = ParseRequiredInt(argv, ref i, "--diag-retention-days", minimum: 0);
                    break;
                case "--actions-retention-days":
                    spec.ActionsRetentionDays = ParseRequiredInt(argv, ref i, "--actions-retention-days", minimum: 0);
                    break;
                case "--workspaces-retention-days":
                case "--workspace-retention-days":
                    spec.WorkspacesRetentionDays = ParseRequiredInt(argv, ref i, "--workspaces-retention-days", minimum: 0);
                    break;
                case "--tool-cache-retention-days":
                    spec.ToolCacheRetentionDays = ParseRequiredInt(argv, ref i, "--tool-cache-retention-days", minimum: 0);
                    break;
                case "--dry-run":
                    spec.DryRun = true;
                    break;
                case "--apply":
                    spec.DryRun = false;
                    break;
                case "--aggressive":
                    spec.Aggressive = true;
                    break;
                case "--allow-sudo":
                    spec.AllowSudo = true;
                    break;
                case "--clean-workspaces":
                    spec.CleanWorkspaces = true;
                    break;
                case "--skip-diagnostics":
                    spec.CleanDiagnostics = false;
                    break;
                case "--skip-runner-temp":
                    spec.CleanRunnerTemp = false;
                    break;
                case "--skip-actions-cache":
                    spec.CleanActionsCache = false;
                    break;
                case "--skip-workspaces":
                case "--skip-workspace-cleanup":
                    spec.CleanWorkspaces = false;
                    break;
                case "--skip-tool-cache":
                    spec.CleanToolCache = false;
                    break;
                case "--skip-dotnet-cache":
                    spec.ClearDotNetCaches = false;
                    break;
                case "--skip-docker":
                    spec.PruneDocker = false;
                    break;
                case "--no-docker-volumes":
                    spec.IncludeDockerVolumes = false;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        return spec;
    }

    private static void ResolveGitHubIdentity(GitHubArtifactCleanupSpec spec, string tokenEnv, string repoEnv)
    {
        if (string.IsNullOrWhiteSpace(spec.Repository) && !string.IsNullOrWhiteSpace(repoEnv))
            spec.Repository = Environment.GetEnvironmentVariable(repoEnv)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spec.Token) && !string.IsNullOrWhiteSpace(tokenEnv))
            spec.Token = Environment.GetEnvironmentVariable(tokenEnv)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(spec.Repository))
            throw new InvalidOperationException("Repository is required. Provide --repo or set GITHUB_REPOSITORY.");
        if (string.IsNullOrWhiteSpace(spec.Token))
            throw new InvalidOperationException("GitHub token is required. Provide --token or set GITHUB_TOKEN.");
    }

    private static void ResolveGitHubIdentity(GitHubActionsCacheCleanupSpec spec, string tokenEnv, string repoEnv)
    {
        if (string.IsNullOrWhiteSpace(spec.Repository) && !string.IsNullOrWhiteSpace(repoEnv))
            spec.Repository = Environment.GetEnvironmentVariable(repoEnv)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spec.Token) && !string.IsNullOrWhiteSpace(tokenEnv))
            spec.Token = Environment.GetEnvironmentVariable(tokenEnv)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(spec.Repository))
            throw new InvalidOperationException("Repository is required. Provide --repo or set GITHUB_REPOSITORY.");
        if (string.IsNullOrWhiteSpace(spec.Token))
            throw new InvalidOperationException("GitHub token is required. Provide --token or set GITHUB_TOKEN.");
    }

    private static string[] NormalizeCsvValues(IEnumerable<string> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ParseRequiredInt(string[] argv, ref int index, string optionName, int minimum)
    {
        if (++index >= argv.Length || !int.TryParse(argv[index], out var value))
            throw new InvalidOperationException($"Invalid {optionName} value. Expected integer.");
        if (value < minimum)
            throw new InvalidOperationException($"Invalid {optionName} value. Expected integer >= {minimum}.");
        return value;
    }

    private static int? ParseOptionalPositiveInt(string[] argv, ref int index, string optionName)
    {
        if (++index >= argv.Length || !int.TryParse(argv[index], out var value))
            throw new InvalidOperationException($"Invalid {optionName} value. Expected integer.");
        return value < 1 ? null : value;
    }

    private static void ThrowOnUnknownOption(string arg)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unknown option: {arg}");
    }

    private static bool IsHelpArg(string arg)
        => arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("--help", StringComparison.OrdinalIgnoreCase);
}
