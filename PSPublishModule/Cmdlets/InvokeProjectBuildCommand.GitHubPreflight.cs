using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PSPublishModule;

public sealed partial class InvokeProjectBuildCommand
{
    internal static string? BuildGitHubSingleReleaseReuseAdvisory(
        string tag,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyCollection<string> plannedAssetNames,
        IReadOnlyCollection<string> existingAssetNames)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var packable = projects
            .Where(p => p.IsPackable && !string.IsNullOrWhiteSpace(p.NewVersion))
            .ToArray();
        var distinctVersions = packable
            .Select(p => p.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctVersions.Length <= 1)
            return null;

        var planned = new HashSet<string>(
            plannedAssetNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(
            existingAssetNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        if (planned.Count == 0 || planned.SetEquals(existing))
            return null;

        var versionSummary = string.Join(", ",
            packable
                .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.ProjectName}={p.NewVersion}"));
        var missingAssets = planned
            .Except(existing, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extraAssets = existing
            .Except(planned, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var details = new List<string>
        {
            $"GitHub preflight blocked publish: tag '{tag}' already exists and Single release mode would reuse it for a mixed-version package set ({versionSummary})."
        };

        if (missingAssets.Length > 0)
            details.Add($"Planned assets missing from the existing release: {string.Join(", ", missingAssets)}.");
        if (extraAssets.Length > 0)
            details.Add($"Existing release contains different assets: {string.Join(", ", extraAssets)}.");

        details.Add("Change one of: set GitHubReleaseMode to 'PerProject'; set GitHubTagConflictPolicy to 'Fail' or 'AppendUtcTimestamp'; or use a unique GitHubTagTemplate such as '{Repo}-v{UtcTimestamp}'. Keep GitHubPrimaryProject explicit when Single mode is intentional.");
        return string.Join(" ", details);
    }

    private static string? ValidateGitHubPublishPreflight(
        ProjectBuildConfig config,
        DotNetRepositoryReleaseResult plan,
        string gitHubToken,
        ILogger logger)
    {
        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        if (!string.Equals(releaseMode, "Single", StringComparison.OrdinalIgnoreCase))
            return null;

        var conflictPolicy = ParseGitHubTagConflictPolicy(config.GitHubTagConflictPolicy);
        if (conflictPolicy != GitHubTagConflictPolicy.Reuse)
            return null;

        var plannedAssetNames = plan.Projects
            .Where(p => p.IsPackable && !string.IsNullOrWhiteSpace(p.ReleaseZipPath))
            .Select(p => System.IO.Path.GetFileName(p.ReleaseZipPath))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (plannedAssetNames.Length == 0)
            return null;

        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var dateTimeToken = nowLocal.ToString("yyyy.MM.dd-HH.mm.ss");
        var utcDateTimeToken = nowUtc.ToString("yyyy.MM.dd-HH.mm.ss");
        var timestampToken = nowLocal.ToString("yyyyMMddHHmmss");
        var utcTimestampToken = nowUtc.ToString("yyyyMMddHHmmss");
        var repoName = string.IsNullOrWhiteSpace(config.GitHubRepositoryName)
            ? "repository"
            : config.GitHubRepositoryName!.Trim();
        var baseVersion = ResolveGitHubBaseVersion(config, plan);
        var tagVersionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;

        var tag = !string.IsNullOrWhiteSpace(config.GitHubTagName)
            ? config.GitHubTagName!
            : (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate)
                ? ApplyTemplate(
                    config.GitHubTagTemplate!,
                    repoName,
                    tagVersionToken,
                    config.GitHubPrimaryProject ?? repoName,
                    tagVersionToken,
                    repoName,
                    dateToken,
                    utcDateToken,
                    dateTimeToken,
                    utcDateTimeToken,
                    timestampToken,
                    utcTimestampToken)
                : $"v{tagVersionToken}");
        tag = ApplyTagConflictPolicy(tag, conflictPolicy, utcTimestampToken);

        var existingRelease = ProbeGitHubReleaseByTag(
            config.GitHubUsername!,
            config.GitHubRepositoryName!,
            gitHubToken,
            tag);
        if (!string.IsNullOrWhiteSpace(existingRelease.ErrorMessage))
            return existingRelease.ErrorMessage;
        if (!existingRelease.Exists)
            return existingRelease.ErrorMessage;

        logger.Info($"GitHub preflight: release tag '{tag}' already exists; validating asset set before publish.");
        return BuildGitHubSingleReleaseReuseAdvisory(tag, plan.Projects, plannedAssetNames, existingRelease.AssetNames);
    }

    private static GitHubReleaseProbeResult ProbeGitHubReleaseByTag(
        string owner,
        string repo,
        string token,
        string tag)
    {
        try
        {
            using var client = CreateGitHubHttpClient(token);
            var uri = new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}");
            var response = client.GetAsync(uri).ConfigureAwait(false).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new GitHubReleaseProbeResult { Exists = false };

            if (!response.IsSuccessStatusCode)
            {
                return new GitHubReleaseProbeResult
                {
                    Exists = true,
                    ErrorMessage = $"GitHub preflight failed while checking tag '{tag}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}"
                };
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<GitHubReleaseProbeResponse>(responseText, options);
            return new GitHubReleaseProbeResult
            {
                Exists = true,
                ReleaseUrl = parsed?.HtmlUrl,
                AssetNames = parsed?.Assets?
                    .Select(a => a.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>()
            };
        }
        catch (Exception ex)
        {
            return new GitHubReleaseProbeResult
            {
                Exists = true,
                ErrorMessage = $"GitHub preflight failed while checking tag '{tag}': {ex.Message}"
            };
        }
    }

    private static HttpClient CreateGitHubHttpClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PSPublishModule", "2.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string TrimForMessage(string? text)
    {
        if (text is null)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        return trimmed.Length > 4000 ? trimmed.Substring(0, 4000) + "..." : trimmed;
    }

    private sealed class GitHubReleaseProbeResult
    {
        public bool Exists { get; set; }
        public string? ReleaseUrl { get; set; }
        public string[] AssetNames { get; set; } = Array.Empty<string>();
        public string? ErrorMessage { get; set; }
    }

    private sealed class GitHubReleaseProbeResponse
    {
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public GitHubReleaseProbeAsset[]? Assets { get; set; }
    }

    private sealed class GitHubReleaseProbeAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
