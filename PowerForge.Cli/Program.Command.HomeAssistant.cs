using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program {
    private const string HomeAssistantReleaseUsage = "Usage: powerforge homeassistant release <prepare|build|publish> [options] [--output json]";

    private static int CommandHomeAssistant(string[] filteredArgs, CliOptions cli, ILogger logger) {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || IsHelpArg(argv[0])) {
            Console.WriteLine(HomeAssistantReleaseUsage);
            return argv.Length == 0 ? 2 : 0;
        }

        if (!argv[0].Equals("release", StringComparison.OrdinalIgnoreCase) ||
            argv.Length < 2 || IsHelpArg(argv[1])) {
            Console.WriteLine(HomeAssistantReleaseUsage);
            return argv.Length >= 2 && IsHelpArg(argv[1]) ? 0 : 2;
        }

        var stage = argv[1].ToLowerInvariant();
        var releaseArgs = argv.Skip(2).ToArray();
        var outputJson = IsJsonOutput(releaseArgs);
        try {
            var (commandLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new HomeAssistantReleaseService(commandLogger);
            var result = stage switch {
                "prepare" => RunWithStatus(
                    outputJson,
                    cli,
                    releaseArgs.Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase))
                        ? "Preparing Home Assistant release metadata"
                        : "Planning Home Assistant release",
                    () => service.Prepare(ParsePrepareSpec(releaseArgs))),
                "build" => RunWithStatus(
                    outputJson,
                    cli,
                    "Building Home Assistant release assets",
                    () => service.Build(ParseBuildSpec(releaseArgs))),
                "publish" => RunWithStatus(
                    outputJson,
                    cli,
                    "Publishing Home Assistant release",
                    () => service.Publish(ParsePublishSpec(releaseArgs))),
                _ => throw new ArgumentException("Release stage must be prepare, build, or publish.")
            };

            if (outputJson) {
                WriteJson(new CliJsonEnvelope {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "homeassistant.release." + stage,
                    Success = result.Success,
                    ExitCode = result.Success ? 0 : 1,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.HomeAssistantReleaseResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return result.Success ? 0 : 1;
            }

            logger.Info($"Action: {result.Action}");
            logger.Info($"Repository type: {result.RepositoryKind}");
            if (!string.IsNullOrWhiteSpace(result.ReleaseVersion)) logger.Info($"Version: {result.ReleaseVersion}");
            if (!string.IsNullOrWhiteSpace(result.TagName)) logger.Info($"Tag: {result.TagName}");
            if (!string.IsNullOrWhiteSpace(result.ReleaseUrl)) logger.Success($"Release: {result.ReleaseUrl}");
            logger.Info(result.Message);
            return result.Success ? 0 : 1;
        } catch (Exception ex) {
            if (outputJson) {
                WriteJson(new CliJsonEnvelope {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "homeassistant.release." + stage,
                    Success = false,
                    ExitCode = 1,
                    Error = ex.Message
                });
            } else {
                logger.Error(ex.Message);
                Console.WriteLine(HomeAssistantReleaseUsage);
            }

            return 1;
        }
    }

    private static HomeAssistantReleasePrepareSpec ParsePrepareSpec(string[] argv) {
        var (owner, repository) = ParseRepository(argv);
        var pullRequestNumber = ParsePositiveInteger(argv, "--pr-number", "POWERFORGE_PR_NUMBER");
        HomeAssistantVersionIncrement? increment = null;
        var incrementValue = TryGetOptionValue(argv, "--increment");
        if (!string.IsNullOrWhiteSpace(incrementValue) && !incrementValue.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            if (!Enum.TryParse<HomeAssistantVersionIncrement>(incrementValue, ignoreCase: true, out var parsedIncrement))
                throw new ArgumentException("--increment must be auto, none, patch, minor, or major.");
            increment = parsedIncrement;
        }

        return new HomeAssistantReleasePrepareSpec {
            RepositoryRoot = TryGetOptionValue(argv, "--repository-root") ?? Directory.GetCurrentDirectory(),
            Owner = owner,
            Repository = repository,
            Token = ReadToken(argv),
            PullRequestNumber = pullRequestNumber,
            MergeCommitSha = TryGetOptionValue(argv, "--merge-sha") ?? Environment.GetEnvironmentVariable("POWERFORGE_MERGE_SHA"),
            DefaultBranch = TryGetOptionValue(argv, "--default-branch") ?? Environment.GetEnvironmentVariable("POWERFORGE_DEFAULT_BRANCH") ?? "main",
            Increment = increment,
            Apply = argv.Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase)),
            ApiBaseUrl = ReadApiBaseUrl(argv),
            ServerUrl = TryGetOptionValue(argv, "--server-url") ?? Environment.GetEnvironmentVariable("GITHUB_SERVER_URL") ?? "https://github.com"
        };
    }

    private static HomeAssistantReleaseBuildSpec ParseBuildSpec(string[] argv)
        => new() {
            RepositoryRoot = TryGetOptionValue(argv, "--repository-root") ?? Directory.GetCurrentDirectory(),
            ReleaseVersion = RequiredOption(argv, "--release-version"),
            ReleaseCommitSha = RequiredOption(argv, "--release-commit")
        };

    private static HomeAssistantReleasePublishSpec ParsePublishSpec(string[] argv) {
        var (owner, repository) = ParseRepository(argv);
        return new HomeAssistantReleasePublishSpec {
            Owner = owner,
            Repository = repository,
            Token = ReadToken(argv),
            PullRequestNumber = ParsePositiveInteger(argv, "--pr-number", "POWERFORGE_PR_NUMBER"),
            MergeCommitSha = TryGetOptionValue(argv, "--merge-sha") ?? Environment.GetEnvironmentVariable("POWERFORGE_MERGE_SHA") ?? string.Empty,
            ReleaseVersion = RequiredOption(argv, "--release-version"),
            ReleaseCommitSha = RequiredOption(argv, "--release-commit"),
            RequiredAssetName = TryGetOptionValue(argv, "--required-asset") ?? string.Empty,
            AssetFilePath = TryGetOptionValue(argv, "--asset") ?? string.Empty,
            ApiBaseUrl = ReadApiBaseUrl(argv)
        };
    }

    private static (string Owner, string Repository) ParseRepository(string[] argv) {
        var slug = TryGetOptionValue(argv, "--repo")
            ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? string.Empty;
        var parts = slug.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("--repo must use owner/name format.");
        return (parts[0], parts[1]);
    }

    private static int ParsePositiveInteger(string[] argv, string option, string environmentVariable) {
        var value = TryGetOptionValue(argv, option) ?? Environment.GetEnvironmentVariable(environmentVariable);
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentException($"{option} must be a positive integer.");
        return parsed;
    }

    private static string ReadToken(string[] argv) {
        var environmentName = TryGetOptionValue(argv, "--token-env") ?? "GITHUB_TOKEN";
        return Environment.GetEnvironmentVariable(environmentName)
            ?? throw new ArgumentException($"GitHub token environment variable '{environmentName}' is empty.");
    }

    private static string ReadApiBaseUrl(string[] argv)
        => TryGetOptionValue(argv, "--api-base-url")
           ?? Environment.GetEnvironmentVariable("GITHUB_API_URL")
           ?? "https://api.github.com";

    private static string RequiredOption(string[] argv, string option)
        => TryGetOptionValue(argv, option)
           ?? throw new ArgumentException($"{option} is required.");
}
