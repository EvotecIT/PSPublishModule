using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly Dictionary<string, WordPressExportEndpointSpec> WordPressExportEndpointMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["posts"] = new WordPressExportEndpointSpec("posts", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["status"] = "publish" }),
        ["pages"] = new WordPressExportEndpointSpec("pages", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["status"] = "publish" }),
        ["categories"] = new WordPressExportEndpointSpec("categories"),
        ["tags"] = new WordPressExportEndpointSpec("tags"),
        ["media"] = new WordPressExportEndpointSpec("media", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["status"] = "inherit" })
    };

    private static void ExecuteWordPressExportSnapshot(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var sourceUrl = (GetString(step, "siteUrl")
                         ?? GetString(step, "site-url")
                         ?? GetString(step, "sourceUrl")
                         ?? GetString(step, "source-url")
                         ?? GetString(step, "url")
                         ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceUrl))
            throw new InvalidOperationException("wordpress-export-snapshot: siteUrl is required.");

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) ||
            !(sourceUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              sourceUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"wordpress-export-snapshot: invalid siteUrl '{sourceUrl}'.");
        }

        var outputPath = ResolvePath(baseDir,
            GetString(step, "out")
            ?? GetString(step, "output")
            ?? GetString(step, "outputPath")
            ?? GetString(step, "output-path")
            ?? GetString(step, "destination")
            ?? GetString(step, "dest")
            ?? GetString(step, "path"));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("wordpress-export-snapshot: out/outputPath is required.");
        var outputRoot = Path.GetFullPath(outputPath);

        var languageQueryParameter = (GetString(step, "languageQueryParameter")
                                      ?? GetString(step, "language-query-parameter")
                                      ?? GetString(step, "queryLanguageParameter")
                                      ?? GetString(step, "query-language-parameter")
                                      ?? "wpml_language").Trim();
        var context = (GetString(step, "context") ?? "view").Trim();

        var recordsPerPage = GetInt(step, "recordsPerPage")
                            ?? GetInt(step, "records-per-page")
                            ?? 100;
        recordsPerPage = Math.Clamp(recordsPerPage, 1, 100);

        var timeoutSeconds = GetInt(step, "timeoutSeconds")
                             ?? GetInt(step, "timeout-seconds")
                             ?? 60;
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 300);

        var includeEmbed = GetBool(step, "includeEmbed")
                           ?? GetBool(step, "include-embed")
                           ?? false;
        var force = GetBool(step, "force") ?? false;
        var whatIf = GetBool(step, "whatIf") ?? GetBool(step, "what-if") ?? false;
        var continueOnError = GetBool(step, "continueOnError")
                              ?? GetBool(step, "continue-on-error")
                              ?? true;
        var authMode = ResolveWordPressExportAuthMode(step);

        var sharedQuery = ResolveWordPressExportSharedQuery(step);
        var perCollectionQuery = ResolveWordPressExportPerCollectionQuery(step);

        var collections = ResolveWordPressExportCollections(step);
        var languages = ResolveWordPressExportLanguages(step);

        if (Directory.Exists(outputRoot) && !force && !whatIf)
        {
            var hasEntries = Directory.EnumerateFileSystemEntries(outputRoot).Any();
            if (hasEntries)
                throw new InvalidOperationException($"wordpress-export-snapshot: output path '{outputRoot}' already contains files. Use force=true to overwrite.");
        }

        var rawRoot = Path.Combine(outputRoot, "raw");
        var reportsRoot = Path.Combine(outputRoot, "_reports");
        var manifestPath = ResolvePath(baseDir, GetString(step, "manifestPath") ?? GetString(step, "manifest-path"))
                           ?? Path.Combine(outputRoot, "snapshot.manifest.json");
        var summaryPath = ResolvePath(baseDir, GetString(step, "summaryPath") ?? GetString(step, "summary-path"))
                          ?? Path.Combine(reportsRoot, "summary.json");

        if (!whatIf)
        {
            Directory.CreateDirectory(rawRoot);
            Directory.CreateDirectory(reportsRoot);
        }

        var files = new List<string>();
        var warnings = new List<string>();
        var counts = new SortedDictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        using var http = CreateWordPressExportHttpClient(step, timeoutSeconds, authMode);

        foreach (var language in languages)
        {
            var languageCode = string.IsNullOrWhiteSpace(language) ? "default" : language.Trim();
            var languageRoot = Path.Combine(rawRoot, languageCode);
            if (!whatIf)
                Directory.CreateDirectory(languageRoot);

            var languageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            counts[languageCode] = languageCounts;

            foreach (var collection in collections)
            {
                if (!WordPressExportEndpointMap.TryGetValue(collection, out var endpointSpec))
                    continue;

                var query = new Dictionary<string, string>(endpointSpec.DefaultQuery, StringComparer.OrdinalIgnoreCase);
                ApplyWordPressExportQueryOverrides(query, sharedQuery);
                if (perCollectionQuery.TryGetValue(collection, out var collectionOverrides))
                    ApplyWordPressExportQueryOverrides(query, collectionOverrides);
                if (!string.IsNullOrWhiteSpace(context))
                    query["context"] = context;
                if (includeEmbed)
                    query["_embed"] = "1";
                if (!string.IsNullOrWhiteSpace(language) &&
                    !string.IsNullOrWhiteSpace(languageQueryParameter) &&
                    !language.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    query[languageQueryParameter] = language.Trim();
                }

                List<JsonElement> items;
                try
                {
                    items = FetchWordPressEndpointItems(http, sourceUrl, endpointSpec.Endpoint, query, recordsPerPage);
                }
                catch (Exception ex)
                {
                    if (!continueOnError)
                        throw;

                    warnings.Add($"[{languageCode}/{collection}] {ex.Message}");
                    items = new List<JsonElement>();
                }

                languageCounts[collection] = items.Count;
                var outputFile = Path.Combine(languageRoot, collection + ".json");
                if (!whatIf)
                {
                    WriteWordPressExportJsonArray(outputFile, items);
                    files.Add(outputFile);
                }
            }
        }

        var summary = new WordPressExportSnapshotSummary
        {
            GeneratedOn = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            SourceUrl = sourceUrl,
            Mode = "public-rest",
            Collections = collections,
            Languages = languages.Select(static value => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim()).ToArray(),
            RecordsPerPage = recordsPerPage,
            IncludeEmbed = includeEmbed,
            AuthMode = authMode,
            ContinueOnError = continueOnError,
            WhatIf = whatIf,
            Counts = counts,
            Files = files,
            Warnings = warnings
        };

        if (!whatIf)
        {
            WriteWordPressExportJsonObject(manifestPath, summary);
            summary.Files.Add(manifestPath);
            WriteWordPressExportJsonObject(summaryPath, summary);
            summary.Files.Add(summaryPath);
        }

        stepResult.Success = true;
        stepResult.Message = $"wordpress-export-snapshot ok: languages={summary.Languages.Length}; collections={collections.Length}; files={files.Count}; warnings={warnings.Count}";
    }

    private static HttpClient CreateWordPressExportHttpClient(JsonElement step, int timeoutSeconds, string authMode)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        var userAgent = GetString(step, "userAgent") ?? GetString(step, "user-agent");
        if (string.IsNullOrWhiteSpace(userAgent))
            userAgent = "PowerForge.Web/wordpress-export-snapshot";
        if (!string.IsNullOrWhiteSpace(userAgent))
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent.Trim());

        var token = ResolveWordPressExportDirectValue(step, "token") ??
                    ResolveWordPressExportEnvValue(step, "tokenEnv", "token-env");
        var authorizationHeader = ResolveWordPressExportDirectValue(step, "authorizationHeader", "authorization-header") ??
                                  ResolveWordPressExportEnvValue(step, "authorizationHeaderEnv", "authorization-header-env");
        var username = ResolveWordPressExportDirectValue(step, "username") ??
                       ResolveWordPressExportEnvValue(step, "usernameEnv", "username-env");
        var password = ResolveWordPressExportDirectValue(step, "password") ??
                       ResolveWordPressExportEnvValue(step, "passwordEnv", "password-env");
        var basicToken = ResolveWordPressExportDirectValue(step, "basicToken", "basic-token") ??
                         ResolveWordPressExportEnvValue(step, "basicTokenEnv", "basic-token-env");
        if (string.IsNullOrWhiteSpace(basicToken) &&
            !string.IsNullOrWhiteSpace(username) &&
            password is not null)
        {
            basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        }

        ApplyWordPressExportAuthorization(client, authMode, authorizationHeader, token, basicToken);

        if (step.ValueKind == JsonValueKind.Object &&
            step.TryGetProperty("headers", out var headersElement) &&
            headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in headersElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                var name = property.Name;
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                    AuthenticationHeaderValue.TryParse(value.Trim(), out var parsedAuth))
                {
                    client.DefaultRequestHeaders.Authorization = parsedAuth;
                    continue;
                }

                _ = client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            }
        }

        return client;
    }

    private static void ApplyWordPressExportAuthorization(
        HttpClient client,
        string authMode,
        string? authorizationHeader,
        string? token,
        string? basicToken)
    {
        switch (authMode)
        {
            case "none":
                return;
            case "header":
                if (string.IsNullOrWhiteSpace(authorizationHeader) ||
                    !AuthenticationHeaderValue.TryParse(authorizationHeader.Trim(), out var parsedHeader))
                {
                    throw new InvalidOperationException("wordpress-export-snapshot: authMode=header requires a valid authorizationHeader.");
                }
                client.DefaultRequestHeaders.Authorization = parsedHeader;
                return;
            case "bearer":
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("wordpress-export-snapshot: authMode=bearer requires token/tokenEnv.");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
                return;
            case "basic":
                if (string.IsNullOrWhiteSpace(basicToken))
                    throw new InvalidOperationException("wordpress-export-snapshot: authMode=basic requires username/password (or basicToken).");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken.Trim());
                return;
            case "auto":
                if (!string.IsNullOrWhiteSpace(authorizationHeader) &&
                    AuthenticationHeaderValue.TryParse(authorizationHeader.Trim(), out var parsedAuto))
                {
                    client.DefaultRequestHeaders.Authorization = parsedAuto;
                    return;
                }
                if (!string.IsNullOrWhiteSpace(token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
                    return;
                }
                if (!string.IsNullOrWhiteSpace(basicToken))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken.Trim());
                    return;
                }
                return;
            default:
                throw new InvalidOperationException($"wordpress-export-snapshot: unsupported authMode '{authMode}'.");
        }
    }

    private static string ResolveWordPressExportAuthMode(JsonElement step)
    {
        var raw = GetString(step, "authMode") ?? GetString(step, "auth-mode") ?? "auto";
        var mode = raw.Trim().ToLowerInvariant();
        return mode is "auto" or "none" or "bearer" or "basic" or "header"
            ? mode
            : throw new InvalidOperationException($"wordpress-export-snapshot: unsupported authMode '{raw}'.");
    }

    private static string? ResolveWordPressExportDirectValue(JsonElement step, params string[] valuePropertyCandidates)
    {
        foreach (var candidate in valuePropertyCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var value = GetString(step, candidate);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ResolveWordPressExportEnvValue(JsonElement step, params string[] envPropertyCandidates)
    {
        foreach (var candidate in envPropertyCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var envName = GetString(step, candidate);
            if (string.IsNullOrWhiteSpace(envName))
                continue;

            var envValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(envValue))
                return envValue;
        }

        return null;
    }

    private static Dictionary<string, string> ResolveWordPressExportSharedQuery(JsonElement step)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var status = GetString(step, "status");
        if (!string.IsNullOrWhiteSpace(status))
            result["status"] = status.Trim();

        var after = GetString(step, "after");
        if (!string.IsNullOrWhiteSpace(after))
            result["after"] = after.Trim();

        var before = GetString(step, "before");
        if (!string.IsNullOrWhiteSpace(before))
            result["before"] = before.Trim();

        var search = GetString(step, "search");
        if (!string.IsNullOrWhiteSpace(search))
            result["search"] = search.Trim();

        var includeIds = ParseIntList(GetArrayOfStrings(step, "includeIds") ??
                                      GetArrayOfStrings(step, "include-ids") ??
                                      GetArrayOfStrings(step, "include"));
        if (includeIds.Length > 0)
            result["include"] = string.Join(",", includeIds);

        var excludeIds = ParseIntList(GetArrayOfStrings(step, "excludeIds") ??
                                      GetArrayOfStrings(step, "exclude-ids") ??
                                      GetArrayOfStrings(step, "exclude"));
        if (excludeIds.Length > 0)
            result["exclude"] = string.Join(",", excludeIds);

        var queryFromObject = ResolveWordPressExportQueryObject(step, "query") ??
                              ResolveWordPressExportQueryObject(step, "queryOverrides") ??
                              ResolveWordPressExportQueryObject(step, "query-overrides");
        if (queryFromObject is not null)
            ApplyWordPressExportQueryOverrides(result, queryFromObject);

        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> ResolveWordPressExportPerCollectionQuery(JsonElement step)
    {
        var element = GetWordPressExportObject(step, "perCollectionQuery") ??
                      GetWordPressExportObject(step, "per-collection-query");
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (element is null)
            return result;

        foreach (var property in element.Value.EnumerateObject())
        {
            var collection = property.Name?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(collection) || !WordPressExportEndpointMap.ContainsKey(collection))
                continue;
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var overrides = ConvertWordPressExportObjectToQueryDictionary(property.Value);
            if (overrides.Count > 0)
                result[collection] = overrides;
        }

        return result;
    }

    private static JsonElement? GetWordPressExportObject(JsonElement step, string propertyName)
    {
        if (step.ValueKind != JsonValueKind.Object)
            return null;
        if (!step.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return value;
    }

    private static Dictionary<string, string>? ResolveWordPressExportQueryObject(JsonElement step, string propertyName)
    {
        var element = GetWordPressExportObject(step, propertyName);
        if (element is null)
            return null;

        var dictionary = ConvertWordPressExportObjectToQueryDictionary(element.Value);
        return dictionary.Count == 0 ? null : dictionary;
    }

    private static Dictionary<string, string> ConvertWordPressExportObjectToQueryDictionary(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in element.EnumerateObject())
        {
            var key = property.Name?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = ConvertWordPressExportQueryValue(property.Value);
            if (value is null)
                continue;

            result[key] = value;
        }

        return result;
    }

    private static string? ConvertWordPressExportQueryValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return value.ToString();
            case JsonValueKind.Array:
            {
                var parts = value.EnumerateArray()
                    .Select(ConvertWordPressExportQueryValue)
                    .Where(static part => !string.IsNullOrWhiteSpace(part))
                    .Select(static part => part!.Trim())
                    .ToArray();
                return parts.Length == 0 ? null : string.Join(",", parts);
            }
            default:
                return null;
        }
    }

    private static void ApplyWordPressExportQueryOverrides(Dictionary<string, string> target, Dictionary<string, string> overrides)
    {
        foreach (var pair in overrides)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;
            target[pair.Key.Trim()] = pair.Value.Trim();
        }
    }

    private static List<JsonElement> FetchWordPressEndpointItems(
        HttpClient http,
        string siteUrl,
        string endpoint,
        IReadOnlyDictionary<string, string> baseQuery,
        int perPage)
    {
        var items = new List<JsonElement>();
        var page = 1;
        int? totalPages = null;

        while (true)
        {
            var query = new Dictionary<string, string>(baseQuery, StringComparer.OrdinalIgnoreCase)
            {
                ["per_page"] = perPage.ToString(CultureInfo.InvariantCulture),
                ["page"] = page.ToString(CultureInfo.InvariantCulture)
            };
            if (!query.ContainsKey("context"))
                query["context"] = "view";

            var uri = BuildWordPressApiEndpointUri(siteUrl, endpoint, query);
            using var response = http.GetAsync(uri).GetAwaiter().GetResult();
            var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                if (IsWordPressInvalidPageResponse(response.StatusCode, payload))
                    break;

                var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "request failed" : response.ReasonPhrase;
                throw new InvalidOperationException($"GET {uri} failed ({(int)response.StatusCode} {reason}).");
            }

            if (string.IsNullOrWhiteSpace(payload))
                payload = "[]";

            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"GET {uri} returned invalid payload (expected array).");

            var pageCount = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                items.Add(item.Clone());
                pageCount++;
            }

            if (!totalPages.HasValue && TryGetWordPressTotalPages(response, out var parsedTotalPages))
                totalPages = parsedTotalPages;

            if (pageCount == 0)
                break;
            if (totalPages.HasValue && page >= totalPages.Value)
                break;
            if (!totalPages.HasValue && pageCount < perPage)
                break;

            page++;
        }

        return items;
    }

    private static string[] ResolveWordPressExportCollections(JsonElement step)
    {
        var values = GetArrayOfStrings(step, "collections");
        if (values is null || values.Length == 0)
            values = new[] { "posts", "pages", "categories", "tags", "media" };

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Where(value => WordPressExportEndpointMap.ContainsKey(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0
            ? new[] { "posts", "pages", "categories", "tags", "media" }
            : normalized;
    }

    private static string[] ResolveWordPressExportLanguages(JsonElement step)
    {
        var values = GetArrayOfStrings(step, "languages");
        if (values is null || values.Length == 0)
            return new[] { "default" };

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? new[] { "default" } : normalized;
    }

    private static string BuildWordPressApiEndpointUri(string siteUrl, string endpoint, IReadOnlyDictionary<string, string> query)
    {
        var trimmedSite = siteUrl.TrimEnd('/');
        var trimmedEndpoint = endpoint.Trim().TrimStart('/');
        var queryString = BuildWordPressQueryString(query);
        return string.IsNullOrWhiteSpace(queryString)
            ? $"{trimmedSite}/wp-json/wp/v2/{trimmedEndpoint}"
            : $"{trimmedSite}/wp-json/wp/v2/{trimmedEndpoint}?{queryString}";
    }

    private static string BuildWordPressQueryString(IReadOnlyDictionary<string, string> query)
    {
        if (query.Count == 0)
            return string.Empty;

        var parts = new List<string>(query.Count);
        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var key = Uri.EscapeDataString(pair.Key);
            var value = Uri.EscapeDataString(pair.Value ?? string.Empty);
            parts.Add(key + "=" + value);
        }

        return string.Join("&", parts);
    }

    private static bool TryGetWordPressTotalPages(HttpResponseMessage response, out int totalPages)
    {
        totalPages = 0;
        if (!response.Headers.TryGetValues("X-WP-TotalPages", out var values))
            return false;

        var value = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out totalPages) &&
               totalPages > 0;
    }

    private static bool IsWordPressInvalidPageResponse(HttpStatusCode statusCode, string payload)
    {
        if (statusCode is not HttpStatusCode.BadRequest and not HttpStatusCode.NotFound)
            return false;
        return payload.IndexOf("rest_post_invalid_page_number", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void WriteWordPressExportJsonArray(string path, IReadOnlyList<JsonElement> items)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var item in items)
            item.WriteTo(writer);
        writer.WriteEndArray();
    }

    private static void WriteWordPressExportJsonObject(string path, object value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private sealed class WordPressExportEndpointSpec
    {
        internal WordPressExportEndpointSpec(string endpoint, Dictionary<string, string>? defaultQuery = null)
        {
            Endpoint = endpoint;
            DefaultQuery = defaultQuery ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal string Endpoint { get; }
        internal Dictionary<string, string> DefaultQuery { get; }
    }

    private sealed class WordPressExportSnapshotSummary
    {
        public string GeneratedOn { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string[] Collections { get; set; } = Array.Empty<string>();
        public string[] Languages { get; set; } = Array.Empty<string>();
        public int RecordsPerPage { get; set; }
        public bool IncludeEmbed { get; set; }
        public string AuthMode { get; set; } = "auto";
        public bool ContinueOnError { get; set; } = true;
        public bool WhatIf { get; set; }
        public SortedDictionary<string, Dictionary<string, int>> Counts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Files { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}
