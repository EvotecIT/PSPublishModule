using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PowerForge;

/// <summary>
/// GitHub Actions cache cleanup service used to reclaim repository cache quota.
/// </summary>
public sealed class GitHubActionsCacheCleanupService
{
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private static readonly HttpClient SharedClient = CreateSharedClient();

    /// <summary>
    /// Creates a cache cleanup service with a logger and optional custom HTTP client.
    /// </summary>
    /// <param name="logger">Logger used for progress and diagnostics.</param>
    /// <param name="client">Optional HTTP client (for tests/custom transports).</param>
    public GitHubActionsCacheCleanupService(ILogger logger, HttpClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? SharedClient;
    }

    /// <summary>
    /// Executes a cleanup run against GitHub Actions caches.
    /// </summary>
    /// <param name="spec">Cleanup specification.</param>
    /// <returns>Run summary with planned/deleted items and counters.</returns>
    public GitHubActionsCacheCleanupResult Prune(GitHubActionsCacheCleanupSpec spec)
        => PruneAsync(spec).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>
    /// Executes a cleanup run against GitHub Actions caches.
    /// </summary>
    /// <param name="spec">Cleanup specification.</param>
    /// <returns>Run summary with planned/deleted items and counters.</returns>
    public async Task<GitHubActionsCacheCleanupResult> PruneAsync(GitHubActionsCacheCleanupSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var normalized = NormalizeSpec(spec);
        var usageBefore = await TryGetUsageAsync(normalized.ApiBaseUri, normalized.Repository, normalized.Token).ConfigureAwait(false);
        var allCaches = await ListCachesAsync(normalized.ApiBaseUri, normalized.Repository, normalized.Token, normalized.PageSize).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var ageCutoff = normalized.MaxAgeDays is > 0
            ? now.AddDays(-normalized.MaxAgeDays.Value)
            : (DateTimeOffset?)null;

        var matched = allCaches
            .Where(c => MatchesAnyPattern(c.Key, normalized.IncludeKeys, defaultWhenEmpty: true))
            .Where(c => !MatchesAnyPattern(c.Key, normalized.ExcludeKeys, defaultWhenEmpty: false))
            .ToArray();

        var planned = new List<GitHubActionsCacheCleanupItem>();
        var keptByRecentWindow = 0;
        var keptByAgeThreshold = 0;

        foreach (var byKey in matched.GroupBy(c => c.Key ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = byKey
                .OrderByDescending(GetSortTimestamp)
                .ThenByDescending(c => c.Id)
                .ToArray();

            for (var index = 0; index < ordered.Length; index++)
            {
                var cache = ordered[index];
                if (index < normalized.KeepLatestPerKey)
                {
                    keptByRecentWindow++;
                    continue;
                }

                if (ageCutoff is not null && GetSortTimestamp(cache) > ageCutoff.Value)
                {
                    keptByAgeThreshold++;
                    continue;
                }

                planned.Add(ToItem(cache, BuildSelectionReason(normalized, ageCutoff)));
            }
        }

        var orderedPlanned = planned
            .OrderBy(c => c.LastAccessedAt ?? c.CreatedAt ?? DateTimeOffset.MinValue)
            .ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Id)
            .Take(normalized.MaxDelete)
            .ToArray();

        var result = new GitHubActionsCacheCleanupResult
        {
            Repository = normalized.Repository,
            IncludeKeys = normalized.IncludeKeys,
            ExcludeKeys = normalized.ExcludeKeys,
            KeepLatestPerKey = normalized.KeepLatestPerKey,
            MaxAgeDays = normalized.MaxAgeDays is > 0 ? normalized.MaxAgeDays : null,
            MaxDelete = normalized.MaxDelete,
            DryRun = normalized.DryRun,
            UsageBefore = usageBefore,
            ScannedCaches = allCaches.Length,
            MatchedCaches = matched.Length,
            KeptByRecentWindow = keptByRecentWindow,
            KeptByAgeThreshold = keptByAgeThreshold,
            Planned = orderedPlanned,
            PlannedDeletes = orderedPlanned.Length,
            PlannedDeleteBytes = orderedPlanned.Sum(c => c.SizeInBytes),
            Success = true
        };

        _logger.Info($"GitHub caches scanned: {result.ScannedCaches}, matched: {result.MatchedCaches}, planned: {result.PlannedDeletes}.");

        if (normalized.DryRun || orderedPlanned.Length == 0)
        {
            if (normalized.DryRun)
                _logger.Info("GitHub cache cleanup dry-run enabled. No delete requests were sent.");
            return result;
        }

        var deleted = new List<GitHubActionsCacheCleanupItem>();
        var failed = new List<GitHubActionsCacheCleanupItem>();

        foreach (var item in orderedPlanned)
        {
            var deleteResult = await DeleteCacheAsync(normalized.ApiBaseUri, normalized.Repository, normalized.Token, item.Id).ConfigureAwait(false);
            if (deleteResult.Ok)
            {
                deleted.Add(item);
                _logger.Verbose($"Deleted cache #{item.Id} ({item.Key}).");
                continue;
            }

            item.DeleteStatusCode = deleteResult.StatusCode;
            item.DeleteError = deleteResult.Error;
            failed.Add(item);
            _logger.Warn($"Failed to delete cache #{item.Id} ({item.Key}): {deleteResult.Error}");
        }

        result.Deleted = deleted.ToArray();
        result.Failed = failed.ToArray();
        result.DeletedCaches = deleted.Count;
        result.DeletedBytes = deleted.Sum(c => c.SizeInBytes);
        result.FailedDeletes = failed.Count;
        result.UsageAfter = await TryGetUsageAsync(normalized.ApiBaseUri, normalized.Repository, normalized.Token).ConfigureAwait(false);
        result.Success = failed.Count == 0 || !normalized.FailOnDeleteError;

        if (!result.Success)
            result.Message = "One or more cache delete operations failed.";
        else if (failed.Count > 0)
            result.Message = "Cleanup finished with non-fatal delete errors.";

        return result;
    }

    private sealed class GitHubActionsCacheRecord
    {
        public long Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string? Ref { get; set; }
        public string? Version { get; set; }
        public long SizeInBytes { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? LastAccessedAt { get; set; }
    }

    private sealed class NormalizedSpec
    {
        public Uri ApiBaseUri { get; set; } = new Uri("https://api.github.com/", UriKind.Absolute);
        public string Repository { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string[] IncludeKeys { get; set; } = Array.Empty<string>();
        public string[] ExcludeKeys { get; set; } = Array.Empty<string>();
        public int KeepLatestPerKey { get; set; }
        public int? MaxAgeDays { get; set; }
        public int MaxDelete { get; set; }
        public int PageSize { get; set; }
        public bool DryRun { get; set; }
        public bool FailOnDeleteError { get; set; }
    }

    private NormalizedSpec NormalizeSpec(GitHubActionsCacheCleanupSpec spec)
    {
        var repository = NormalizeRepository(spec.Repository);
        if (string.IsNullOrWhiteSpace(repository))
            throw new InvalidOperationException("Repository is required (owner/repo).");

        var token = (spec.Token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub token is required.");

        return new NormalizedSpec
        {
            ApiBaseUri = NormalizeApiBaseUri(spec.ApiBaseUrl),
            Repository = repository,
            Token = token,
            IncludeKeys = NormalizePatterns(spec.IncludeKeys),
            ExcludeKeys = NormalizePatterns(spec.ExcludeKeys),
            KeepLatestPerKey = Math.Max(0, spec.KeepLatestPerKey),
            MaxAgeDays = spec.MaxAgeDays is < 1 ? null : spec.MaxAgeDays,
            MaxDelete = Math.Max(1, spec.MaxDelete),
            PageSize = Clamp(spec.PageSize, 1, 100),
            DryRun = spec.DryRun,
            FailOnDeleteError = spec.FailOnDeleteError
        };
    }

    private async Task<GitHubActionsCacheUsage?> TryGetUsageAsync(Uri apiBaseUri, string repository, string token)
    {
        try
        {
            var uri = BuildApiUri(apiBaseUri, repository, "actions/cache/usage");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _client.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Verbose($"GitHub cache usage lookup failed: HTTP {(int)response.StatusCode}.");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new GitHubActionsCacheUsage
            {
                ActiveCachesCount = TryGetInt32(root, "active_caches_count"),
                ActiveCachesSizeInBytes = TryGetInt64(root, "active_caches_size_in_bytes")
            };
        }
        catch (Exception ex)
        {
            _logger.Verbose($"GitHub cache usage lookup failed: {ex.Message}");
            return null;
        }
    }

    private async Task<GitHubActionsCacheRecord[]> ListCachesAsync(Uri apiBaseUri, string repository, string token, int pageSize)
    {
        var records = new List<GitHubActionsCacheRecord>();
        var page = 1;
        int? totalCount = null;

        while (true)
        {
            var uri = BuildApiUri(apiBaseUri, repository, $"actions/caches?per_page={pageSize}&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _client.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw BuildHttpFailure("listing caches", response, body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("total_count", out var totalElement) && totalElement.ValueKind == JsonValueKind.Number)
                totalCount = totalElement.GetInt32();

            if (!root.TryGetProperty("actions_caches", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
                break;

            var pageItems = itemsElement.EnumerateArray().Select(ParseCache).ToArray();
            if (pageItems.Length == 0)
                break;

            records.AddRange(pageItems);

            if (pageItems.Length < pageSize)
                break;

            if (totalCount.HasValue && records.Count >= totalCount.Value)
                break;

            page++;
        }

        return records.ToArray();
    }

    private async Task<(bool Ok, int? StatusCode, string? Error)> DeleteCacheAsync(Uri apiBaseUri, string repository, string token, long cacheId)
    {
        var uri = BuildApiUri(apiBaseUri, repository, $"actions/caches/{cacheId}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _client.SendAsync(request).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return (true, (int)response.StatusCode, null);

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var error = BuildHttpFailure("deleting cache", response, body).Message;
        return (false, (int)response.StatusCode, error);
    }

    private static DateTimeOffset GetSortTimestamp(GitHubActionsCacheRecord record)
        => record.LastAccessedAt ?? record.CreatedAt ?? DateTimeOffset.MinValue;

    private static GitHubActionsCacheCleanupItem ToItem(GitHubActionsCacheRecord record, string? reason)
    {
        return new GitHubActionsCacheCleanupItem
        {
            Id = record.Id,
            Key = record.Key,
            Ref = record.Ref,
            Version = record.Version,
            SizeInBytes = record.SizeInBytes,
            CreatedAt = record.CreatedAt,
            LastAccessedAt = record.LastAccessedAt,
            Reason = reason
        };
    }

    private static string BuildSelectionReason(NormalizedSpec spec, DateTimeOffset? ageCutoff)
    {
        if (ageCutoff is null)
            return $"older-than-keep-window:{spec.KeepLatestPerKey}";

        return $"older-than-keep-window:{spec.KeepLatestPerKey};older-than-days:{spec.MaxAgeDays}";
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

    private static GitHubActionsCacheRecord ParseCache(JsonElement cache)
    {
        return new GitHubActionsCacheRecord
        {
            Id = TryGetInt64(cache, "id"),
            Key = TryGetString(cache, "key") ?? string.Empty,
            Ref = TryGetString(cache, "ref"),
            Version = TryGetString(cache, "version"),
            SizeInBytes = TryGetInt64(cache, "size_in_bytes"),
            CreatedAt = TryGetDateTimeOffset(cache, "created_at"),
            LastAccessedAt = TryGetDateTimeOffset(cache, "last_accessed_at")
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

    private static int TryGetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return 0;

        if (value.ValueKind != JsonValueKind.Number)
            return 0;

        return value.TryGetInt32(out var parsed) ? parsed : 0;
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
