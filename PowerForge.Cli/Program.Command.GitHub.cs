using PowerForge;
using PowerForge.Cli;
using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class Program
{
    private static int CommandGitHub(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: powerforge github artifacts prune [--repo <owner/repo>] [--api-base-url <Url>] [--token-env <ENV>] [--token <TOKEN>] [--name <pattern[,pattern...]>] [--exclude <pattern[,pattern...]>] [--keep <N>] [--max-age-days <N>] [--max-delete <N>] [--dry-run|--apply] [--fail-on-delete-error] [--output json]");
            return 2;
        }

        var sub = argv[0].ToLowerInvariant();
        switch (sub)
        {
            case "artifacts":
            {
                var subArgs = argv.Skip(1).ToArray();
                if (subArgs.Length == 0 || subArgs[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || subArgs[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Usage: powerforge github artifacts prune [--repo <owner/repo>] [--api-base-url <Url>] [--token-env <ENV>] [--token <TOKEN>] [--name <pattern[,pattern...]>] [--exclude <pattern[,pattern...]>] [--keep <N>] [--max-age-days <N>] [--max-delete <N>] [--dry-run|--apply] [--fail-on-delete-error] [--output json]");
                    return 2;
                }

                if (!subArgs[0].Equals("prune", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Usage: powerforge github artifacts prune [options]");
                    return 2;
                }

                var pruneArgs = subArgs.Skip(1).ToArray();
                var outputJson = IsJsonOutput(pruneArgs);

                GitHubArtifactCleanupSpec spec;
                try
                {
                    spec = ParseGitHubArtifactPruneArgs(pruneArgs);
                }
                catch (Exception ex)
                {
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "github.artifacts.prune",
                            Success = false,
                            ExitCode = 2,
                            Error = ex.Message
                        });
                        return 2;
                    }

                    logger.Error(ex.Message);
                    Console.WriteLine("Usage: powerforge github artifacts prune [options]");
                    return 2;
                }

                try
                {
                    var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
                    var service = new GitHubArtifactCleanupService(cmdLogger);
                    var statusText = spec.DryRun
                        ? "Planning GitHub artifact cleanup"
                        : "Pruning GitHub artifacts";
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
                    if (outputJson)
                    {
                        WriteJson(new CliJsonEnvelope
                        {
                            SchemaVersion = OutputSchemaVersion,
                            Command = "github.artifacts.prune",
                            Success = false,
                            ExitCode = 1,
                            Error = ex.Message
                        });
                        return 1;
                    }

                    logger.Error(ex.Message);
                    return 1;
                }
            }
            default:
                Console.WriteLine("Usage: powerforge github artifacts prune [options]");
                return 2;
        }
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
                    if (++i >= argv.Length || !int.TryParse(argv[i], out var keep))
                        throw new InvalidOperationException("Invalid --keep value. Expected integer.");
                    if (keep < 0)
                        throw new InvalidOperationException("Invalid --keep value. Expected integer >= 0.");
                    spec.KeepLatestPerName = keep;
                    break;
                case "--max-age-days":
                case "--age-days":
                    if (++i >= argv.Length || !int.TryParse(argv[i], out var days))
                        throw new InvalidOperationException("Invalid --max-age-days value. Expected integer.");
                    spec.MaxAgeDays = days < 1 ? null : days;
                    break;
                case "--max-delete":
                case "--limit":
                    if (++i >= argv.Length || !int.TryParse(argv[i], out var maxDelete))
                        throw new InvalidOperationException("Invalid --max-delete value. Expected integer.");
                    if (maxDelete < 1)
                        throw new InvalidOperationException("Invalid --max-delete value. Expected integer >= 1.");
                    spec.MaxDelete = maxDelete;
                    break;
                case "--page-size":
                case "--per-page":
                    if (++i >= argv.Length || !int.TryParse(argv[i], out var pageSize))
                        throw new InvalidOperationException("Invalid --page-size value. Expected integer.");
                    spec.PageSize = pageSize;
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
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                        throw new InvalidOperationException($"Unknown option: {arg}");
                    break;
            }
        }

        spec.IncludeNames = include
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        spec.ExcludeNames = exclude
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(spec.Repository) && !string.IsNullOrWhiteSpace(repoEnv))
            spec.Repository = Environment.GetEnvironmentVariable(repoEnv)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spec.Token) && !string.IsNullOrWhiteSpace(tokenEnv))
            spec.Token = Environment.GetEnvironmentVariable(tokenEnv)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(spec.Repository))
            throw new InvalidOperationException("Repository is required. Provide --repo or set GITHUB_REPOSITORY.");
        if (string.IsNullOrWhiteSpace(spec.Token))
            throw new InvalidOperationException("GitHub token is required. Provide --token or set GITHUB_TOKEN.");

        return spec;
    }
}
