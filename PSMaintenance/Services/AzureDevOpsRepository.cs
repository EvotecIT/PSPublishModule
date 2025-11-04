using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PSMaintenance;

/// <summary>
/// Minimal Azure DevOps client for fetching repository files via the Git REST API.
/// </summary>
internal sealed class AzureDevOpsRepository : IRepoClient
{
    private readonly string _organization;
    private readonly string _project;
    private readonly string _repository;
    private readonly string? _pat; // required for private repos

    public AzureDevOpsRepository(string organization, string project, string repository, string? pat = null)
    {
        _organization = organization; _project = project; _repository = repository; _pat = pat;
    }

    private HttpClient CreateClient()
    {
        var c = new HttpClient();
        if (!string.IsNullOrWhiteSpace(_pat))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    /// <summary>Gets the repository default branch name.</summary>
    public string GetDefaultBranch()
    {
        try
        {
            using var c = CreateClient();
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{_repository}?api-version=7.1-preview.1";
            var s = c.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            if (root.TryGetProperty("defaultBranch", out var db))
            {
                var v = db.GetString();
                if (v != null && v.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
                    return v.Substring("refs/heads/".Length);
                return v ?? "main";
            }
        }
        catch { }
        return "main";
    }

    /// <summary>
    /// Gets file content at the specified path and branch. Returns null when not found.
    /// </summary>
    public string? GetFileContent(string path, string branch)
    {
        try
        {
            using var c = CreateClient();
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{_repository}/items?path=/{Uri.EscapeDataString(path.TrimStart('/'))}&version={Uri.EscapeDataString(branch ?? "main")}&includeContent=true&api-version=7.1-preview.1";
            var resp = c.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return null;
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("content", out var ct)) return ct.GetString();
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
            var url = $"https://dev.azure.com/{_organization}/{_project}/_apis/git/repositories/{_repository}/items?scopePath=/{Uri.EscapeDataString(path.Trim('/'))}&recursionLevel=OneLevel&includeContentMetadata=true&version={Uri.EscapeDataString(branch ?? "main")}&api-version=7.1-preview.1";
            var resp = c.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return result;
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var isFolder = item.TryGetProperty("isFolder", out var f) && f.GetBoolean();
                    if (isFolder) continue;
                    if (!item.TryGetProperty("path", out var pEl)) continue;
                    var name = pEl.GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    var nn = name!;
                    var idx = nn.LastIndexOf('/');
                    var n = idx >= 0 ? nn.Substring(idx + 1) : nn;
                    result.Add((n, nn.TrimStart('/')));
                }
            }
        }
        catch { }
        return result;
    }
}
