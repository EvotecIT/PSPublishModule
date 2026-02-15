using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleSourcesSync(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.sources-sync");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        var sources = spec.Sources ?? Array.Empty<SourceRepoSpec>();
        if (sources.Length == 0)
            return Fail("site.json has no Sources entries.", outputJson, logger, "web.sources-sync");

        WebPipelineStepResult stepResult;
        try
        {
            var defaults = ParseSourceDefaultsFromArgs(subArgs);
            var gitSyncStep = BuildGitSyncStepFromSources(spec, plan, sources, defaults);
            stepResult = WebPipelineRunner.RunGitSyncStepForCli(gitSyncStep, plan.RootPath, logger);
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.sources-sync",
                    Success = false,
                    ExitCode = 1,
                    Config = "web",
                    ConfigPath = specPath,
                    Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                    Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                    Error = ex.Message
                });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }

        if (outputJson)
        {
            var pipeline = new WebPipelineResult
            {
                StepCount = 1,
                Success = stepResult.Success,
                Steps = new() { stepResult }
            };
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.sources-sync",
                Success = stepResult.Success,
                ExitCode = stepResult.Success ? 0 : 1,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                Result = WebCliJson.SerializeToElement(pipeline, WebCliJson.Context.WebPipelineResult)
            });
            return stepResult.Success ? 0 : 1;
        }

        if (stepResult.Success)
        {
            logger.Success(stepResult.Message ?? "sources-sync ok");
            return 0;
        }

        logger.Error(stepResult.Message ?? "sources-sync failed");
        return 1;
    }

    private sealed class SourceSyncDefaults
    {
        public string? RepoBaseUrl { get; init; }
        public string? AuthType { get; init; }
        public string? TokenEnv { get; init; }
        public string? Username { get; init; }
        public string? Ref { get; init; }
        public bool? Clean { get; init; }
        public bool? FetchTags { get; init; }
        public int? Depth { get; init; }
        public int? TimeoutSeconds { get; init; }
        public int? Retry { get; init; }
        public int? RetryDelayMs { get; init; }
        public bool? Submodules { get; init; }
        public bool? SubmodulesRecursive { get; init; }
        public int? SubmoduleDepth { get; init; }
        public string? LockMode { get; init; }
        public string? LockPath { get; init; }
        public bool? WriteManifest { get; init; }
        public string? ManifestPath { get; init; }
    }

    private static SourceSyncDefaults ParseSourceDefaultsFromArgs(string[] argv)
    {
        return new SourceSyncDefaults
        {
            RepoBaseUrl = TryGetOptionValue(argv, "--repo-base-url") ?? TryGetOptionValue(argv, "--repoBaseUrl") ?? TryGetOptionValue(argv, "--repo-host"),
            AuthType = TryGetOptionValue(argv, "--auth-type") ?? TryGetOptionValue(argv, "--authType") ?? TryGetOptionValue(argv, "--auth"),
            TokenEnv = TryGetOptionValue(argv, "--token-env") ?? TryGetOptionValue(argv, "--tokenEnv"),
            Username = TryGetOptionValue(argv, "--username"),
            Ref = TryGetOptionValue(argv, "--ref") ?? TryGetOptionValue(argv, "--branch") ?? TryGetOptionValue(argv, "--tag") ?? TryGetOptionValue(argv, "--commit"),
            Clean = HasOption(argv, "--clean") ? true : null,
            FetchTags = HasOption(argv, "--fetch-tags") || HasOption(argv, "--fetchTags") ? true : null,
            Depth = TryGetOptionValue(argv, "--depth") is { } depthText ? ParseIntOption(depthText, 0) : null,
            TimeoutSeconds = TryGetOptionValue(argv, "--timeout-seconds") is { } timeoutText ? ParseIntOption(timeoutText, 600) : null,
            Retry = TryGetOptionValue(argv, "--retry") is { } retryText ? ParseIntOption(retryText, 0) : null,
            RetryDelayMs = TryGetOptionValue(argv, "--retry-delay-ms") is { } delayText ? ParseIntOption(delayText, 500) : null,
            Submodules = HasOption(argv, "--submodules") || HasOption(argv, "--submodule") ? true : null,
            SubmodulesRecursive = HasOption(argv, "--submodules-recursive") || HasOption(argv, "--submodulesRecursive") ? true : null,
            SubmoduleDepth = TryGetOptionValue(argv, "--submodule-depth") is { } smDepthText ? ParseIntOption(smDepthText, 0) : null,
            LockMode = TryGetOptionValue(argv, "--lock-mode") ?? TryGetOptionValue(argv, "--lockMode"),
            LockPath = TryGetOptionValue(argv, "--lock-path") ?? TryGetOptionValue(argv, "--lockPath") ?? TryGetOptionValue(argv, "--lock"),
            WriteManifest = HasOption(argv, "--write-manifest") || HasOption(argv, "--writeManifest") ? true : null,
            ManifestPath = TryGetOptionValue(argv, "--manifest-path") ?? TryGetOptionValue(argv, "--manifestPath") ?? TryGetOptionValue(argv, "--manifest")
        };
    }

    private static JsonElement BuildGitSyncStepFromSources(SiteSpec spec, WebSitePlan plan, SourceRepoSpec[] sources, SourceSyncDefaults defaults)
    {
        var repos = new List<Dictionary<string, object?>>();
        var usedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source is null) continue;
            if (string.IsNullOrWhiteSpace(source.Repo)) continue;

            var slug = !string.IsNullOrWhiteSpace(source.Slug) ? source.Slug!.Trim() : InferRepoSlug(source.Repo);
            if (string.IsNullOrWhiteSpace(slug))
                slug = "repo";

            var destinationValue = FirstNonEmpty(source.Destination, null);
            if (string.IsNullOrWhiteSpace(destinationValue))
            {
                var projectsRoot = string.IsNullOrWhiteSpace(spec.ProjectsRoot) ? "projects" : spec.ProjectsRoot!;
                destinationValue = Path.Combine(projectsRoot, slug);
            }

            var destinationFull = Path.GetFullPath(Path.IsPathRooted(destinationValue)
                ? destinationValue
                : Path.Combine(plan.RootPath, destinationValue));
            if (!usedDestinations.Add(destinationFull))
                throw new InvalidOperationException($"sources-sync has duplicate destination: {destinationFull}");

            repos.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = source.Repo,
                ["destination"] = destinationValue,
                ["ref"] = FirstNonEmpty(source.Ref, defaults.Ref),
                ["repoBaseUrl"] = FirstNonEmpty(source.RepoBaseUrl, defaults.RepoBaseUrl),
                ["authType"] = FirstNonEmpty(source.AuthType, defaults.AuthType),
                ["tokenEnv"] = FirstNonEmpty(source.TokenEnv, defaults.TokenEnv),
                ["token"] = source.Token,
                ["username"] = FirstNonEmpty(source.Username, defaults.Username),
                ["clean"] = source.Clean ?? defaults.Clean,
                ["fetchTags"] = source.FetchTags ?? defaults.FetchTags,
                ["depth"] = source.Depth ?? defaults.Depth,
                ["timeoutSeconds"] = source.TimeoutSeconds ?? defaults.TimeoutSeconds,
                ["retry"] = source.Retry ?? defaults.Retry,
                ["retryDelayMs"] = source.RetryDelayMs ?? defaults.RetryDelayMs,
                ["sparseCheckout"] = source.SparseCheckout is { Length: > 0 } ? source.SparseCheckout : null,
                ["sparsePaths"] = FirstNonEmpty(source.SparsePaths, null),
                ["submodules"] = source.Submodules ?? defaults.Submodules,
                ["submodulesRecursive"] = source.SubmodulesRecursive ?? defaults.SubmodulesRecursive,
                ["submoduleDepth"] = source.SubmoduleDepth ?? defaults.SubmoduleDepth
            });
        }

        if (repos.Count == 0)
            throw new InvalidOperationException("site.json Sources entries are empty.");

        var step = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["repos"] = repos,
            ["lockMode"] = defaults.LockMode,
            ["lockPath"] = defaults.LockPath,
            ["writeManifest"] = defaults.WriteManifest,
            ["manifestPath"] = defaults.ManifestPath
        };

        var json = JsonSerializer.Serialize(step);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string? FirstNonEmpty(string? a, string? b)
    {
        if (!string.IsNullOrWhiteSpace(a)) return a;
        if (!string.IsNullOrWhiteSpace(b)) return b;
        return null;
    }

    private static string InferRepoSlug(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            return string.Empty;

        var trimmed = repo.Trim().TrimEnd('/');

        // SCP-like: git@host:group/repo.git
        var colonIndex = trimmed.LastIndexOf(':');
        if (colonIndex > 0 && trimmed.Contains("@", StringComparison.Ordinal) && !trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = trimmed.Substring(colonIndex + 1);

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var seg = uri.Segments.LastOrDefault()?.TrimEnd('/');
            return TrimGitSuffix(seg ?? string.Empty);
        }

        if (trimmed.Contains("/", StringComparison.Ordinal))
        {
            var seg = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            return TrimGitSuffix(seg);
        }

        if (trimmed.Contains("\\", StringComparison.Ordinal))
        {
            var seg = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
            return TrimGitSuffix(seg);
        }

        return TrimGitSuffix(trimmed);
    }

    private static string TrimGitSuffix(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var v = value.Trim();
        if (v.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            v = v.Substring(0, v.Length - 4);
        return v.Trim();
    }
}
