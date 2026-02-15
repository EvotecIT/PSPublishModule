using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteSourcesSync(JsonElement step, string baseDir, WebConsoleLogger? logger, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("sources-sync requires config.");

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        var sources = spec.Sources ?? Array.Empty<SourceRepoSpec>();
        if (sources.Length == 0)
            throw new InvalidOperationException("sources-sync: site.json has no Sources entries.");

        var repos = new List<Dictionary<string, object?>>();
        foreach (var source in sources)
        {
            if (source is null) continue;
            if (string.IsNullOrWhiteSpace(source.Repo)) continue;

            var slug = !string.IsNullOrWhiteSpace(source.Slug) ? source.Slug!.Trim() : InferRepoSlug(source.Repo);
            if (string.IsNullOrWhiteSpace(slug))
                slug = "repo";

            var destinationValue = string.IsNullOrWhiteSpace(source.Destination)
                ? null
                : source.Destination!.Trim();
            if (string.IsNullOrWhiteSpace(destinationValue))
            {
                var projectsRoot = string.IsNullOrWhiteSpace(spec.ProjectsRoot) ? "projects" : spec.ProjectsRoot!;
                destinationValue = Path.Combine(projectsRoot, slug);
            }

            repos.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = source.Repo,
                ["destination"] = destinationValue,
                ["ref"] = source.Ref,
                ["repoBaseUrl"] = source.RepoBaseUrl,
                ["authType"] = source.AuthType,
                ["tokenEnv"] = source.TokenEnv,
                ["token"] = source.Token,
                ["username"] = source.Username,
                ["clean"] = source.Clean,
                ["fetchTags"] = source.FetchTags,
                ["depth"] = source.Depth,
                ["timeoutSeconds"] = source.TimeoutSeconds,
                ["retry"] = source.Retry,
                ["retryDelayMs"] = source.RetryDelayMs,
                ["sparseCheckout"] = source.SparseCheckout is { Length: > 0 } ? source.SparseCheckout : null,
                ["sparsePaths"] = source.SparsePaths,
                ["submodules"] = source.Submodules,
                ["submodulesRecursive"] = source.SubmodulesRecursive,
                ["submoduleDepth"] = source.SubmoduleDepth
            });
        }

        if (repos.Count == 0)
            throw new InvalidOperationException("sources-sync: Sources entries are empty.");

        var gitSyncStep = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["repos"] = repos,
            ["lockMode"] = GetString(step, "lockMode") ?? GetString(step, "lock-mode"),
            ["lockPath"] = GetString(step, "lockPath") ?? GetString(step, "lock-path") ?? GetString(step, "lock"),
            ["writeManifest"] = GetBool(step, "writeManifest") ?? GetBool(step, "write-manifest"),
            ["manifestPath"] = GetString(step, "manifestPath") ?? GetString(step, "manifest-path") ?? GetString(step, "manifest")
        };

        var json = JsonSerializer.Serialize(gitSyncStep);
        using var doc = JsonDocument.Parse(json);

        ExecuteGitSync(doc.RootElement, plan.RootPath, logger, stepResult);
        stepResult.Task = "sources-sync";
    }

    private static string InferRepoSlug(string repo)
    {
        if (string.IsNullOrWhiteSpace(repo))
            return string.Empty;

        var trimmed = repo.Trim().TrimEnd('/');

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

