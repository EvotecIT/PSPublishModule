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
    public GitHubReleasePublishResult PublishRelease(GitHubReleasePublishRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var owner = request.Owner;
        var repo = request.Repository;
        var token = request.Token;
        var tagName = request.TagName;
        var releaseName = request.ReleaseName;
        var releaseNotes = request.ReleaseNotes;
        var commitish = request.Commitish;
        var generateReleaseNotes = request.GenerateReleaseNotes;
        var isDraft = request.IsDraft;
        var isPreRelease = request.IsPreRelease;
        var reuseExistingReleaseOnConflict = request.ReuseExistingReleaseOnConflict;

        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("TagName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(releaseName)) releaseName = tagName;
        if (generateReleaseNotes && !string.IsNullOrWhiteSpace(releaseNotes))
            throw new ArgumentException("ReleaseNotes cannot be provided when GenerateReleaseNotes is enabled.", nameof(request));

        var assets = (request.AssetFilePaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim().Trim('"')))
            .ToArray();

        foreach (var assetPath in assets)
        {
            if (!File.Exists(assetPath))
                throw new FileNotFoundException($"GitHub asset not found: {assetPath}");
        }

        var release = CreateRelease(
            owner,
            repo,
            token,
            tagName,
            releaseName!,
            releaseNotes,
            commitish,
            generateReleaseNotes,
            isDraft,
            isPreRelease,
            reuseExistingReleaseOnConflict);

        var result = new GitHubReleasePublishResult
        {
            Succeeded = true,
            ReleaseCreationSucceeded = true,
            AllAssetUploadsSucceeded = assets.Length == 0 ? null : true,
            HtmlUrl = release.HtmlUrl,
            UploadUrl = release.UploadUrl,
            ReusedExistingRelease = release.ReusedExistingRelease
        };

        if (assets.Length > 0)
        {
            var uploadUrl = RemoveUploadUrlTemplate(release.UploadUrl);
            result.SkippedExistingAssets.AddRange(UploadAssets(uploadUrl, assets, token));
        }

        return result;
    }

    /// <summary>
    /// Creates a GitHub release and uploads assets using the legacy parameter surface.
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
        var result = PublishRelease(new GitHubReleasePublishRequest
        {
            Owner = owner,
            Repository = repo,
            Token = token,
            TagName = tagName,
            ReleaseName = releaseName,
            ReleaseNotes = releaseNotes,
            Commitish = commitish,
            GenerateReleaseNotes = generateReleaseNotes,
            IsDraft = isDraft,
            IsPreRelease = isPreRelease,
            ReuseExistingReleaseOnConflict = true,
            AssetFilePaths = assetFilePaths ?? Array.Empty<string>()
        });

        return (result.HtmlUrl ?? string.Empty, result.UploadUrl ?? string.Empty);
    }

    private GitHubReleaseApiResponse CreateRelease(
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
        bool reuseExistingReleaseOnConflict)
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
        {
            // Idempotency: reruns frequently hit "tag already exists" (release already created).
            if (reuseExistingReleaseOnConflict &&
                (int)response.StatusCode == 422 &&
                IsAlreadyExistsValidationError(responseText, fieldName: "tag_name"))
            {
                _logger.Info($"GitHub release for tag '{tagName}' already exists; reusing existing release.");
                return GetReleaseByTag(owner, repo, token, tagName, reusedExistingRelease: true);
            }

            throw new InvalidOperationException($"GitHub release creation failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        var html = parsed.HtmlUrl ?? string.Empty;
        var upload = parsed.UploadUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(upload))
            throw new InvalidOperationException("GitHub release creation succeeded but upload_url was empty.");

        return new GitHubReleaseApiResponse(html, upload, reusedExistingRelease: false);
    }

    private GitHubReleaseApiResponse GetReleaseByTag(string owner, string repo, string token, string tagName, bool reusedExistingRelease)
    {
        var uri = new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tagName)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = SharedClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub get-release-by-tag failed for '{tagName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        var html = parsed.HtmlUrl ?? string.Empty;
        var upload = parsed.UploadUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(upload))
            throw new InvalidOperationException($"GitHub get-release-by-tag succeeded for '{tagName}' but upload_url was empty.");

        return new GitHubReleaseApiResponse(html, upload, reusedExistingRelease);
    }

    private IReadOnlyList<string> UploadAssets(string uploadUrl, string[] assets, string token)
    {
        var skippedAssets = new List<string>();

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
            {
                // Idempotency: reruns can hit "asset already exists". Prefer to continue rather than failing the whole build.
                if ((int)resp.StatusCode == 422 && IsAlreadyExistsValidationError(respText, fieldName: "name"))
                {
                    _logger.Info($"GitHub release asset '{fileName}' already exists; skipping upload.");
                    skippedAssets.Add(fileName);
                    continue;
                }

                throw new InvalidOperationException($"GitHub asset upload failed for '{fileName}' ({(int)resp.StatusCode} {resp.ReasonPhrase}). {TrimForMessage(respText)}");
            }
        }

        return skippedAssets;
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

    private static bool IsAlreadyExistsValidationError(string? responseJson, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return false;
        try
        {
            var parsed = Deserialize<GitHubValidationErrorResponse>(responseJson!);
            var errors = parsed.Errors ?? Array.Empty<GitHubValidationError>();
            return errors.Any(e =>
                string.Equals(e.Code, "already_exists", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Field, fieldName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Best-effort parse only. If GitHub changes the error schema (or returns non-JSON), just treat it as non-matching.
            return false;
        }
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

    [DataContract]
    private sealed class GitHubValidationErrorResponse
    {
        [DataMember(Name = "message", EmitDefaultValue = false)]
        public string? Message { get; set; }

        [DataMember(Name = "errors", EmitDefaultValue = false)]
        public GitHubValidationError[]? Errors { get; set; }
    }

    [DataContract]
    private sealed class GitHubValidationError
    {
        [DataMember(Name = "resource", EmitDefaultValue = false)]
        public string? Resource { get; set; }

        [DataMember(Name = "code", EmitDefaultValue = false)]
        public string? Code { get; set; }

        [DataMember(Name = "field", EmitDefaultValue = false)]
        public string? Field { get; set; }

        [DataMember(Name = "message", EmitDefaultValue = false)]
        public string? Message { get; set; }
    }

    private sealed class GitHubReleaseApiResponse
    {
        public GitHubReleaseApiResponse(string htmlUrl, string uploadUrl, bool reusedExistingRelease)
        {
            HtmlUrl = htmlUrl;
            UploadUrl = uploadUrl;
            ReusedExistingRelease = reusedExistingRelease;
        }

        public string HtmlUrl { get; }
        public string UploadUrl { get; }
        public bool ReusedExistingRelease { get; }
    }
}
