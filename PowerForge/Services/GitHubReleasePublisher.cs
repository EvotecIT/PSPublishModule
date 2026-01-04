using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PowerForge;

/// <summary>
/// Minimal GitHub Releases client for creating releases and uploading assets.
/// </summary>
public sealed class GitHubReleasePublisher
{
    private readonly ILogger _logger;
    private static readonly HttpClient SharedClient = CreateSharedClient();

    /// <summary>
    /// Creates a new publisher using the provided logger.
    /// </summary>
    public GitHubReleasePublisher(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Creates a GitHub release and uploads assets.
    /// </summary>
    public (string HtmlUrl, string UploadUrl) PublishRelease(
        string owner,
        string repo,
        string token,
        string tagName,
        string releaseName,
        string? releaseNotes,
        string? commitish,
        bool generateReleaseNotes,
        bool isDraft,
        bool isPreRelease,
        IReadOnlyList<string> assetFilePaths)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(owner));
        if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("Repo is required.", nameof(repo));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.", nameof(token));
        if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("TagName is required.", nameof(tagName));
        if (string.IsNullOrWhiteSpace(releaseName)) releaseName = tagName;
        if (generateReleaseNotes && !string.IsNullOrWhiteSpace(releaseNotes))
            throw new ArgumentException("ReleaseNotes cannot be provided when GenerateReleaseNotes is enabled.", nameof(releaseNotes));

        var created = CreateRelease(owner, repo, token, tagName, releaseName, releaseNotes, commitish, generateReleaseNotes, isDraft, isPreRelease);
        var uploadUrl = RemoveUploadUrlTemplate(created.UploadUrl);

        var assets = (assetFilePaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim().Trim('"')))
            .ToArray();
        foreach (var a in assets)
            if (!File.Exists(a)) throw new FileNotFoundException($"GitHub asset not found: {a}");

        if (assets.Length > 0)
            UploadAssets(uploadUrl, assets, token);

        return (created.HtmlUrl, created.UploadUrl);
    }

    private (string HtmlUrl, string UploadUrl) CreateRelease(
        string owner,
        string repo,
        string token,
        string tagName,
        string releaseName,
        string? releaseNotes,
        string? commitish,
        bool generateReleaseNotes,
        bool isDraft,
        bool isPreRelease)
    {
        var uri = new Uri($"https://api.github.com/repos/{owner}/{repo}/releases");

        var normalizedCommitish = string.IsNullOrWhiteSpace(commitish) ? null : commitish!.Trim();
        var normalizedReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes;
        if (generateReleaseNotes) normalizedReleaseNotes = null;
        var body = new CreateReleaseRequest
        {
            TagName = tagName,
            TargetCommitish = normalizedCommitish,
            Name = releaseName,
            Body = normalizedReleaseNotes,
            GenerateReleaseNotes = generateReleaseNotes,
            Draft = isDraft,
            Prerelease = isPreRelease
        };

        var json = Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/vnd.github+json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = SharedClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub release creation failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        var html = parsed.HtmlUrl ?? string.Empty;
        var upload = parsed.UploadUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(upload))
            throw new InvalidOperationException("GitHub release creation succeeded but upload_url was empty.");

        return (html, upload);
    }

    private void UploadAssets(string uploadUrl, string[] assets, string token)
    {
        foreach (var assetPath in assets)
        {
            var fileName = Path.GetFileName(assetPath) ?? assetPath;
            var target = new Uri(uploadUrl + "?name=" + Uri.EscapeDataString(fileName));

            _logger.Info($"Uploading GitHub release asset: {fileName}");

            using var fs = File.OpenRead(assetPath);
            using var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var req = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = SharedClient.SendAsync(req).ConfigureAwait(false).GetAwaiter().GetResult();
            var respText = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub asset upload failed for '{fileName}' ({(int)resp.StatusCode} {resp.ReasonPhrase}). {TrimForMessage(respText)}");
        }
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
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge", "1.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static string RemoveUploadUrlTemplate(string uploadUrl)
    {
        if (string.IsNullOrWhiteSpace(uploadUrl)) return uploadUrl;
        var idx = uploadUrl.IndexOf('{');
        return idx >= 0 ? uploadUrl.Substring(0, idx) : uploadUrl;
    }

    private static string Serialize<T>(T value)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, value);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static T Deserialize<T>(string json)
    {
        var serializer = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty));
        return (T)serializer.ReadObject(ms)!;
    }

    private static string TrimForMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text!.Trim();
        return t.Length > 4000 ? t.Substring(0, 4000) + "..." : t;
    }

    [DataContract]
    private sealed class CreateReleaseRequest
    {
        [DataMember(Name = "tag_name", EmitDefaultValue = true)]
        public string TagName { get; set; } = string.Empty;

        [DataMember(Name = "target_commitish", EmitDefaultValue = false)]
        public string? TargetCommitish { get; set; }

        [DataMember(Name = "name", EmitDefaultValue = true)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "body", EmitDefaultValue = false)]
        public string? Body { get; set; }

        [DataMember(Name = "generate_release_notes", EmitDefaultValue = false)]
        public bool GenerateReleaseNotes { get; set; }

        [DataMember(Name = "draft", EmitDefaultValue = true)]
        public bool Draft { get; set; }

        [DataMember(Name = "prerelease", EmitDefaultValue = true)]
        public bool Prerelease { get; set; }
    }

    [DataContract]
    private sealed class CreateReleaseResponse
    {
        [DataMember(Name = "html_url")]
        public string? HtmlUrl { get; set; }

        [DataMember(Name = "upload_url")]
        public string? UploadUrl { get; set; }
    }
}
