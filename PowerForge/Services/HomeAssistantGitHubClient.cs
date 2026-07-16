using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PowerForge;

internal sealed class HomeAssistantGitHubClient : IHomeAssistantGitHubClient {
    private static readonly HttpClient SharedClient = new();
    private readonly string _owner;
    private readonly string _repository;
    private readonly string _token;
    private readonly string _apiBaseUrl;
    private readonly HttpClient _client;

    internal HomeAssistantGitHubClient(
        string owner,
        string repository,
        string token,
        string apiBaseUrl,
        HttpClient? client = null) {
        _owner = string.IsNullOrWhiteSpace(owner) ? throw new ArgumentException("Owner is required.", nameof(owner)) : owner.Trim();
        _repository = string.IsNullOrWhiteSpace(repository) ? throw new ArgumentException("Repository is required.", nameof(repository)) : repository.Trim();
        _token = string.IsNullOrWhiteSpace(token) ? throw new ArgumentException("Token is required.", nameof(token)) : token.Trim();
        _apiBaseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.github.com" : apiBaseUrl.TrimEnd('/');
        _client = client ?? SharedClient;
    }

    public HomeAssistantPullRequest GetPullRequest(int number) {
        if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number), "Pull request number must be positive.");
        var value = GetObject($"/repos/{_owner}/{_repository}/pulls/{number}");
        var result = new HomeAssistantPullRequest {
            Number = number,
            Merged = value["merged"]?.GetValue<bool>() == true,
            Title = GetString(value, "title"),
            HtmlUrl = GetString(value, "html_url"),
            HeadSha = GetString(value["head"] as JsonObject, "sha"),
            MergeCommitSha = GetString(value, "merge_commit_sha")
        };

        if (value["labels"] is JsonArray labels) {
            foreach (var label in labels.OfType<JsonObject>()) {
                var name = GetString(label, "name");
                if (!string.IsNullOrWhiteSpace(name)) result.Labels.Add(name);
            }
        }

        for (var page = 1; page <= 20; page++) {
            var files = GetArray($"/repos/{_owner}/{_repository}/pulls/{number}/files?per_page=100&page={page}");
            foreach (var file in files.OfType<JsonObject>()) {
                var path = GetString(file, "filename");
                if (!string.IsNullOrWhiteSpace(path)) result.ChangedFiles.Add(path);
            }

            if (files.Count < 100) break;
            if (page == 20) throw new InvalidOperationException("Pull request changed-file pagination exceeded 2,000 files.");
        }

        return result;
    }

    public HomeAssistantCheckSummary GetCheckSummary(string commitSha) {
        if (string.IsNullOrWhiteSpace(commitSha)) throw new ArgumentException("Commit SHA is required.", nameof(commitSha));
        var result = new HomeAssistantCheckSummary();
        for (var page = 1; page <= 10; page++) {
            var value = GetObject($"/repos/{_owner}/{_repository}/commits/{commitSha}/check-runs?per_page=100&filter=latest&page={page}");
            if (value["check_runs"] is not JsonArray runs) return result;

            foreach (var run in runs.OfType<JsonObject>()) {
                result.Total++;
                var name = GetString(run, "name");
                var status = GetString(run, "status");
                var conclusion = GetString(run, "conclusion");
                if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) || !IsAcceptedConclusion(conclusion))
                    result.BlockingChecks.Add($"{name}: {(string.IsNullOrWhiteSpace(conclusion) ? status : conclusion)}");
            }

            if (runs.Count < 100) return result;
            if (page == 10) throw new InvalidOperationException("Check-run pagination exceeded 1,000 checks.");
        }

        return result;
    }

    public HomeAssistantGitHubRelease? GetLatestRelease()
        => TryGetRelease($"/repos/{_owner}/{_repository}/releases/latest");

    public HomeAssistantGitHubRelease? FindReleaseByMarker(string marker) {
        if (string.IsNullOrWhiteSpace(marker)) throw new ArgumentException("Release marker is required.", nameof(marker));
        for (var page = 1; page <= 10; page++) {
            var releases = GetArray($"/repos/{_owner}/{_repository}/releases?per_page=100&page={page}");
            foreach (var release in releases.OfType<JsonObject>()) {
                var parsed = ParseRelease(release);
                if (parsed.Body.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return parsed;
            }

            if (releases.Count < 100) return null;
        }

        throw new InvalidOperationException("Release marker search exceeded 1,000 GitHub releases.");
    }

    public HomeAssistantGitHubRelease? GetReleaseByTag(string tagName) {
        if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("Tag name is required.", nameof(tagName));
        return TryGetRelease($"/repos/{_owner}/{_repository}/releases/tags/{Uri.EscapeDataString(tagName)}");
    }

    public string? GetTagCommitSha(string tagName) {
        var value = TryGetObject($"/repos/{_owner}/{_repository}/git/ref/tags/{Uri.EscapeDataString(tagName)}");
        if (value is null) return null;
        var target = value["object"] as JsonObject;
        var sha = GetString(target, "sha");
        var type = GetString(target, "type");
        if (string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sha)) {
            var annotated = GetObject($"/repos/{_owner}/{_repository}/git/tags/{sha}");
            sha = GetString(annotated["object"] as JsonObject, "sha");
        }

        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    private HomeAssistantGitHubRelease? TryGetRelease(string path) {
        var value = TryGetObject(path);
        return value is null ? null : ParseRelease(value);
    }

    private static HomeAssistantGitHubRelease ParseRelease(JsonObject value) {
        var result = new HomeAssistantGitHubRelease {
            Id = value["id"]?.GetValue<long>() ?? 0,
            TagName = GetString(value, "tag_name"),
            Body = GetString(value, "body"),
            HtmlUrl = GetString(value, "html_url"),
            TargetCommitish = GetString(value, "target_commitish")
        };
        if (value["assets"] is JsonArray assets) {
            foreach (var asset in assets.OfType<JsonObject>()) {
                var name = GetString(asset, "name");
                if (!string.IsNullOrWhiteSpace(name)) result.AssetNames.Add(name);
            }
        }

        return result;
    }

    private JsonObject GetObject(string path)
        => TryGetObject(path) ?? throw new InvalidOperationException($"GitHub API resource was not found: {path}");

    private JsonObject? TryGetObject(string path) {
        var node = Send(HttpMethod.Get, path, allowNotFound: true);
        if (node is null) return null;
        return node as JsonObject ?? throw new InvalidOperationException($"GitHub API returned a non-object response for '{path}'.");
    }

    private JsonArray GetArray(string path) {
        var node = Send(HttpMethod.Get, path, allowNotFound: false);
        return node as JsonArray ?? throw new InvalidOperationException($"GitHub API returned a non-array response for '{path}'.");
    }

    private JsonNode? Send(HttpMethod method, string path, bool allowNotFound) {
        using var request = new HttpRequestMessage(method, new Uri(_apiBaseUrl + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("PowerForge-HomeAssistant-Release");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = _client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode} {response.ReasonPhrase}) for '{path}'. {Trim(body)}");
        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    private static string GetString(JsonObject? value, string propertyName)
        => value?[propertyName]?.GetValue<string>()?.Trim() ?? string.Empty;

    private static bool IsAcceptedConclusion(string conclusion)
        => conclusion.Equals("success", StringComparison.OrdinalIgnoreCase) ||
           conclusion.Equals("neutral", StringComparison.OrdinalIgnoreCase) ||
           conclusion.Equals("skipped", StringComparison.OrdinalIgnoreCase);

    private static string Trim(string value) {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 500 ? normalized : normalized.Substring(0, 500) + "...";
    }
}