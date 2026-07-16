using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program {
    private const string HomeAssistantReleaseUsage = "Usage: powerforge homeassistant release --repo <owner/name> --pr-number <N> [--repository-root <path>] [--merge-sha <sha>] [--default-branch <name>] [--increment <auto|none|patch|minor|major>] [--token-env <ENV>] [--apply] [--publish] [--output json]";

    private static int CommandHomeAssistant(string[] filteredArgs, CliOptions cli, ILogger logger) {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || IsHelpArg(argv[0])) {
            Console.WriteLine(HomeAssistantReleaseUsage);
            return argv.Length == 0 ? 2 : 0;
        }

        if (!argv[0].Equals("release", StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine(HomeAssistantReleaseUsage);
            return 2;
        }

        var releaseArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(releaseArgs);
        try {
            var spec = ParseHomeAssistantReleaseSpec(releaseArgs);
            var (commandLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new HomeAssistantReleaseService(commandLogger);
            var status = spec.Apply ? "Releasing Home Assistant repository" : "Planning Home Assistant release";
            var result = RunWithStatus(outputJson, cli, status, () => service.Run(spec));
            if (outputJson) {
                WriteJson(new CliJsonEnvelope {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "homeassistant.release",
                    Success = result.Success,
                    ExitCode = result.Success ? 0 : 1,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.HomeAssistantReleaseResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return result.Success ? 0 : 1;
            }

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
                    Command = "homeassistant.release",
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

    private static HomeAssistantReleaseSpec ParseHomeAssistantReleaseSpec(string[] argv) {
        var repositorySlug = TryGetOptionValue(argv, "--repo")
            ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
            ?? string.Empty;
        var repositoryParts = repositorySlug.Split('/');
        if (repositoryParts.Length != 2 || repositoryParts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("--repo must use owner/name format.");

        var pullRequestValue = TryGetOptionValue(argv, "--pr-number")
            ?? Environment.GetEnvironmentVariable("POWERFORGE_PR_NUMBER");
        if (!int.TryParse(pullRequestValue, out var pullRequestNumber) || pullRequestNumber <= 0)
            throw new ArgumentException("--pr-number must be a positive integer.");

        var tokenEnvironmentName = TryGetOptionValue(argv, "--token-env") ?? "GITHUB_TOKEN";
        var token = Environment.GetEnvironmentVariable(tokenEnvironmentName);
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException($"GitHub token environment variable '{tokenEnvironmentName}' is empty.");

        HomeAssistantVersionIncrement? increment = null;
        var incrementValue = TryGetOptionValue(argv, "--increment");
        if (!string.IsNullOrWhiteSpace(incrementValue) && !incrementValue.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            if (!Enum.TryParse<HomeAssistantVersionIncrement>(incrementValue, ignoreCase: true, out var parsedIncrement))
                throw new ArgumentException("--increment must be auto, none, patch, minor, or major.");
            increment = parsedIncrement;
        }

        var apply = argv.Any(value => value.Equals("--apply", StringComparison.OrdinalIgnoreCase));
        var publish = argv.Any(value => value.Equals("--publish", StringComparison.OrdinalIgnoreCase));
        return new HomeAssistantReleaseSpec {
            RepositoryRoot = TryGetOptionValue(argv, "--repository-root") ?? Directory.GetCurrentDirectory(),
            Owner = repositoryParts[0],
            Repository = repositoryParts[1],
            Token = token,
            PullRequestNumber = pullRequestNumber,
            MergeCommitSha = TryGetOptionValue(argv, "--merge-sha") ?? Environment.GetEnvironmentVariable("POWERFORGE_MERGE_SHA"),
            DefaultBranch = TryGetOptionValue(argv, "--default-branch") ?? Environment.GetEnvironmentVariable("POWERFORGE_DEFAULT_BRANCH") ?? "main",
            Increment = increment,
            Apply = apply,
            Publish = publish,
            ApiBaseUrl = TryGetOptionValue(argv, "--api-base-url") ?? Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com"
        };
    }
}