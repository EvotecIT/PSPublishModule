using System.Text;
using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ArchitectureUsage =
        "Usage: powerforge architecture verify [--config <architecture.json>] [--base <git-ref>] [--head <git-ref>] [--working-tree] [--run-evidence] [--report-json <path>] [--summary-markdown <path>] [--output json]";

    private static int CommandArchitecture(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ArchitectureUsage);
            return 2;
        }

        if (!argv[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(ArchitectureUsage);
            return 2;
        }

        var args = argv.Skip(1).ToArray();
        var outputJson = IsJsonOutput(args);
        try
        {
            var configPath = TryGetOptionValue(args, "--config")
                             ?? FindDefaultArchitectureConfig(Directory.GetCurrentDirectory());
            if (string.IsNullOrWhiteSpace(configPath))
                return WriteArchitectureError(outputJson, logger, 2, "Missing --config and no default architecture config found.");

            configPath = Path.GetFullPath(configPath);
            var service = new RepositoryArchitectureService();
            var spec = service.Load(configPath);
            var repositoryRoot = ResolveArchitectureRepositoryRoot(spec, configPath);
            var changedFiles = ResolveArchitectureChangedFiles(
                repositoryRoot,
                TryGetOptionValue(args, "--base"),
                TryGetOptionValue(args, "--head"),
                args.Any(arg => arg.Equals("--working-tree", StringComparison.OrdinalIgnoreCase)));
            var report = service.Verify(spec, configPath, changedFiles);
            WorkspaceValidationPlan? validationPlan = null;
            WorkspaceValidationResult? validation = null;

            if (report.Succeeded && args.Any(arg => arg.Equals("--run-evidence", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(spec.WorkspaceValidationConfig))
                    return WriteArchitectureError(outputJson, logger, 2, "--run-evidence requires workspaceValidationConfig in the architecture policy.");

                var workspacePath = Path.GetFullPath(Path.Combine(report.RepositoryRoot, spec.WorkspaceValidationConfig!));
                var loadedWorkspace = LoadWorkspaceValidationSpecWithPath(workspacePath);
                var request = new WorkspaceValidationRequest
                {
                    ProfileName = string.IsNullOrWhiteSpace(spec.WorkspaceValidationProfile)
                        ? "architecture"
                        : spec.WorkspaceValidationProfile,
                    IncludedStepIds = report.RequiredValidationStepIds,
                    RestrictToIncludedStepIds = true,
                    FailOnSkippedSteps = true,
                    CaptureOutput = outputJson,
                    CaptureError = outputJson
                };
                var workspaceService = new WorkspaceValidationService();
                var validationErrors = workspaceService.Validate(loadedWorkspace.Value, loadedWorkspace.FullPath, request);
                if (validationErrors.Length > 0)
                {
                    foreach (var error in validationErrors)
                    {
                        report.Issues = report.Issues.Append(new RepositoryArchitectureIssue
                        {
                            Severity = RepositoryArchitectureIssueSeverity.Error,
                            Code = "ARC303",
                            Message = error,
                            Path = spec.WorkspaceValidationConfig
                        }).ToArray();
                    }
                    report.Succeeded = false;
                }
                else
                {
                    validationPlan = workspaceService.Plan(loadedWorkspace.Value, loadedWorkspace.FullPath, request);
                    validation = workspaceService.RunAsync(loadedWorkspace.Value, loadedWorkspace.FullPath, request)
                        .GetAwaiter()
                        .GetResult();
                }
            }

            var result = new RepositoryArchitectureExecutionResult
            {
                Architecture = report,
                ValidationPlan = validationPlan,
                Validation = validation
            };

            WriteArchitectureArtifacts(
                result,
                TryGetOptionValue(args, "--report-json"),
                TryGetOptionValue(args, "--summary-markdown"));

            var exitCode = result.Succeeded ? 0 : 1;
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "architecture.verify",
                    Success = result.Succeeded,
                    ExitCode = exitCode,
                    Error = result.Succeeded ? null : BuildArchitectureFailureSummary(result),
                    Config = "architecture",
                    ConfigPath = configPath,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.RepositoryArchitectureExecutionResult)
                });
                return exitCode;
            }

            WriteArchitectureText(result, logger);
            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteArchitectureError(outputJson, logger, 1, ex.Message);
        }
    }

    private static string? FindDefaultArchitectureConfig(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(".powerforge", "architecture.json"),
            Path.Combine("Build", "architecture.json"),
            "architecture.json"
        };

        foreach (var directory in EnumerateSelfAndParents(baseDirectory))
        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(Path.Combine(directory, candidate));
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static string ResolveArchitectureRepositoryRoot(RepositoryArchitectureSpec spec, string configPath)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(string.IsNullOrWhiteSpace(spec.RepositoryRoot)
            ? configDirectory
            : Path.Combine(configDirectory, spec.RepositoryRoot!));
    }

    private static string[] ResolveArchitectureChangedFiles(
        string repositoryRoot,
        string? baseRef,
        string? headRef,
        bool includeWorkingTree)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var git = new GitClient();

        if (!string.IsNullOrWhiteSpace(baseRef))
        {
            var resolvedHead = string.IsNullOrWhiteSpace(headRef) ? "HEAD" : headRef!;
            AddGitPaths(git.RunRawAsync(
                    repositoryRoot,
                    ["diff", "--name-only", "--diff-filter=ACMRD", $"{baseRef}...{resolvedHead}", "--"],
                    TimeSpan.FromMinutes(2))
                .GetAwaiter()
                .GetResult(), changed, $"git diff {baseRef}...{resolvedHead}");
        }
        else if (!string.IsNullOrWhiteSpace(headRef))
        {
            throw new InvalidOperationException("--head requires --base.");
        }

        if (includeWorkingTree)
        {
            AddGitPaths(git.RunRawAsync(
                    repositoryRoot,
                    ["diff", "--name-only", "--diff-filter=ACMRD", "HEAD", "--"],
                    TimeSpan.FromMinutes(2))
                .GetAwaiter()
                .GetResult(), changed, "git diff HEAD");
            AddGitPaths(git.RunRawAsync(
                    repositoryRoot,
                    ["ls-files", "--others", "--exclude-standard"],
                    TimeSpan.FromMinutes(2))
                .GetAwaiter()
                .GetResult(), changed, "git ls-files");
        }

        return changed.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddGitPaths(
        ProcessRunResult result,
        ISet<string> changed,
        string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} failed: {result.StdErr.Trim()}");

        foreach (var line in result.StdOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            changed.Add(line.Trim().Replace('\\', '/'));
    }

    private static void WriteArchitectureArtifacts(
        RepositoryArchitectureExecutionResult result,
        string? reportJsonPath,
        string? summaryMarkdownPath)
    {
        if (!string.IsNullOrWhiteSpace(reportJsonPath))
        {
            var fullPath = Path.GetFullPath(reportJsonPath!);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(
                fullPath,
                JsonSerializer.Serialize(result, CliJson.Context.RepositoryArchitectureExecutionResult));
        }

        if (!string.IsNullOrWhiteSpace(summaryMarkdownPath))
        {
            var fullPath = Path.GetFullPath(summaryMarkdownPath!);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(fullPath, BuildArchitectureMarkdown(result));
        }
    }

    private static void WriteArchitectureText(RepositoryArchitectureExecutionResult result, ILogger logger)
    {
        var report = result.Architecture;
        var errors = report.Issues.Count(issue => issue.Severity == RepositoryArchitectureIssueSeverity.Error);
        var warnings = report.Issues.Length - errors;
        if (result.Succeeded)
            logger.Success($"Architecture policy passed ({report.Projects.Length} project(s), {report.Capabilities.Length} capability contract(s)).");
        else
            logger.Error($"Architecture policy failed ({errors} error(s), {warnings} warning(s)).");

        foreach (var capability in report.Capabilities)
        {
            var impact = capability.Impacted ? "impacted" : "unchanged";
            logger.Info($" -> {capability.Id}: {impact}; consumers={capability.DeclaredConsumerProjects.Length}; evidence={capability.RequiredEvidenceIds.Length}");
        }
        foreach (var issue in report.Issues)
        {
            var message = $"{issue.Code}: {issue.Message}" + (string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" [{issue.Path}]");
            if (issue.Severity == RepositoryArchitectureIssueSeverity.Error)
                logger.Error(message);
            else
                logger.Warn(message);
        }

        if (report.RequiredValidationStepIds.Length > 0)
            logger.Info($"Required evidence steps: {string.Join(", ", report.RequiredValidationStepIds)}");
        if (result.Validation is not null)
        {
            if (result.Validation.Succeeded)
                logger.Success($"Architecture evidence passed ({result.Validation.Steps.Length} step(s)).");
            else
                logger.Error(result.Validation.ErrorMessage ?? "Architecture evidence failed.");
        }
    }

    private static string BuildArchitectureMarkdown(RepositoryArchitectureExecutionResult result)
    {
        var report = result.Architecture;
        var builder = new StringBuilder();
        builder.AppendLine("## Repository architecture");
        builder.AppendLine();
        builder.AppendLine(result.Succeeded ? "Architecture policy and required evidence passed." : "Architecture policy or required evidence failed.");
        builder.AppendLine();
        builder.AppendLine($"- Projects: {report.Projects.Length}");
        builder.AppendLine($"- Capabilities: {report.Capabilities.Length}");
        builder.AppendLine($"- Changed files: {report.ChangedFiles.Length}");
        builder.AppendLine($"- Required evidence steps: {report.RequiredValidationStepIds.Length}");

        if (report.Capabilities.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("| Capability | Impact | Declared consumers | Observed consumers | Evidence steps |");
            builder.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var capability in report.Capabilities)
            {
                builder.AppendLine($"| `{capability.Id}` | {(capability.Impacted ? "yes" : "no")} | {capability.DeclaredConsumerProjects.Length} | {capability.ObservedConsumerProjects.Length} | {capability.RequiredValidationStepIds.Length} |");
            }
        }

        if (report.Issues.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Findings");
            builder.AppendLine();
            foreach (var issue in report.Issues)
                builder.AppendLine($"- **{issue.Severity} {issue.Code}**: {issue.Message}{(string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" (`{issue.Path}`)")}");
        }

        if (result.Validation is not null)
        {
            builder.AppendLine();
            builder.AppendLine("### Evidence execution");
            builder.AppendLine();
            foreach (var step in result.Validation.Steps)
                builder.AppendLine($"- {(step.Succeeded ? "Passed" : "Failed")}: `{step.Step.Id}`");
        }

        return builder.ToString();
    }

    private static string BuildArchitectureFailureSummary(RepositoryArchitectureExecutionResult result)
    {
        var architectureErrors = result.Architecture.Issues
            .Where(issue => issue.Severity == RepositoryArchitectureIssueSeverity.Error)
            .Select(issue => $"{issue.Code}: {issue.Message}")
            .Take(6)
            .ToArray();
        if (architectureErrors.Length > 0)
            return string.Join("\n", architectureErrors);
        return result.Validation?.ErrorMessage ?? "Repository architecture verification failed.";
    }

    private static int WriteArchitectureError(bool outputJson, ILogger logger, int exitCode, string message)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "architecture.verify",
                Success = false,
                ExitCode = exitCode,
                Error = message
            });
            return exitCode;
        }

        logger.Error(message);
        return exitCode;
    }
}
