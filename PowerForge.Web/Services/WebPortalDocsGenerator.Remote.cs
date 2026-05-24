using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebPortalDocsGenerator
{
    private static async Task<List<string>> GetGitHubPaths(
        string owner,
        string repo,
        string branch,
        HttpClient http,
        List<string> include,
        List<string> exclude,
        string? token,
        WebPortalDocsSource normalizedSource,
        List<string> warnings)
    {
        var treeUrl = $"https://api.github.com/repos/{owner}/{repo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, treeUrl);
        ApplyGitHubAuthorization(request, token);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AddSourceWarning(normalizedSource, warnings, $"GitHub tree request for '{owner}/{repo}@{branch}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
            return ExplicitIncludePaths(include);
        }

        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
            return ExplicitIncludePaths(include);
        if (json.RootElement.TryGetProperty("truncated", out var truncated) &&
            truncated.ValueKind == JsonValueKind.True)
        {
            AddSourceWarning(normalizedSource, warnings, $"GitHub tree request for '{owner}/{repo}@{branch}' was truncated; generated repository docs may be incomplete.");
        }

        return tree.EnumerateArray()
            .Where(item => item.TryGetProperty("type", out var type) && type.GetString()?.Equals("blob", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => item.TryGetProperty("path", out var path) ? NormalizePath(path.GetString() ?? string.Empty) : string.Empty)
            .Where(path => path.Length > 0)
            .Where(path => IsIncluded(path, include, exclude))
            .Where(IsTextDocument)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<string>> GetAzureDevOpsPaths(
        WebPortalDocsSourceSpec source,
        string repository,
        string branch,
        HttpClient http,
        List<string> include,
        List<string> exclude,
        string? token,
        WebPortalDocsSource normalizedSource,
        List<string> warnings)
    {
        var url = BuildAzureDevOpsListUrl(source.Organization!, source.Project!, repository, branch);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAzureDevOpsAuthorization(request, source, token);
        using var response = await http.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AddSourceWarning(normalizedSource, warnings, $"Azure DevOps items request for '{source.Organization}/{source.Project}/{repository}@{branch}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
            return ExplicitIncludePaths(include);
        }

        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("value", out var items) || items.ValueKind != JsonValueKind.Array)
            return ExplicitIncludePaths(include);

        return items.EnumerateArray()
            .Where(item => !IsAzureDevOpsFolder(item))
            .Select(item => item.TryGetProperty("path", out var path) ? NormalizePath(path.GetString() ?? string.Empty) : string.Empty)
            .Where(path => path.Length > 0)
            .Where(path => IsIncluded(path, include, exclude))
            .Where(IsTextDocument)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<string?> FetchAzureDevOpsText(
        string url,
        HttpClient http,
        int maxContentBytes,
        WebPortalDocsSourceSpec sourceSpec,
        string? token,
        WebPortalDocsSource source,
        List<string> warnings)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAzureDevOpsAuthorization(request, sourceSpec, token);
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AddSourceWarning(source, warnings, $"Azure DevOps content request '{url}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var body = Encoding.UTF8.GetString(bytes);
            if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    return TruncateText(content.GetString(), maxContentBytes, url, "Azure DevOps content request", source, warnings);
            }

            return TruncateBytes(bytes, maxContentBytes, url, "Azure DevOps content request", source, warnings);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            AddSourceWarning(source, warnings, $"Azure DevOps content request '{url}' failed: {ex.Message}");
            return null;
        }
    }

    private static List<string> ExplicitIncludePaths(List<string> include)
        => include
            .Where(pattern => !ContainsWildcard(pattern))
            .Select(NormalizePath)
            .Where(IsTextDocument)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<string?> FetchText(string rawUrl, HttpClient http, int maxContentBytes, string? token, WebPortalDocsSource source, List<string> warnings)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            ApplyGitHubAuthorization(request, token);
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AddSourceWarning(source, warnings, $"Content request '{rawUrl}' failed with {(int)response.StatusCode} {response.ReasonPhrase}.");
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return TruncateBytes(bytes, maxContentBytes, rawUrl, "Content request", source, warnings);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AddSourceWarning(source, warnings, $"Content request '{rawUrl}' failed: {ex.Message}");
            return null;
        }
    }

    private static string TruncateBytes(byte[] bytes, int maxContentBytes, string url, string label, WebPortalDocsSource source, List<string> warnings)
    {
        if (bytes.Length > maxContentBytes)
        {
            AddSourceWarning(source, warnings, $"{label} '{url}' returned {bytes.Length} bytes and was truncated to {maxContentBytes} bytes.");
            bytes = bytes.Take(maxContentBytes).ToArray();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? TruncateText(string? value, int maxContentBytes, string url, string label, WebPortalDocsSource source, List<string> warnings)
    {
        if (value is null)
            return null;

        var bytes = Encoding.UTF8.GetBytes(value);
        return TruncateBytes(bytes, maxContentBytes, url, label, source, warnings);
    }

    private static bool IsAzureDevOpsFolder(JsonElement item)
    {
        if (item.TryGetProperty("isFolder", out var isFolder) && isFolder.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return isFolder.GetBoolean();

        return item.TryGetProperty("gitObjectType", out var objectType) &&
               objectType.GetString()?.Equals("tree", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string BuildGitHubRawUrl(string owner, string repo, string branch, string path)
        => $"https://raw.githubusercontent.com/{owner}/{repo}/{Uri.EscapeDataString(branch)}/{EscapePath(path)}";

    private static string BuildAzureDevOpsListUrl(string organization, string project, string repository, string branch)
        => $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repository)}/items?scopePath=/&recursionLevel=Full&includeContentMetadata=true&versionDescriptor.version={Uri.EscapeDataString(branch)}&versionDescriptor.versionType=branch&api-version=7.1";

    private static string BuildAzureDevOpsItemUrl(string organization, string project, string repository, string branch, string path, bool includeContent)
        => $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/_apis/git/repositories/{Uri.EscapeDataString(repository)}/items?path=/{EscapePath(path)}&includeContent={includeContent.ToString().ToLowerInvariant()}&versionDescriptor.version={Uri.EscapeDataString(branch)}&versionDescriptor.versionType=branch&api-version=7.1";

    private static void ApplyAzureDevOpsAuthorization(HttpRequestMessage request, WebPortalDocsSourceSpec? source, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var auth = source?.Authentication ?? source?.Auth ?? string.Empty;
        if (auth.Equals("basic", StringComparison.OrdinalIgnoreCase) ||
            auth.Equals("basic-token", StringComparison.OrdinalIgnoreCase) ||
            auth.Equals("pat", StringComparison.OrdinalIgnoreCase))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + token));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static void ApplyGitHubAuthorization(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string EscapePath(string path)
        => string.Join("/", NormalizePath(path).Split('/').Select(Uri.EscapeDataString));
}
