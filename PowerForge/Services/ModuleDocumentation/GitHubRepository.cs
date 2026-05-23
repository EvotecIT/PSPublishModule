using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerForge;

/// <summary>
/// Minimal GitHub client for fetching repository files via the Contents API.
/// </summary>
/// <summary>
/// Minimal GitHub client for fetching repository files via the Contents API.
/// </summary>
internal sealed class GitHubRepository : IRepoClient
{
    private readonly string _owner;
    private readonly string _repo;
    private readonly string? _token; // optional

    public GitHubRepository(string owner, string repo, string? token = null)
    {
        _owner = owner; _repo = repo; _token = token;
    }

    private HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PSPublishModule", "1.0"));
        if (!string.IsNullOrWhiteSpace(_token))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Gets the repository default branch name.</summary>
    public string GetDefaultBranch()
    {
        try
        {
            using var c = CreateClient();
            var url = $"https://api.github.com/repos/{_owner}/{_repo}";
            var s = c.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            if (root.TryGetProperty("default_branch", out var db)) return db.GetString() ?? "main";
        }
        catch { }
        return "main"; // fallback
    }

    /// <summary>
    /// Gets file content at the specified path and branch. Returns null when not found.
    /// </summary>
    public string? GetFileContent(string path, string branch)
    {
        // Try Contents API (works for private/public; returns base64 content)
        try
        {
            using var c = CreateClient();
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/contents/{EncodeContentPath(path)}?ref={Uri.EscapeDataString(branch ?? "main")}";
            var resp = c.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("encoding", out var enc) && enc.GetString()?.Equals("base64", StringComparison.OrdinalIgnoreCase) == true &&
                root.TryGetProperty("content", out var data))
            {
                var raw = data.GetString() ?? string.Empty;
                var bytes = Convert.FromBase64String(raw.Replace("\n", string.Empty).Replace("\r", string.Empty));
                return Encoding.UTF8.GetString(bytes);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Lists files in a path at the specified branch. Returns only files (no folders).
    /// </summary>
    public List<(string Name, string Path)> ListFiles(string path, string branch)
    {
        var result = new List<(string,string)>();
        try
        {
            using var c = CreateClient();
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/contents/{EncodeContentPath(path)}?ref={Uri.EscapeDataString(branch ?? "main")}";
            var resp = c.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return result;
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeEl)) continue;
                    var type = typeEl.GetString();
                    if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    var p = item.TryGetProperty("path", out var pEl) ? pEl.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(p)) continue;
                    result.Add((name!, p!));
                }
            }
        }
        catch { }
        return result;
    }

    internal static string EncodeContentPathForTesting(string path)
        => EncodeContentPath(path);

    private static string EncodeContentPath(string? path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        return string.Join("/", normalized
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
    }

    /// <summary>
    /// Lists GitHub releases (tag, name, body, published, assets).
    /// </summary>
    public List<RepoRelease> ListReleases()
    {
        var result = new List<RepoRelease>();
        try
        {
            using var c = CreateClient();
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases";
            var resp = c.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return result;
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var r = new RepoRelease();
                r.Tag = rel.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? string.Empty : string.Empty;
                r.Name = rel.TryGetProperty("name", out var name) ? name.GetString() ?? r.Tag : r.Tag;
                r.Url = rel.TryGetProperty("html_url", out var html) ? html.GetString() : null;
                r.Body = rel.TryGetProperty("body", out var body) ? (body.GetString() ?? string.Empty) : string.Empty;
                r.IsDraft = rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
                r.IsPrerelease = rel.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True;
                if (rel.TryGetProperty("published_at", out var pub) && pub.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(pub.GetString(), out var dto)) r.PublishedAt = dto;
                if (rel.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assetsEl.EnumerateArray())
                    {
                        var asset = new RepoReleaseAsset
                        {
                            Name = a.TryGetProperty("name", out var an) ? an.GetString() ?? string.Empty : string.Empty,
                            DownloadUrl = a.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() ?? string.Empty : string.Empty,
                            ContentType = a.TryGetProperty("content_type", out var ct) ? ct.GetString() : null,
                            Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : (long?)null
                        };
                        r.Assets.Add(asset);
                    }
                }
                result.Add(r);
            }
        }
        catch { }
        return result;
    }
}
