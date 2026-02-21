using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// GitHub Actions artifact cleanup service used to reclaim storage quota.
/// </summary>
public sealed class GitHubArtifactCleanupService
{
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private static readonly HttpClient SharedClient = CreateSharedClient();

    private static readonly string[] DefaultIncludePatterns =
    {
        "github-pages",
        "test-results*",
        "coverage*",
        "*-analysis*",
        "*-report*",
        "site-audit*",
        "seo-doctor*"
    };

    /// <summary>
    /// Creates a cleanup service with a logger and optional custom HTTP client.
    /// </summary>
    /// <param name="logger">Logger used for progress and diagnostics.</param>
    /// <param name="client">Optional HTTP client (for tests/custom transports).</param>
    public GitHubArtifactCleanupService(ILogger logger, HttpClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? SharedClient;
    }

    /// <summary>
    /// Executes a cleanup run against GitHub Actions artifacts.
    /// </summary>
    /// <param name="spec">Cleanup specification.</param>
    /// <returns>Run summary with planned/deleted items and counters.</returns>
    public GitHubArtifactCleanupResult Prune(GitHubArtifactCleanupSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var normalized = NormalizeSpec(spec);
        var allArtifacts = ListArtifacts(normalized.ApiBaseUri, normalized.Repository, normalized.Token, normalized.PageSize);
        var now = DateTimeOffset.UtcNow;
        var ageCutoff = normalized.MaxAgeDays is > 0
            ? now.AddDays(-normalized.MaxAgeDays.Value)
            : (DateTimeOffset?)null;

        var matched = allArtifacts
            .Where(a => !a.Expired)
            .Where(a => MatchesAnyPattern(a.Name, normalized.IncludeNames, defaultWhenEmpty: true))
            .Where(a => !MatchesAnyPattern(a.Name, normalized.ExcludeNames, defaultWhenEmpty: false))
            .ToArray();

        var planned = new List<GitHubArtifactCleanupItem>();
        var keptByRecentWindow = 0;
        var keptByAgeThreshold = 0;

        foreach (var byName in matched.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = byName
                .OrderByDescending(GetSortTimestamp)
                .ThenByDescending(a => a.Id)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var artifact = ordered[index];
                if (index < normalized.KeepLatestPerName)
                {
                    keptByRecentWindow++;
                    continue;
                }

                if (ageCutoff is not null && GetSortTimestamp(artifact) > ageCutoff.Value)
                {
                    keptByAgeThreshold++;
                    continue;
                }

                planned.Add(ToItem(artifact, reason: BuildSelectionReason(normalized, ageCutoff)));
            }
        }

        var orderedPlanned = planned
            .OrderBy(a => a.UpdatedAt ?? a.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Id)
            .Take(normalized.MaxDelete)
            .ToArray();

        var result = new GitHubArtifactCleanupResult
        {
            Repository = normalized.Repository,
            IncludeNames = normalized.IncludeNames,
            ExcludeNames = normalized.ExcludeNames,
            KeepLatestPerName = normalized.KeepLatestPerName,
            MaxAgeDays = normalized.MaxAgeDays is > 0 ? normalized.MaxAgeDays : null,
            MaxDelete = normalized.MaxDelete,
            DryRun = normalized.DryRun,
            ScannedArtifacts = allArtifacts.Length,
            MatchedArtifacts = matched.Length,
            KeptByRecentWindow = keptByRecentWindow,
            KeptByAgeThreshold = keptByAgeThreshold,
            Planned = orderedPlanned,
            PlannedDeletes = orderedPlanned.Length,
            PlannedDeleteBytes = orderedPlanned.Sum(p => p.SizeInBytes),
            Success = true
        };

        _logger.Info($"GitHub artifacts scanned: {result.ScannedArtifacts}, matched: {result.MatchedArtifacts}, planned: {result.PlannedDeletes}.");

        if (normalized.DryRun || orderedPlanned.Length == 0)
        {
            if (normalized.DryRun)
                _logger.Info("GitHub artifact cleanup dry-run enabled. No delete requests were sent.");
            return result;
        }

        var deleted = new List<GitHubArtifactCleanupItem>();
        var failed = new List<GitHubArtifactCleanupItem>();

        foreach (var item in orderedPlanned)
        {
            var deleteResult = DeleteArtifact(normalized.ApiBaseUri, normalized.Repository, normalized.Token, item.Id);
            if (deleteResult.Ok)
            {
                deleted.Add(item);
                _logger.Verbose($"Deleted artifact #{item.Id} ({item.Name}).");
                continue;
            }

            item.DeleteStatusCode = deleteResult.StatusCode;
            item.DeleteError = deleteResult.Error;
            failed.Add(item);
            _logger.Warn($"Failed to delete artifact #{item.Id} ({item.Name}): {deleteResult.Error}");
        }

        result.Deleted = deleted.ToArray();
        result.Failed = failed.ToArray();
        result.DeletedArtifacts = deleted.Count;
        result.DeletedBytes = deleted.Sum(p => p.SizeInBytes);
        result.FailedDeletes = failed.Count;
        result.Success = failed.Count == 0 || !normalized.FailOnDeleteError;

        if (!result.Success)
            result.Message = "One or more artifact delete operations failed.";
        else if (failed.Count > 0)
            result.Message = "Cleanup finished with non-fatal delete errors.";

        return result;
    }

    private sealed class GitHubArtifactRecord
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool Expired { get; set; }
        public long? WorkflowRunId { get; set; }
    }

    private sealed class NormalizedSpec
    {
        public Uri ApiBaseUri { get; set; } = new Uri("https://api.github.com/", UriKind.Absolute);
        public string Repository { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string[] IncludeNames { get; set; } = Array.Empty<string>();
        public string[] ExcludeNames { get; set; } = Array.Empty<string>();
        public int KeepLatestPerName { get; set; }
        public int? MaxAgeDays { get; set; }
        public int MaxDelete { get; set; }
        public int PageSize { get; set; }
        public bool DryRun { get; set; }
        public bool FailOnDeleteError { get; set; }
    }

    private NormalizedSpec NormalizeSpec(GitHubArtifactCleanupSpec spec)
    {
        var repository = NormalizeRepository(spec.Repository);
        if (string.IsNullOrWhiteSpace(repository))
            throw new InvalidOperationException("Repository is required (owner/repo).");

        var token = (spec.Token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub token is required.");

        var include = NormalizePatterns(spec.IncludeNames);
        if (include.Length == 0)
            include = DefaultIncludePatterns.ToArray();

        var exclude = NormalizePatterns(spec.ExcludeNames);

        return new NormalizedSpec
        {
            ApiBaseUri = NormalizeApiBaseUri(spec.ApiBaseUrl),
            Repository = repository,
            Token = token,
            IncludeNames = include,
            ExcludeNames = exclude,
            KeepLatestPerName = Math.Max(0, spec.KeepLatestPerName),
            MaxAgeDays = spec.MaxAgeDays is < 1 ? null : spec.MaxAgeDays,
            MaxDelete = Math.Max(1, spec.MaxDelete),
            PageSize = Clamp(spec.PageSize, 1, 100),
            DryRun = spec.DryRun,
            FailOnDeleteError = spec.FailOnDeleteError
        };
    }

    private GitHubArtifactRecord[] ListArtifacts(Uri apiBaseUri, string repository, string token, int pageSize)
    {
        var records = new List<GitHubArtifactRecord>();
        var page = 1;
        int? totalCount = null;

        while (true)
        {
            var uri = BuildApiUri(apiBaseUri, repository, $"actions/artifacts?per_page={pageSize}&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = _client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw BuildHttpFailure("listing artifacts", response, body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_count", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number)
                totalCount = totalElement.GetInt32();

            if (!root.TryGetProperty("artifacts", out var artifactsElement) || artifactsElement.ValueKind != JsonValueKind.Array)
                break;

            var pageCount = 0;
            foreach (var item in artifactsElement.EnumerateArray())
            {
                records.Add(ParseArtifact(item));
                pageCount++;
            }

            if (pageCount == 0)
                break;

            if (totalCount is not null && records.Count >= totalCount.Value)
                break;

            if (pageCount < pageSize)
                break;

            page++;
        }

        return records.ToArray();
    }

    private (bool Ok, int? StatusCode, string? Error) DeleteArtifact(Uri apiBaseUri, string repository, string token, long artifactId)
    {
        var uri = BuildApiUri(apiBaseUri, repository, $"actions/artifacts/{artifactId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = _client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound || (int)response.StatusCode == 410)
            return (true, (int)response.StatusCode, null);

        var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        var error = BuildHttpFailure("deleting artifact", response, body).Message;
        return (false, (int)response.StatusCode, error);
    }

    private static DateTimeOffset GetSortTimestamp(GitHubArtifactRecord artifact)
        => artifact.UpdatedAt ?? artifact.CreatedAt ?? DateTimeOffset.MinValue;

    private static GitHubArtifactCleanupItem ToItem(GitHubArtifactRecord record, string? reason)
    {
        return new GitHubArtifactCleanupItem
        {
            Id = record.Id,
            Name = record.Name,
            SizeInBytes = record.SizeInBytes,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            Expired = record.Expired,
            WorkflowRunId = record.WorkflowRunId,
            Reason = reason
        };
    }

    private static string BuildSelectionReason(NormalizedSpec spec, DateTimeOffset? ageCutoff)
    {
        if (ageCutoff is null)
            return $"older-than-keep-window:{spec.KeepLatestPerName}";

        return $"older-than-keep-window:{spec.KeepLatestPerName};older-than-days:{spec.MaxAgeDays}";
    }

    private static string[] NormalizePatterns(string[]? patterns)
    {
        return (patterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesAnyPattern(string value, string[] patterns, bool defaultWhenEmpty)
    {
        if (patterns is null || patterns.Length == 0)
            return defaultWhenEmpty;

        foreach (var pattern in patterns)
        {
            if (MatchSinglePattern(value, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchSinglePattern(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
        {
            var expression = trimmed.Substring(3);
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!trimmed.Contains('*') && !trimmed.Contains('?'))
            return string.Equals(value, trimmed, StringComparison.OrdinalIgnoreCase);

        var regexPattern = "^" + Regex.Escape(trimmed)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static GitHubArtifactRecord ParseArtifact(JsonElement artifact)
    {
        var workflowRunId = TryGetProperty(artifact, "workflow_run", out var workflowElement) &&
                            workflowElement.ValueKind == JsonValueKind.Object &&
                            TryGetProperty(workflowElement, "id", out var workflowIdElement) &&
                            workflowIdElement.ValueKind == JsonValueKind.Number
            ? workflowIdElement.GetInt64()
            : (long?)null;

        return new GitHubArtifactRecord
        {
            Id = TryGetInt64(artifact, "id"),
            Name = TryGetString(artifact, "name") ?? string.Empty,
            SizeInBytes = TryGetInt64(artifact, "size_in_bytes"),
            Expired = TryGetBool(artifact, "expired"),
            CreatedAt = TryGetDateTimeOffset(artifact, "created_at"),
            UpdatedAt = TryGetDateTimeOffset(artifact, "updated_at"),
            WorkflowRunId = workflowRunId
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static long TryGetInt64(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return 0;

        if (value.ValueKind != JsonValueKind.Number)
            return 0;

        return value.TryGetInt64(out var parsed) ? parsed : 0;
    }

    private static bool TryGetBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var text = TryGetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
    }

    private static Exception BuildHttpFailure(string operation, HttpResponseMessage response, string? body)
    {
        var status = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "HTTP error";
        var trimmedBody = TrimForMessage(body);
        var rateLimitHint = BuildRateLimitHint(response);
        var details = string.IsNullOrWhiteSpace(trimmedBody) ? reason : $"{reason}. {trimmedBody}";
        if (!string.IsNullOrWhiteSpace(rateLimitHint))
            details = $"{details} {rateLimitHint}";

        return new InvalidOperationException($"GitHub API error while {operation} (HTTP {status}). {details}".Trim());
    }

    private static string? BuildRateLimitHint(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return null;

        if (!response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
            return null;

        var remaining = (remainingValues ?? Array.Empty<string>()).FirstOrDefault();
        if (!string.Equals(remaining, "0", StringComparison.OrdinalIgnoreCase))
            return null;

        var resetText = string.Empty;
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues))
        {
            var raw = (resetValues ?? Array.Empty<string>()).FirstOrDefault();
            if (long.TryParse(raw, out var epoch))
            {
                var resetUtc = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                resetText = $"Rate limit resets at {resetUtc:O} UTC.";
            }
        }

        return string.IsNullOrWhiteSpace(resetText)
            ? "GitHub API rate limit exceeded."
            : $"GitHub API rate limit exceeded. {resetText}";
    }

    private static string TrimForMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text!.Trim();
        const int maxLength = 2000;
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized.Substring(0, maxLength) + "...";
    }

    private static string NormalizeRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return string.Empty;

        var normalized = repository!.Trim().Trim('"').Trim();
        normalized = normalized.Trim('/');
        return normalized;
    }

    private static Uri NormalizeApiBaseUri(string? apiBaseUrl)
    {
        var raw = (apiBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return new Uri("https://api.github.com/", UriKind.Absolute);

        if (!raw.EndsWith("/", StringComparison.Ordinal))
            raw += "/";

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid GitHub API base URL: {apiBaseUrl}");

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported GitHub API base URL scheme: {uri.Scheme}");

        return uri;
    }

    private static Uri BuildApiUri(Uri apiBaseUri, string repository, string relativePathWithQuery)
    {
        var repoPath = repository.Trim().Trim('/');
        var relative = $"repos/{repoPath}/{relativePathWithQuery.TrimStart('/')}";
        return new Uri(apiBaseUri, relative);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static HttpClient CreateSharedClient()
    {
        HttpMessageHandler handler;
#if NETFRAMEWORK
        handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
#else
        handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 16
        };
#endif

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge", "1.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
