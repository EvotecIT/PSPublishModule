using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PSPublishModule;

/// <summary>
/// Creates a new release for the given GitHub repository and optionally uploads assets.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet uses the GitHub REST API to create a release and upload assets. It is a lower-level building block used by
/// higher-level helpers (such as <c>Publish-GitHubReleaseAsset</c>) and can also be used directly in CI pipelines.
/// </para>
/// <para>
/// Provide the token via an environment variable to avoid leaking secrets into logs or history.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a release and upload a ZIP asset</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3' -ReleaseNotes 'Bug fixes' -AssetFilePaths 'C:\Artifacts\MyProject.zip'</code>
/// <para>Creates the release and uploads the specified asset file.</para>
/// </example>
/// <example>
/// <summary>Create a prerelease as a draft</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3-preview.1' -IsDraft $true -IsPreRelease $true</code>
/// <para>Creates a draft prerelease that can be reviewed before publishing.</para>
/// </example>
[Cmdlet(VerbsCommunications.Send, "GitHubRelease")]
public sealed class SendGitHubReleaseCommand : PSCmdlet
{
    /// <summary>GitHub username owning the repository.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubUsername { get; set; } = string.Empty;

    /// <summary>GitHub repository name.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubRepositoryName { get; set; } = string.Empty;

    /// <summary>GitHub personal access token used for authentication.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubAccessToken { get; set; } = string.Empty;

    /// <summary>The tag name used for the release.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string TagName { get; set; } = string.Empty;

    /// <summary>The name of the release. If omitted, TagName is used.</summary>
    [Parameter]
    public string? ReleaseName { get; set; }

    /// <summary>The text describing the contents of the release.</summary>
    [Parameter]
    public string? ReleaseNotes { get; set; }

    /// <summary>When set, asks GitHub to generate release notes automatically (cannot be used with ReleaseNotes).</summary>
    [Parameter]
    public SwitchParameter GenerateReleaseNotes { get; set; }

    /// <summary>The full paths of the files to include as release assets.</summary>
    [Parameter]
    public string[]? AssetFilePaths { get; set; }

    /// <summary>Commitish value that determines where the Git tag is created from.</summary>
    [Parameter]
    public string? Commitish { get; set; }

    /// <summary>True to create a draft (unpublished) release.</summary>
    [Parameter]
    public bool IsDraft { get; set; }

    /// <summary>True to identify the release as a prerelease.</summary>
    [Parameter]
    public bool IsPreRelease { get; set; }

    /// <summary>
    /// When true (default), a 422 tag conflict reuses the existing release.
    /// When false, the cmdlet fails on existing tags.
    /// </summary>
    [Parameter]
    public bool ReuseExistingReleaseOnConflict { get; set; } = true;

    /// <summary>Creates the release and uploads assets.</summary>
    protected override void ProcessRecord()
    {
        var result = new GitHubReleaseResult
        {
            Succeeded = false,
            ReleaseCreationSucceeded = false,
            AllAssetUploadsSucceeded = false,
            ReleaseUrl = null,
            ErrorMessage = null
        };

        var assets = (AssetFilePaths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var thereAreNoAssetsToInclude = assets.Length == 0;
        if (thereAreNoAssetsToInclude)
        {
            result.AllAssetUploadsSucceeded = null;
        }

        if (string.IsNullOrWhiteSpace(ReleaseName))
        {
            ReleaseName = TagName;
        }

        ValidateAssetFilePathsOrThrow(assets);

        if (GenerateReleaseNotes.IsPresent && !string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            throw new PSArgumentException($"{nameof(ReleaseNotes)} cannot be used when {nameof(GenerateReleaseNotes)} is set.");
        }

        try
        {
            var release = CreateRelease(GitHubUsername, GitHubRepositoryName, GitHubAccessToken,
                TagName, ReleaseName!, ReleaseNotes, Commitish, GenerateReleaseNotes.IsPresent, IsDraft, IsPreRelease, ReuseExistingReleaseOnConflict);

            result.ReleaseCreationSucceeded = true;
            result.ReleaseUrl = release.HtmlUrl;

            if (thereAreNoAssetsToInclude)
            {
                result.Succeeded = true;
                WriteObject(result);
                return;
            }

            var uploadUrl = RemoveUploadUrlTemplate(release.UploadUrl);
            UploadAssets(uploadUrl, assets, GitHubAccessToken);

            result.AllAssetUploadsSucceeded = true;
            result.Succeeded = true;
            WriteObject(result);
        }
        catch (Exception ex)
        {
            if (!result.ReleaseCreationSucceeded)
                result.ReleaseCreationSucceeded = false;

            result.Succeeded = false;
            result.AllAssetUploadsSucceeded = thereAreNoAssetsToInclude ? null : false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
        }
    }

    private static void ValidateAssetFilePathsOrThrow(string[] assets)
    {
        foreach (var filePath in assets)
        {
            var missing = string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath);
            if (missing)
            {
                throw new InvalidOperationException($"There is no file at the specified path, '{filePath}'.");
            }
        }
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
        using var client = CreateHttpClient(token);

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

        var response = client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            // Idempotency: reruns frequently hit "tag already exists" (release already created).
            if (reuseExistingReleaseOnConflict &&
                (int)response.StatusCode == 422 &&
                IsAlreadyExistsValidationError(responseText, fieldName: "tag_name"))
            {
                WriteWarning($"GitHub release for tag '{tagName}' already exists; reusing existing release and continuing.");
                return GetReleaseByTag(owner, repo, token, tagName);
            }

            throw new InvalidOperationException($"GitHub release creation failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        return new GitHubReleaseApiResponse(parsed.HtmlUrl ?? string.Empty, parsed.UploadUrl ?? string.Empty);
    }

    private GitHubReleaseApiResponse GetReleaseByTag(string owner, string repo, string token, string tagName)
    {
        using var client = CreateHttpClient(token);
        var uri = new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tagName)}");

        var response = client.GetAsync(uri).ConfigureAwait(false).GetAwaiter().GetResult();
        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub get-release-by-tag failed for '{tagName}' ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");

        var parsed = Deserialize<CreateReleaseResponse>(responseText);
        return new GitHubReleaseApiResponse(parsed.HtmlUrl ?? string.Empty, parsed.UploadUrl ?? string.Empty);
    }

    private void UploadAssets(string uploadUrl, string[] assets, string token)
    {
        using var client = CreateHttpClient(token);

        foreach (var assetPath in assets)
        {
            var fileName = System.IO.Path.GetFileName(assetPath) ?? assetPath;
            var target = new Uri(uploadUrl + "?name=" + Uri.EscapeDataString(fileName));

            using var fs = System.IO.File.OpenRead(assetPath);
            using var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var req = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
            var resp = client.SendAsync(req).ConfigureAwait(false).GetAwaiter().GetResult();
            var respText = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                // Idempotency: reruns can hit "asset already exists". Prefer to continue rather than failing the whole build.
                if ((int)resp.StatusCode == 422 && IsAlreadyExistsValidationError(respText, fieldName: "name"))
                {
                    WriteWarning($"GitHub release asset '{fileName}' already exists; skipping upload.");
                    continue;
                }

                throw new InvalidOperationException($"GitHub asset upload failed for '{fileName}' ({(int)resp.StatusCode} {resp.ReasonPhrase}). {TrimForMessage(respText)}");
            }
        }
    }

    private static HttpClient CreateHttpClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PSPublishModule", "2.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T)serializer.ReadObject(ms)!;
    }

    private static string TrimForMessage(string? text)
    {
        if (text is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
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
        public string HtmlUrl { get; }
        public string UploadUrl { get; }

        public GitHubReleaseApiResponse(string htmlUrl, string uploadUrl)
        {
            HtmlUrl = htmlUrl;
            UploadUrl = uploadUrl;
        }
    }

    /// <summary>
    /// Result returned by <c>Send-GitHubRelease</c>.
    /// </summary>
    public sealed class GitHubReleaseResult
    {
        /// <summary>True if the release was created successfully and all assets were uploaded.</summary>
        public bool Succeeded { get; set; }

        /// <summary>True if the release was created successfully (does not include asset uploads).</summary>
        public bool ReleaseCreationSucceeded { get; set; }

        /// <summary>True if all assets were uploaded; false if any failed; null if no assets were provided.</summary>
        public bool? AllAssetUploadsSucceeded { get; set; }

        /// <summary>The URL of the created release.</summary>
        public string? ReleaseUrl { get; set; }

        /// <summary>Error message describing what went wrong when <see cref="Succeeded"/> is false.</summary>
        public string? ErrorMessage { get; set; }
    }
}
