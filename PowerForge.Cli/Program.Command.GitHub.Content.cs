using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int CommandGitHubContent(string[] argv, CliOptions cli, ILogger logger)
    {
        if (argv.Length == 0 || IsHelpArg(argv[0]) || !argv[0].Equals("sync", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(GitHubContentSyncUsage);
            return 2;
        }

        var syncArgs = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(syncArgs);
        GitHubRepositoryContentSpec spec;
        string configPath;
        string? restrictedOutputRoot;
        try
        {
            (spec, configPath, restrictedOutputRoot) = ParseGitHubContentSyncArgs(syncArgs);
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandArgumentError(outputJson, "github.content.sync", ex.Message, GitHubContentSyncUsage, logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new GitHubRepositoryContentService(cmdLogger);
            var baseDirectory = FindGitRepositoryRoot(configPath);
            var result = RunWithStatus(
                outputJson,
                cli,
                "Synchronizing GitHub repository content",
                () => service.Sync(spec, baseDirectory, restrictedOutputRoot));
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.content.sync",
                    Success = result.Success,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.GitHubRepositoryContentResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            logger.Info($"Sponsors: {result.CurrentSponsors.Length} current, {result.FormerSponsors.Length} former.");
            foreach (var document in result.Documents)
            {
                var state = document.Created ? "created" : document.Appended ? "appended" : document.Changed ? "updated" : "unchanged";
                logger.Info($"{document.Path}: {state}");
            }
            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandFailure(outputJson, "github.content.sync", ex.Message, logger);
        }
    }

    private static (GitHubRepositoryContentSpec Spec, string ConfigPath, string? RestrictedOutputRoot) ParseGitHubContentSyncArgs(string[] argv)
    {
        var configPath = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            configPath = FindDefaultGitHubContentConfig(Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(configPath))
            throw new InvalidOperationException("GitHub content config file not found. Provide --config or add .powerforge/github-content.json.");

        var (spec, fullConfigPath) = LoadGitHubRepositoryContentSpecWithPath(configPath!);
        spec.Sponsors ??= new GitHubSponsorsContentSpec();
        string? restrictedOutputRoot = null;

        for (var index = 0; index < argv.Length; index++)
        {
            var arg = argv[index];
            switch (arg.ToLowerInvariant())
            {
                case "--config":
                    index++;
                    break;
                case "--login":
                case "--sponsorable":
                    spec.Sponsors.SponsorableLogin = ReadRequiredValue(argv, ref index, arg);
                    break;
                case "--graphql-endpoint":
                case "--api-url":
                    spec.GraphQlEndpoint = ReadRequiredValue(argv, ref index, arg);
                    break;
                case "--token":
                    spec.Token = ReadRequiredValue(argv, ref index, arg);
                    break;
                case "--token-env":
                    spec.TokenEnvName = ReadRequiredValue(argv, ref index, arg);
                    break;
                case "--restrict-output-root":
                    restrictedOutputRoot = Path.GetFullPath(ReadRequiredValue(argv, ref index, arg));
                    break;
                case "--output":
                    index++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        return (spec, fullConfigPath, restrictedOutputRoot);
    }

    private static string? FindDefaultGitHubContentConfig(string baseDirectory)
    {
        var candidates = new[]
        {
            "github-content.json",
            Path.Combine(".powerforge", "github-content.json"),
            Path.Combine(".github", "powerforge", "github-content.json")
        };

        foreach (var directory in EnumerateSelfAndParents(baseDirectory))
        {
            foreach (var relativePath in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return null;
    }

    private static string ReadRequiredValue(string[] argv, ref int index, string optionName)
    {
        if (++index >= argv.Length || string.IsNullOrWhiteSpace(argv[index]))
            throw new InvalidOperationException($"{optionName} requires a value.");
        return argv[index];
    }

    private static (GitHubRepositoryContentSpec Value, string FullPath) LoadGitHubRepositoryContentSpecWithPath(string path)
    {
        var fullPath = ResolveExistingFilePath(path);
        var json = File.ReadAllText(fullPath);
        var spec = CliJson.DeserializeOrThrow(json, CliJson.Context.GitHubRepositoryContentSpec, fullPath);
        return (spec, fullPath);
    }

    private static string FindGitRepositoryRoot(string configPath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return current.FullName;
            current = current.Parent;
        }

        return Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
    }
}
