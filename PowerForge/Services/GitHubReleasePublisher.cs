using System;
using System.Collections.Generic;
using System.Diagnostics;
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
public sealed partial class GitHubReleasePublisher
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
        var apiBaseUrl = NormalizeApiBaseUrl(request.ApiBaseUrl);
        var tagName = request.TagName;
        var releaseName = request.ReleaseName;
        var releaseNotes = request.ReleaseNotes;
        var commitish = request.Commitish;
        var generateReleaseNotes = request.GenerateReleaseNotes;
        var isDraft = request.IsDraft;
        var isPreRelease = request.IsPreRelease;
        var reuseExistingReleaseOnConflict = request.ReuseExistingReleaseOnConflict;
        var requireExpectedExistingRelease = request.RequireExpectedExistingRelease;
        var expectedExistingReleaseId = request.ExpectedExistingReleaseId;
        var expectedReleaseBodyMarker = request.ExpectedReleaseBodyMarker;
        var expectedTagCommitSha = request.ExpectedTagCommitSha;
        var replaceExistingAssets = request.ReplaceExistingAssets;

        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(tagName)) throw new ArgumentException("TagName is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(releaseName)) releaseName = tagName;
        var assets = (request.AssetFilePaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p.Trim().Trim('"')))
            .ToArray();

        foreach (var assetPath in assets)
        {
            if (!File.Exists(assetPath))
                throw new FileNotFoundException($"GitHub asset not found: {assetPath}");
        }

        var releaseWatch = Stopwatch.StartNew();
        var release = CreateRelease(
            owner,
            repo,
            token,
            apiBaseUrl,
            tagName,
            releaseName!,
            releaseNotes,
            commitish,
            generateReleaseNotes,
            isDraft,
            isPreRelease,
            reuseExistingReleaseOnConflict,
            requireExpectedExistingRelease,
            expectedExistingReleaseId);
        releaseWatch.Stop();
        _logger.Success($"GitHub release ready in {DotNetRepositoryReleaseService.FormatDuration(releaseWatch.Elapsed)}: {release.HtmlUrl}");

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
            var assetUploadWatch = Stopwatch.StartNew();
            result.SkippedExistingAssets.AddRange(UploadAssets(
                uploadUrl,
                assets,
                token,
                owner,
                repo,
                apiBaseUrl,
                release.Id,
                tagName,
                expectedReleaseBodyMarker,
                expectedTagCommitSha,
                replaceExistingAssets,
                result.ReplacedExistingAssets));
            assetUploadWatch.Stop();
            var uploaded = assets.Length - result.SkippedExistingAssets.Count;
            _logger.Success($"GitHub release asset upload phase completed in {DotNetRepositoryReleaseService.FormatDuration(assetUploadWatch.Elapsed)} ({uploaded} uploaded, {result.SkippedExistingAssets.Count} skipped).");
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
            ApiBaseUrl = "https://api.github.com",
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
        string apiBaseUrl,
        string tagName,
        string releaseName,
        string? releaseNotes,
        string? commitish,
        bool generateReleaseNotes,
        bool isDraft,
        bool isPreRelease,
        bool reuseExistingReleaseOnConflict,
        bool requireExpectedExistingRelease,
        long? expectedExistingReleaseId)
    {
        var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases");

        var normalizedCommitish = string.IsNullOrWhiteSpace(commitish) ? null : commitish!.Trim();
        var normalizedReleaseNotes =
            string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes;
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
                var existing = GetReleaseByTag(owner, repo, token, apiBaseUrl, tagName, reusedExistingRelease: true);
                ValidateExpectedExistingRelease(
                    tagName,
                    requireExpectedExistingRelease,
                    expectedExistingReleaseId,
                    existing.Id);

                _logger.Info(requireExpectedExistingRelease
                    ? $"GitHub release for tag '{tagName}' already exists; reusing preflight-verified release {existing.Id}."
                    : $"GitHub release for tag '{tagName}' already exists; reusing existing release {existing.Id}.");
                return existing;
            }

            throw new InvalidOperationException($"GitHub release creation failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        var html = parsed.HtmlUrl ?? string.Empty;
        var upload = parsed.UploadUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(upload))
            throw new InvalidOperationException("GitHub release creation succeeded but upload_url was empty.");

        return new GitHubReleaseApiResponse(parsed.Id, html, upload, reusedExistingRelease: false, parsed.Body);
    }

    private GitHubReleaseApiResponse GetReleaseByTag(string owner, string repo, string token, string apiBaseUrl, string tagName, bool reusedExistingRelease)
    {
        var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tagName)}");
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

        return new GitHubReleaseApiResponse(parsed.Id, html, upload, reusedExistingRelease, parsed.Body);
    }

    private IReadOnlyList<string> UploadAssets(
        string uploadUrl,
        string[] assets,
        string token,
        string owner,
        string repo,
        string apiBaseUrl,
        long releaseId,
        string tagName,
        string? expectedReleaseBodyMarker,
        string? expectedTagCommitSha,
        bool replaceExistingAssets,
        List<string> replacedExistingAssets)
    {
        ValidateReleaseBeforeAssetMutation(
            owner,
            repo,
            token,
            apiBaseUrl,
            releaseId,
            tagName,
            expectedReleaseBodyMarker,
            expectedTagCommitSha);

        var skippedAssets = new List<string>();
        var replaceableAssetNames = replaceExistingAssets
            ? CreateReplaceableAssetNameSet(ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId))
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assetPath in assets)
        {
            var fileName = Path.GetFileName(assetPath) ?? assetPath;

            if (replaceExistingAssets &&
                TryReserveExistingAssetNameForReplacement(replaceableAssetNames, fileName) &&
                DeleteExistingAssetByName(owner, repo, token, apiBaseUrl, releaseId, fileName))
            {
                replacedExistingAssets.Add(fileName);
            }

            var assetSize = new FileInfo(assetPath).Length;
            _logger.Info($"Uploading GitHub release asset: {fileName} ({DotNetRepositoryReleaseService.FormatBytes(assetSize)})");

            var assetWatch = Stopwatch.StartNew();
            var resp = UploadAsset(uploadUrl, assetPath, fileName, token);
            var respText = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode && replaceExistingAssets &&
                (int)resp.StatusCode == 422 &&
                IsAlreadyExistsValidationError(respText, fieldName: "name") &&
                TryReserveExistingAssetNameForReplacement(replaceableAssetNames, fileName) &&
                DeleteExistingAssetByName(owner, repo, token, apiBaseUrl, releaseId, fileName))
            {
                replacedExistingAssets.Add(fileName);
                resp.Dispose();
                resp = UploadAsset(uploadUrl, assetPath, fileName, token);
                respText = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                {
                    // Idempotency: reruns can hit "asset already exists". Prefer to continue rather than failing the whole build.
                    if ((int)resp.StatusCode == 422 && IsAlreadyExistsValidationError(respText, fieldName: "name"))
                    {
                        assetWatch.Stop();
                        _logger.Info($"GitHub release asset '{fileName}' already exists; skipping upload after {DotNetRepositoryReleaseService.FormatDuration(assetWatch.Elapsed)}.");
                        skippedAssets.Add(fileName);
                        continue;
                    }

                    throw new InvalidOperationException($"GitHub asset upload failed for '{fileName}' ({(int)resp.StatusCode} {resp.ReasonPhrase}). {TrimForMessage(respText)}");
                }
            }

            assetWatch.Stop();
            _logger.Success($"Uploaded GitHub release asset: {fileName} in {DotNetRepositoryReleaseService.FormatDuration(assetWatch.Elapsed)} ({DotNetRepositoryReleaseService.FormatBytes(assetSize)}).");
        }

        return skippedAssets;
    }

    internal static bool TryReserveExistingAssetNameForReplacement(ISet<string> replaceableAssetNames, string fileName)
    {
        if (replaceableAssetNames is null) throw new ArgumentNullException(nameof(replaceableAssetNames));
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        return replaceableAssetNames.Remove(fileName);
    }

    internal static void ValidateExpectedExistingRelease(
        string tagName,
        bool requireExpectedExistingRelease,
        long? expectedExistingReleaseId,
        long actualExistingReleaseId)
    {
        if (!requireExpectedExistingRelease) return;
        if (expectedExistingReleaseId.HasValue && expectedExistingReleaseId.Value == actualExistingReleaseId) return;

        throw new InvalidOperationException(
            $"GitHub release for tag '{tagName}' already exists, but release id {actualExistingReleaseId} was not preflight-verified for reuse.");
    }

    private static HashSet<string> CreateReplaceableAssetNameSet(IEnumerable<GitHubReleaseAssetResponse> existingAssets)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in existingAssets)
        {
            if (!string.IsNullOrWhiteSpace(asset.Name))
                names.Add(asset.Name!);
        }

        return names;
    }

    private static HttpResponseMessage UploadAsset(string uploadUrl, string assetPath, string fileName, string token)
    {
        var target = new Uri(uploadUrl + "?name=" + Uri.EscapeDataString(fileName));

        using var fs = File.OpenRead(assetPath);
        using var content = new StreamContent(fs);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var req = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return SharedClient.SendAsync(req).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    private bool DeleteExistingAssetByName(string owner, string repo, string token, string apiBaseUrl, long releaseId, string fileName)
    {
        if (releaseId <= 0)
            throw new InvalidOperationException("GitHub release asset replacement requires the release id returned by GitHub.");

        var asset = ListReleaseAssets(owner, repo, token, apiBaseUrl, releaseId)
            .FirstOrDefault(existing => string.Equals(existing.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            return false;

        var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/assets/{asset.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = SharedClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        using (response)
        {
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"GitHub asset delete failed for '{fileName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        _logger.Info($"Deleted existing GitHub release asset before replacement: {fileName}");
        return true;
    }

    private static IReadOnlyList<GitHubReleaseAssetResponse> ListReleaseAssets(string owner, string repo, string token, string apiBaseUrl, long releaseId)
    {
        var assets = new List<GitHubReleaseAssetResponse>();
        for (var page = 1; ; page++)
        {
            var uri = BuildApiUri(apiBaseUrl, $"/repos/{owner}/{repo}/releases/{releaseId}/assets?per_page=100&page={page}");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = SharedClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"GitHub list-release-assets failed for release '{releaseId}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
            }

            var pageAssets = Deserialize<GitHubReleaseAssetResponse[]>(responseText) ?? Array.Empty<GitHubReleaseAssetResponse>();
            assets.AddRange(pageAssets);
            if (pageAssets.Length < 100)
                break;
        }

        return assets;
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

    private static string NormalizeApiBaseUrl(string? apiBaseUrl)
        => string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "https://api.github.com"
            : apiBaseUrl!.Trim().TrimEnd('/');

    internal static Uri BuildApiUri(string apiBaseUrl, string path)
        => new(NormalizeApiBaseUrl(apiBaseUrl) + path);

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

        [DataMember(Name = "id")]
        public long Id { get; set; }

        [DataMember(Name = "upload_url")]
        public string? UploadUrl { get; set; }

        [DataMember(Name = "body")]
        public string? Body { get; set; }
    }

    [DataContract]
    private sealed class GitHubReleaseAssetResponse
    {
        [DataMember(Name = "id")]
        public long Id { get; set; }

        [DataMember(Name = "name")]
        public string? Name { get; set; }
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
        public GitHubReleaseApiResponse(long id, string htmlUrl, string uploadUrl, bool reusedExistingRelease, string? body = null)
        {
            Id = id;
            HtmlUrl = htmlUrl;
            UploadUrl = uploadUrl;
            ReusedExistingRelease = reusedExistingRelease;
            Body = body;
        }

        public long Id { get; }
        public string HtmlUrl { get; }
        public string UploadUrl { get; }
        public bool ReusedExistingRelease { get; }
        public string? Body { get; }
    }
}
