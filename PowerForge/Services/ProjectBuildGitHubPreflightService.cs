using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ProjectBuildGitHubPreflightService
{
    private readonly ILogger _logger;
    private readonly Func<string, string, string, string, GitHubReleaseProbeResult> _probeRelease;
    private readonly Func<DateTime> _localNow;
    private readonly Func<DateTime> _utcNow;

    public ProjectBuildGitHubPreflightService(
        ILogger logger,
        Func<string, string, string, string, GitHubReleaseProbeResult>? probeRelease = null,
        Func<DateTime>? localNow = null,
        Func<DateTime>? utcNow = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _probeRelease = probeRelease ?? ProbeGitHubReleaseByTag;
        _localNow = localNow ?? (() => DateTime.Now);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string? Validate(ProjectBuildConfiguration config, DotNetRepositoryReleaseResult plan, string gitHubToken)
    {
        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        if (!string.Equals(releaseMode, "Single", StringComparison.OrdinalIgnoreCase))
            return null;

        var conflictPolicy = ProjectBuildSupportService.ParseGitHubTagConflictPolicy(config.GitHubTagConflictPolicy);
        if (!string.Equals(conflictPolicy, "Reuse", StringComparison.OrdinalIgnoreCase))
            return null;

        var plannedAssetNames = plan.Projects
            .Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.ReleaseZipPath))
            .Select(project => Path.GetFileName(project.ReleaseZipPath))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (plannedAssetNames.Length == 0)
            return null;

        var nowLocal = _localNow();
        var nowUtc = _utcNow();
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var dateTimeToken = nowLocal.ToString("yyyy.MM.dd-HH.mm.ss");
        var utcDateTimeToken = nowUtc.ToString("yyyy.MM.dd-HH.mm.ss");
        var timestampToken = nowLocal.ToString("yyyyMMddHHmmss");
        var utcTimestampToken = nowUtc.ToString("yyyyMMddHHmmss");
        var repoName = string.IsNullOrWhiteSpace(config.GitHubRepositoryName)
            ? "repository"
            : config.GitHubRepositoryName!.Trim();
        var baseVersion = ProjectBuildSupportService.ResolveGitHubBaseVersion(config, plan);
        var tagVersionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;

        var tag = !string.IsNullOrWhiteSpace(config.GitHubTagName)
            ? config.GitHubTagName!
            : (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate)
                ? ProjectBuildSupportService.ApplyTemplate(
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
        tag = ProjectBuildSupportService.ApplyTagConflictPolicy(tag, conflictPolicy, utcTimestampToken);

        var existingRelease = _probeRelease(
            config.GitHubUsername!,
            config.GitHubRepositoryName!,
            gitHubToken,
            tag);
        if (!string.IsNullOrWhiteSpace(existingRelease.ErrorMessage))
            return existingRelease.ErrorMessage;
        if (!existingRelease.Exists)
            return existingRelease.ErrorMessage;

        _logger.Info($"GitHub preflight: release tag '{tag}' already exists; validating asset set before publish.");
        return ProjectBuildGitHubPublisher.BuildSingleReleaseReuseAdvisory(tag, plan.Projects, plannedAssetNames, existingRelease.AssetNames);
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
                    .Select(asset => asset.Name)
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
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text!.Trim();
        return trimmed.Length > 4000 ? trimmed.Substring(0, 4000) + "..." : trimmed;
    }

    internal sealed class GitHubReleaseProbeResult
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
