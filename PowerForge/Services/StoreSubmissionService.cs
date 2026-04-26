using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge;

internal sealed class StoreSubmissionService : IDisposable
{
    private static readonly string[] UploadPackagePatterns = { "*.msixupload", "*.appxupload" };
    private static readonly string[] StorePackagePatterns = { "*.msixbundle", "*.appxbundle", "*.msix", "*.appx" };
    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public StoreSubmissionService(ILogger? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger ?? new NullLogger();
        _httpClient = httpClient ?? SharedHttpClient;
        _ownsHttpClient = false;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    public StoreSubmissionTargetSummary[] ListTargets(StoreSubmissionSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        return (spec.Targets ?? Array.Empty<StoreSubmissionTarget>())
            .Where(target => target is not null && !string.IsNullOrWhiteSpace(target.Name))
            .Select(target => new StoreSubmissionTargetSummary
            {
                Name = target.Name,
                Description = target.Description,
                Provider = target.Provider,
                ApplicationId = target.ApplicationId
            })
            .OrderBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string[] Validate(StoreSubmissionSpec spec, string configPath, StoreSubmissionRequest? request = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var errors = new List<string>();

        try
        {
            ValidateAuthentication(spec.Authentication);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        try
        {
            _ = Plan(spec, configPath, request);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public StoreSubmissionPlan Plan(StoreSubmissionSpec spec, string configPath, StoreSubmissionRequest? request = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var fullConfigPath = Path.GetFullPath(FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath)).Trim().Trim('"'));
        var baseDirectory = Path.GetDirectoryName(fullConfigPath) ?? Directory.GetCurrentDirectory();
        var target = SelectTarget(spec, request?.TargetName);
        var provider = target.Provider;
        if (provider == StoreSubmissionProviderKind.DesktopInstaller)
            _ = ResolveSellerId(spec.Authentication);

        var packageFiles = provider == StoreSubmissionProviderKind.PackagedApp
            ? ResolvePackagedAppFiles(target, baseDirectory, request)
            : Array.Empty<string>();
        var desktopPackages = provider == StoreSubmissionProviderKind.DesktopInstaller
            ? ResolveDesktopPackages(target)
            : Array.Empty<StoreSubmissionDesktopPackage>();

        var submissionId = string.IsNullOrWhiteSpace(request?.SubmissionId)
            ? NormalizeNullable(target.SubmissionId)
            : NormalizeNullable(request!.SubmissionId);
        var commit = request?.Commit ?? target.Commit;
        var waitForCommit = request?.WaitForCommit ?? target.WaitForCommit;

        return new StoreSubmissionPlan
        {
            ConfigPath = fullConfigPath,
            TargetName = target.Name,
            Provider = provider,
            ApplicationId = FrameworkCompatibility.NotNullOrWhiteSpace(target.ApplicationId, nameof(target.ApplicationId)).Trim(),
            SubmissionId = submissionId,
            PackageFiles = packageFiles,
            ZipPath = provider == StoreSubmissionProviderKind.PackagedApp ? ResolveZipPath(target, baseDirectory) : string.Empty,
            Commit = commit,
            WaitForCommit = commit && waitForCommit,
            PollIntervalSeconds = Math.Max(1, target.PollIntervalSeconds),
            PollTimeoutMinutes = Math.Max(1, target.PollTimeoutMinutes),
            MinimumDirectXVersion = string.IsNullOrWhiteSpace(target.MinimumDirectXVersion) ? "None" : target.MinimumDirectXVersion.Trim(),
            MinimumSystemRam = string.IsNullOrWhiteSpace(target.MinimumSystemRam) ? "None" : target.MinimumSystemRam.Trim(),
            DesktopPackages = desktopPackages
        };
    }

    public async Task<StoreSubmissionResult> RunAsync(StoreSubmissionSpec spec, string configPath, StoreSubmissionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var plan = Plan(spec, configPath, request);
        var result = new StoreSubmissionResult
        {
            Plan = plan,
            PackageFiles = plan.PackageFiles,
            PackageZipPath = plan.ZipPath
        };

        try
        {
            if (plan.Provider == StoreSubmissionProviderKind.DesktopInstaller)
                return await RunDesktopInstallerAsync(spec.Authentication, plan, cancellationToken).ConfigureAwait(false);

            var token = await ResolveAccessTokenAsync(spec.Authentication, plan.Provider, cancellationToken).ConfigureAwait(false);
            JsonObject submission;
            string submissionId;
            string uploadUrl;

            if (string.IsNullOrWhiteSpace(plan.SubmissionId))
            {
                _logger.Info($"Creating Store draft submission for target '{plan.TargetName}'.");
                submission = await CreateSubmissionAsync(plan.ApplicationId, token, cancellationToken).ConfigureAwait(false);
                submissionId = GetRequiredString(submission, "id", "submission id");
                uploadUrl = GetRequiredString(submission, "fileUploadUrl", "file upload URL");
                result.CreatedSubmission = true;
            }
            else
            {
                submissionId = plan.SubmissionId!;
                _logger.Info($"Using existing Store draft submission '{submissionId}' for target '{plan.TargetName}'.");
                submission = await GetSubmissionAsync(plan.ApplicationId, submissionId, token, cancellationToken).ConfigureAwait(false);
                uploadUrl = GetRequiredString(submission, "fileUploadUrl", "file upload URL");
            }

            result.SubmissionId = submissionId;

            ApplyPackageUpdate(submission, plan);
            var updatedSubmission = await UpdateSubmissionAsync(plan.ApplicationId, submissionId, submission, token, cancellationToken).ConfigureAwait(false);
            uploadUrl = GetOptionalString(updatedSubmission, "fileUploadUrl") ?? uploadUrl;

            CreateSubmissionArchive(plan.ZipPath, plan.PackageFiles);
            await UploadSubmissionArchiveAsync(uploadUrl, plan.ZipPath, cancellationToken).ConfigureAwait(false);
            result.UploadedPackageArchive = true;

            if (!plan.Commit)
            {
                result.Succeeded = true;
                result.FinalStatus = GetOptionalString(updatedSubmission, "status") ?? "PendingCommit";
                result.StatusDetails = ExtractStatusDetails(updatedSubmission);
                return result;
            }

            await CommitSubmissionAsync(plan.ApplicationId, submissionId, token, cancellationToken).ConfigureAwait(false);
            result.CommittedSubmission = true;

            if (!plan.WaitForCommit)
            {
                result.Succeeded = true;
                result.FinalStatus = "CommitStarted";
                return result;
            }

            var statusHistory = new List<StoreSubmissionStatusSnapshot>();
            var deadline = DateTimeOffset.UtcNow.AddMinutes(plan.PollTimeoutMinutes);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var statusResponse = await GetSubmissionStatusAsync(plan.ApplicationId, submissionId, token, cancellationToken).ConfigureAwait(false);
                var status = GetOptionalString(statusResponse, "status") ?? "Unknown";
                var details = ExtractStatusDetails(statusResponse);

                statusHistory.Add(new StoreSubmissionStatusSnapshot
                {
                    CheckedUtc = DateTimeOffset.UtcNow,
                    Status = status,
                    Details = details
                });

                if (string.Equals(status, "CommitFailed", StringComparison.OrdinalIgnoreCase))
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = status;
                    result.StatusDetails = details;
                    result.ErrorMessage = string.IsNullOrWhiteSpace(details)
                        ? $"Store submission '{submissionId}' failed to commit."
                        : $"Store submission '{submissionId}' failed to commit. {details}";
                    return result;
                }

                if (!IsTransientCommitStatus(status))
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = status;
                    result.StatusDetails = details;
                    result.Succeeded = true;
                    return result;
                }

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = status;
                    result.StatusDetails = details;
                    result.ErrorMessage =
                        $"Store submission '{submissionId}' did not leave transient commit status within {plan.PollTimeoutMinutes} minute(s). " +
                        $"Last status: {status}.";
                    return result;
                }

                await Task.Delay(TimeSpan.FromSeconds(plan.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static void ValidateAuthentication(StoreSubmissionAuthenticationOptions? authentication)
    {
        if (authentication is null)
            throw new InvalidOperationException("Store submission authentication settings are required.");

        ValidateAuthorityHost(authentication.AuthorityHost);

        var accessToken = ResolveCredential(authentication.AccessToken, authentication.AccessTokenEnvVar);
        _ = NormalizeAuthorityHost(authentication.AuthorityHost);
        if (!string.IsNullOrWhiteSpace(accessToken))
            return;

        var tenantId = NormalizeNullable(authentication.TenantId);
        var clientId = NormalizeNullable(authentication.ClientId);
        var clientSecret = ResolveCredential(authentication.ClientSecret, authentication.ClientSecretEnvVar);

        if (string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                "Store submission authentication requires either AccessToken/AccessTokenEnvVar, or TenantId + ClientId + ClientSecret/ClientSecretEnvVar.");
        }
    }

    private static StoreSubmissionTarget SelectTarget(StoreSubmissionSpec spec, string? targetName)
    {
        var targets = (spec.Targets ?? Array.Empty<StoreSubmissionTarget>())
            .Where(target => target is not null && !string.IsNullOrWhiteSpace(target.Name))
            .ToArray();

        if (targets.Length == 0)
            throw new InvalidOperationException("Store submission config does not define any targets.");

        if (string.IsNullOrWhiteSpace(targetName))
        {
            if (targets.Length == 1)
                return targets[0];

            throw new InvalidOperationException(
                $"Store submission config defines multiple targets ({string.Join(", ", targets.Select(target => target.Name))}). " +
                "Specify --target <name>.");
        }

        var selected = targets.FirstOrDefault(target => string.Equals(target.Name, targetName, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            throw new InvalidOperationException($"Store submission target '{targetName}' was not found.");

        return selected;
    }

    private static string[] ResolvePackagedAppFiles(StoreSubmissionTarget target, string baseDirectory, StoreSubmissionRequest? request)
    {
        IEnumerable<string> files;
        var requestPackagePaths = request?.PackagePaths ?? Array.Empty<string>();
        if (requestPackagePaths.Length > 0)
        {
            files = requestPackagePaths.Select(path => ResolveFilePath(baseDirectory, path));
        }
        else if ((target.PackagePaths ?? Array.Empty<string>()).Length > 0)
        {
            var targetPackagePaths = target.PackagePaths ?? Array.Empty<string>();
            files = targetPackagePaths.Select(path => ResolveFilePath(baseDirectory, path));
        }
        else
        {
            var sourceDirectoryRaw = NormalizeNullable(target.SourceDirectory);
            if (string.IsNullOrWhiteSpace(sourceDirectoryRaw))
                throw new InvalidOperationException($"Store submission target '{target.Name}' must define PackagePaths or SourceDirectory.");

            var sourceDirectory = ResolveDirectoryPath(baseDirectory, sourceDirectoryRaw!);
            if (!Directory.Exists(sourceDirectory))
                throw new DirectoryNotFoundException($"Store submission source directory not found: {sourceDirectory}");

            var searchOption = target.RecurseSourceDirectory ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var patterns = (target.PackagePatterns ?? Array.Empty<string>())
                .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
                .Select(pattern => pattern.Trim())
                .ToArray();

            if (patterns.Length > 0)
            {
                files = patterns.SelectMany(pattern => Directory.EnumerateFiles(sourceDirectory, pattern, searchOption));
            }
            else
            {
                var uploadFiles = UploadPackagePatterns
                    .SelectMany(pattern => Directory.EnumerateFiles(sourceDirectory, pattern, searchOption))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                files = uploadFiles.Length > 0
                    ? uploadFiles
                    : StorePackagePatterns.SelectMany(pattern => Directory.EnumerateFiles(sourceDirectory, pattern, searchOption));
            }
        }

        var resolved = files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resolved.Length == 0)
            throw new InvalidOperationException($"Store submission target '{target.Name}' did not resolve any package files.");

        var duplicateFileNames = resolved
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (duplicateFileNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"Store submission target '{target.Name}' resolves package files with duplicate names: {string.Join(", ", duplicateFileNames)}. " +
                "Partner Center package uploads require unique file names inside the submission ZIP.");
        }

        return resolved;
    }

    private static StoreSubmissionDesktopPackage[] ResolveDesktopPackages(StoreSubmissionTarget target)
    {
        var packages = (target.DesktopPackages ?? Array.Empty<StoreSubmissionDesktopPackage>())
            .Select(CloneDesktopPackage)
            .ToArray();

        if (packages.Length == 0)
            throw new InvalidOperationException($"Desktop Store submission target '{target.Name}' must define DesktopPackages.");

        foreach (var package in packages)
        {
            _ = ValidateHttpsUrl(package.PackageUrl, nameof(package.PackageUrl));
            if (package.Languages is null || package.Languages.Length == 0)
                throw new InvalidOperationException($"Desktop Store submission target '{target.Name}' must define Languages for each DesktopPackages entry.");
            if (package.Architectures is null || package.Architectures.Length == 0)
                throw new InvalidOperationException($"Desktop Store submission target '{target.Name}' must define Architectures for each DesktopPackages entry.");
            if (!package.IsSilentInstall && string.IsNullOrWhiteSpace(package.InstallerParameters))
            {
                throw new InvalidOperationException(
                    $"Desktop Store submission target '{target.Name}' must define InstallerParameters when IsSilentInstall is false.");
            }
        }

        return packages;
    }

    private static string ResolveZipPath(StoreSubmissionTarget target, string baseDirectory)
    {
        var configured = NormalizeNullable(target.ZipPath);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(baseDirectory, configured));

        var safeName = ToSafeFileName(target.Name, "store-submit");
        return Path.Combine(baseDirectory, "Artifacts", "StoreSubmit", safeName, $"{safeName}.zip");
    }

    private static string ResolveFilePath(string baseDirectory, string path)
    {
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Store submission package file not found: {fullPath}", fullPath);

        return fullPath;
    }

    private static string ResolveDirectoryPath(string baseDirectory, string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
    }

    private async Task<string> ResolveAccessTokenAsync(StoreSubmissionAuthenticationOptions authentication, StoreSubmissionProviderKind provider, CancellationToken cancellationToken)
    {
        ValidateAuthentication(authentication);

        var explicitToken = ResolveCredential(authentication.AccessToken, authentication.AccessTokenEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitToken))
            return explicitToken!;

        var tenantId = NormalizeNullable(authentication.TenantId)!;
        var clientId = NormalizeNullable(authentication.ClientId)!;
        var clientSecret = ResolveCredential(authentication.ClientSecret, authentication.ClientSecretEnvVar)!;
        var authorityHost = NormalizeAuthorityHost(authentication.AuthorityHost);
        var tokenUrl = provider == StoreSubmissionProviderKind.DesktopInstaller
            ? $"{authorityHost}/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token"
            : $"{authorityHost}/{Uri.EscapeDataString(tenantId)}/oauth2/token";
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        if (provider == StoreSubmissionProviderKind.DesktopInstaller)
            form["scope"] = string.IsNullOrWhiteSpace(authentication.Scope) ? "https://api.store.microsoft.com/.default" : authentication.Scope.Trim();
        else
            form["resource"] = string.IsNullOrWhiteSpace(authentication.Resource) ? "https://manage.devcenter.microsoft.com" : authentication.Resource.Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Store submission token request failed ({(int)response.StatusCode} {response.ReasonPhrase}). {TrimForMessage(responseText)}");
        }

        var node = ParseJsonObject(responseText, "token response");
        var accessToken = GetOptionalString(node, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Store submission token response did not include access_token.");

        return accessToken!;
    }

    private async Task<JsonObject> CreateSubmissionAsync(string applicationId, string accessToken, CancellationToken cancellationToken)
    {
        var endpoint = CreateAppSubmissionEndpoint(applicationId);
        using var request = CreateApiRequest(HttpMethod.Post, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("create Store submission", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "create submission response");
    }

    private async Task<JsonObject> GetSubmissionAsync(string applicationId, string submissionId, string accessToken, CancellationToken cancellationToken)
    {
        var endpoint = $"{CreateAppSubmissionEndpoint(applicationId)}/{Uri.EscapeDataString(submissionId)}";
        using var request = CreateApiRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("get Store submission", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "get submission response");
    }

    private async Task<JsonObject> UpdateSubmissionAsync(string applicationId, string submissionId, JsonObject submission, string accessToken, CancellationToken cancellationToken)
    {
        var endpoint = $"{CreateAppSubmissionEndpoint(applicationId)}/{Uri.EscapeDataString(submissionId)}";
        var payload = submission.ToJsonString();
        using var request = CreateApiRequest(HttpMethod.Put, endpoint, accessToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("update Store submission", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "update submission response");
    }

    private async Task CommitSubmissionAsync(string applicationId, string submissionId, string accessToken, CancellationToken cancellationToken)
    {
        var endpoint = $"{CreateAppSubmissionEndpoint(applicationId)}/{Uri.EscapeDataString(submissionId)}/commit";
        using var request = CreateApiRequest(HttpMethod.Post, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("commit Store submission", response.StatusCode, response.ReasonPhrase, responseText);
    }

    private async Task<JsonObject> GetSubmissionStatusAsync(string applicationId, string submissionId, string accessToken, CancellationToken cancellationToken)
    {
        var endpoint = $"{CreateAppSubmissionEndpoint(applicationId)}/{Uri.EscapeDataString(submissionId)}/status";
        using var request = CreateApiRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("get Store submission status", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "submission status response");
    }

    private async Task UploadSubmissionArchiveAsync(string uploadUrl, string zipPath, CancellationToken cancellationToken)
    {
        var uploadUri = ValidateHttpsUrl(uploadUrl, nameof(uploadUrl));
        using var stream = File.OpenRead(zipPath);
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri)
        {
            Content = new StreamContent(stream)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("upload Store submission archive", response.StatusCode, response.ReasonPhrase, responseText);
    }

    private static HttpRequestMessage CreateApiRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static HttpRequestMessage CreateDesktopApiRequest(HttpMethod method, string url, string accessToken, string sellerId)
    {
        var request = CreateApiRequest(method, url, accessToken);
        request.Headers.TryAddWithoutValidation("X-Seller-Account-Id", sellerId);
        return request;
    }

    private static string CreateAppSubmissionEndpoint(string applicationId)
    {
        return $"https://manage.devcenter.microsoft.com/v1.0/my/applications/{Uri.EscapeDataString(applicationId)}/submissions";
    }

    private static void ApplyPackageUpdate(JsonObject submission, StoreSubmissionPlan plan)
    {
        var packages = new JsonArray();
        foreach (var packagePath in plan.PackageFiles)
        {
            packages.Add(new JsonObject
            {
                ["fileName"] = Path.GetFileName(packagePath),
                ["fileStatus"] = "PendingUpload",
                ["minimumDirectXVersion"] = plan.MinimumDirectXVersion,
                ["minimumSystemRam"] = plan.MinimumSystemRam
            });
        }

        submission["applicationPackages"] = packages;
    }

    private static void CreateSubmissionArchive(string zipPath, IReadOnlyCollection<string> packageFiles)
    {
        var fullPath = Path.GetFullPath(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        using var archive = ZipFile.Open(fullPath, ZipArchiveMode.Create);
        foreach (var packagePath in packageFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            archive.CreateEntryFromFile(packagePath, Path.GetFileName(packagePath), CompressionLevel.NoCompression);
        }
    }

    private static bool IsTransientCommitStatus(string status)
    {
        return string.Equals(status, "PendingCommit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "CommitStarted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "CommitPending", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "PreProcessing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Processing", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractStatusDetails(JsonObject statusResponse)
    {
        if (statusResponse["statusDetails"] is not JsonObject details)
            return null;

        var messages = new List<string>();
        foreach (var sectionName in new[] { "errors", "warnings" })
        {
            var prefix = sectionName.Length > 1 ? sectionName.Substring(0, sectionName.Length - 1) : sectionName;
            if (details[sectionName] is not JsonArray section)
                continue;

            foreach (var item in section)
            {
                if (item is JsonObject itemObject)
                {
                    var message = GetOptionalString(itemObject, "message")
                        ?? GetOptionalString(itemObject, "details")
                        ?? itemObject.ToJsonString();
                    if (!string.IsNullOrWhiteSpace(message))
                        messages.Add($"{prefix}: {message}");
                }
                else if (item is not null)
                {
                    messages.Add($"{prefix}: {item.ToJsonString()}");
                }
            }
        }

        return messages.Count == 0 ? null : string.Join(" | ", messages);
    }

    private static JsonObject ParseJsonObject(string json, string label)
    {
        var node = JsonNode.Parse(json);
        if (node is not JsonObject jsonObject)
            throw new InvalidOperationException($"Store submission {label} was not a JSON object.");

        return jsonObject;
    }

    private static string GetRequiredString(JsonObject node, string propertyName, string label)
    {
        var value = GetOptionalString(node, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Store submission response did not include {label}.");

        return value!;
    }

    private static string? GetOptionalString(JsonObject node, string propertyName)
    {
        return node[propertyName]?.GetValue<string>();
    }

    private static string? ResolveCredential(string? literalValue, string? environmentVariable)
    {
        var literal = NormalizeNullable(literalValue);
        if (!string.IsNullOrWhiteSpace(literal))
            return literal;

        var envVarName = NormalizeNullable(environmentVariable);
        if (string.IsNullOrWhiteSpace(envVarName))
            return null;

        return NormalizeNullable(Environment.GetEnvironmentVariable(envVarName));
    }

    private static string NormalizeAuthorityHost(string authorityHost)
    {
        var normalized = string.IsNullOrWhiteSpace(authorityHost)
            ? "https://login.microsoftonline.com"
            : authorityHost.Trim();
        var uri = ValidateHttpsUrl(normalized, nameof(authorityHost));
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static void ValidateAuthorityHost(string authorityHost)
    {
        // Blank authority host values intentionally use the same Microsoft Entra default as token resolution.
        var normalized = string.IsNullOrWhiteSpace(authorityHost)
            ? "https://login.microsoftonline.com"
            : authorityHost.Trim();

        ValidateHttpsUrl(normalized, nameof(authorityHost));
    }

    private static Uri ValidateHttpsUrl(string value, string parameterName)
    {
        if (!Uri.TryCreate(FrameworkCompatibility.NotNullOrWhiteSpace(value, parameterName).Trim(), UriKind.Absolute, out var uri))
            throw new ArgumentException($"'{parameterName}' must be a valid absolute URI.", parameterName);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{parameterName}' must use HTTPS.", parameterName);

        return uri;
    }

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Task<string> ReadResponseTextAsync(HttpContent content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return content.ReadAsStringAsync();
    }

    private static InvalidOperationException CreateApiException(string operation, HttpStatusCode statusCode, string? reasonPhrase, string? responseText)
    {
        return new InvalidOperationException(
            $"Unable to {operation} ({(int)statusCode} {reasonPhrase}). {TrimForMessage(responseText)}");
    }

    private static string TrimForMessage(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        return trimmed.Length <= 2000 ? trimmed : trimmed.Substring(0, 2000) + "...";
    }

    private static string ToSafeFileName(string? value, string fallback)
    {
        var input = string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    private async Task<StoreSubmissionResult> RunDesktopInstallerAsync(
        StoreSubmissionAuthenticationOptions authentication,
        StoreSubmissionPlan plan,
        CancellationToken cancellationToken)
    {
        var result = new StoreSubmissionResult
        {
            Plan = plan,
            FinalStatus = "NotStarted"
        };

        try
        {
            var sellerId = ResolveSellerId(authentication);
            var token = await ResolveAccessTokenAsync(authentication, StoreSubmissionProviderKind.DesktopInstaller, cancellationToken).ConfigureAwait(false);
            var statusHistory = new List<StoreSubmissionStatusSnapshot>();

            var draftStatus = await GetDesktopDraftStatusAsync(plan.ApplicationId, token, sellerId, cancellationToken).ConfigureAwait(false);
            var (isReady, readyDetails) = ParseDesktopDraftStatus(draftStatus);
            statusHistory.Add(new StoreSubmissionStatusSnapshot
            {
                CheckedUtc = DateTimeOffset.UtcNow,
                Status = isReady ? "READY" : "NOT_READY",
                Details = readyDetails
            });

            if (!isReady)
            {
                result.StatusHistory = statusHistory.ToArray();
                result.FinalStatus = "NOT_READY";
                result.StatusDetails = readyDetails;
                result.ErrorMessage = string.IsNullOrWhiteSpace(readyDetails)
                    ? $"Desktop Store draft for '{plan.TargetName}' is not ready."
                    : $"Desktop Store draft for '{plan.TargetName}' is not ready. {readyDetails}";
                return result;
            }

            await UpdateDesktopPackagesAsync(plan, token, sellerId, cancellationToken).ConfigureAwait(false);
            await CommitDesktopPackagesAsync(plan.ApplicationId, token, sellerId, cancellationToken).ConfigureAwait(false);

            var uploadDeadline = DateTimeOffset.UtcNow.AddMinutes(plan.PollTimeoutMinutes);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                draftStatus = await GetDesktopDraftStatusAsync(plan.ApplicationId, token, sellerId, cancellationToken).ConfigureAwait(false);
                (isReady, readyDetails) = ParseDesktopDraftStatus(draftStatus);
                statusHistory.Add(new StoreSubmissionStatusSnapshot
                {
                    CheckedUtc = DateTimeOffset.UtcNow,
                    Status = isReady ? "READY" : "PACKAGES_IN_PROGRESS",
                    Details = readyDetails
                });

                if (isReady)
                    break;

                if (DateTimeOffset.UtcNow >= uploadDeadline)
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = "PACKAGES_IN_PROGRESS";
                    result.StatusDetails = readyDetails;
                    result.ErrorMessage =
                        $"Desktop Store package commit for '{plan.TargetName}' did not reach ready state within {plan.PollTimeoutMinutes} minute(s).";
                    return result;
                }

                await Task.Delay(TimeSpan.FromSeconds(plan.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }

            if (!plan.Commit)
            {
                result.Succeeded = true;
                result.FinalStatus = "READY";
                result.StatusDetails = readyDetails;
                result.StatusHistory = statusHistory.ToArray();
                return result;
            }

            var submissionId = await CreateDesktopSubmissionAsync(plan.ApplicationId, token, sellerId, cancellationToken).ConfigureAwait(false);
            result.SubmissionId = submissionId;
            result.CommittedSubmission = true;

            if (!plan.WaitForCommit)
            {
                result.Succeeded = true;
                result.FinalStatus = "INPROGRESS";
                result.StatusHistory = statusHistory.ToArray();
                return result;
            }

            var submitDeadline = DateTimeOffset.UtcNow.AddMinutes(plan.PollTimeoutMinutes);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var submissionStatus = await GetDesktopSubmissionStatusAsync(plan.ApplicationId, submissionId, token, sellerId, cancellationToken).ConfigureAwait(false);
                var (publishingStatus, hasFailed, details) = ParseDesktopSubmissionStatus(submissionStatus);
                statusHistory.Add(new StoreSubmissionStatusSnapshot
                {
                    CheckedUtc = DateTimeOffset.UtcNow,
                    Status = publishingStatus,
                    Details = details
                });

                if (hasFailed || string.Equals(publishingStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = publishingStatus;
                    result.StatusDetails = details;
                    result.ErrorMessage = string.IsNullOrWhiteSpace(details)
                        ? $"Desktop Store submission '{submissionId}' failed."
                        : $"Desktop Store submission '{submissionId}' failed. {details}";
                    return result;
                }

                if (!string.Equals(publishingStatus, "INPROGRESS", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(publishingStatus, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = publishingStatus;
                    result.StatusDetails = details;
                    result.Succeeded = true;
                    return result;
                }

                if (DateTimeOffset.UtcNow >= submitDeadline)
                {
                    result.StatusHistory = statusHistory.ToArray();
                    result.FinalStatus = publishingStatus;
                    result.StatusDetails = details;
                    result.ErrorMessage =
                        $"Desktop Store submission '{submissionId}' did not finish within {plan.PollTimeoutMinutes} minute(s). " +
                        $"Last status: {publishingStatus}.";
                    return result;
                }

                await Task.Delay(TimeSpan.FromSeconds(plan.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static string ResolveSellerId(StoreSubmissionAuthenticationOptions authentication)
    {
        var sellerId = NormalizeNullable(authentication.SellerId);
        if (string.IsNullOrWhiteSpace(sellerId))
            throw new InvalidOperationException("Desktop Store submission requires Authentication.SellerId.");

        return sellerId!;
    }

    private async Task<JsonObject> GetDesktopDraftStatusAsync(string productId, string accessToken, string sellerId, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.store.microsoft.com/submission/v1/product/{Uri.EscapeDataString(productId)}/status";
        using var request = CreateDesktopApiRequest(HttpMethod.Get, endpoint, accessToken, sellerId);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("get desktop Store draft status", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "desktop draft status response");
    }

    private async Task UpdateDesktopPackagesAsync(StoreSubmissionPlan plan, string accessToken, string sellerId, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.store.microsoft.com/submission/v1/product/{Uri.EscapeDataString(plan.ApplicationId)}/packages";
        var payload = new JsonObject
        {
            ["packages"] = new JsonArray(plan.DesktopPackages.Select(CreateDesktopPackageJson).ToArray())
        };

        using var request = CreateDesktopApiRequest(HttpMethod.Put, endpoint, accessToken, sellerId);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("update desktop Store packages", response.StatusCode, response.ReasonPhrase, responseText);

        EnsureDesktopApiSuccess(ParseJsonObject(responseText, "desktop package update response"), "update desktop Store packages");
    }

    private async Task CommitDesktopPackagesAsync(string productId, string accessToken, string sellerId, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.store.microsoft.com/submission/v1/product/{Uri.EscapeDataString(productId)}/packages/commit";
        using var request = CreateDesktopApiRequest(HttpMethod.Post, endpoint, accessToken, sellerId);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("commit desktop Store packages", response.StatusCode, response.ReasonPhrase, responseText);

        EnsureDesktopApiSuccess(ParseJsonObject(responseText, "desktop package commit response"), "commit desktop Store packages");
    }

    private async Task<string> CreateDesktopSubmissionAsync(string productId, string accessToken, string sellerId, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.store.microsoft.com/submission/v1/product/{Uri.EscapeDataString(productId)}/submit";
        using var request = CreateDesktopApiRequest(HttpMethod.Post, endpoint, accessToken, sellerId);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("create desktop Store submission", response.StatusCode, response.ReasonPhrase, responseText);

        var payload = ParseJsonObject(responseText, "desktop create submission response");
        var data = GetDesktopResponseData(payload, "create desktop Store submission");
        return GetRequiredString(data, "submissionId", "desktop submission id");
    }

    private async Task<JsonObject> GetDesktopSubmissionStatusAsync(string productId, string submissionId, string accessToken, string sellerId, CancellationToken cancellationToken)
    {
        var endpoint = $"https://api.store.microsoft.com/submission/v1/product/{Uri.EscapeDataString(productId)}/submission/{Uri.EscapeDataString(submissionId)}/status";
        using var request = CreateDesktopApiRequest(HttpMethod.Get, endpoint, accessToken, sellerId);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await ReadResponseTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException("get desktop Store submission status", response.StatusCode, response.ReasonPhrase, responseText);

        return ParseJsonObject(responseText, "desktop submission status response");
    }

    private static (bool IsReady, string? Details) ParseDesktopDraftStatus(JsonObject payload)
    {
        var data = GetDesktopResponseData(payload, "get desktop Store draft status");
        var isReady = data["isReady"]?.GetValue<bool>() ?? false;
        return (isReady, ExtractDesktopApiDetails(payload));
    }

    private static (string PublishingStatus, bool HasFailed, string? Details) ParseDesktopSubmissionStatus(JsonObject payload)
    {
        var data = GetDesktopResponseData(payload, "get desktop Store submission status");
        var publishingStatus = GetOptionalString(data, "publishingStatus") ?? "UNKNOWN";
        var hasFailed = data["hasFailed"]?.GetValue<bool>() ?? false;
        return (publishingStatus, hasFailed, ExtractDesktopApiDetails(payload));
    }

    private static JsonObject GetDesktopResponseData(JsonObject payload, string operation)
    {
        EnsureDesktopApiSuccess(payload, operation);
        if (payload["responseData"] is not JsonObject data)
            throw new InvalidOperationException($"Desktop Store API response for '{operation}' did not include responseData.");

        return data;
    }

    private static void EnsureDesktopApiSuccess(JsonObject payload, string operation)
    {
        var isSuccess = payload["isSuccess"]?.GetValue<bool>() ?? false;
        if (isSuccess)
            return;

        var details = ExtractDesktopApiDetails(payload);
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(details)
                ? $"Desktop Store API call failed during '{operation}'."
                : $"Desktop Store API call failed during '{operation}'. {details}");
    }

    private static string? ExtractDesktopApiDetails(JsonObject payload)
    {
        if (payload["errors"] is not JsonArray errors || errors.Count == 0)
            return null;

        var messages = new List<string>();
        foreach (var item in errors)
        {
            if (item is JsonObject error)
            {
                var code = GetOptionalString(error, "code");
                var message = GetOptionalString(error, "message");
                var target = GetOptionalString(error, "target");
                var parts = new[] { code, target, message }.Where(part => !string.IsNullOrWhiteSpace(part));
                var combined = string.Join(": ", parts);
                if (!string.IsNullOrWhiteSpace(combined))
                    messages.Add(combined);
            }
            else if (item is not null)
            {
                messages.Add(item.ToJsonString());
            }
        }

        return messages.Count == 0 ? null : string.Join(" | ", messages);
    }

    private static JsonObject CreateDesktopPackageJson(StoreSubmissionDesktopPackage package)
    {
        var json = new JsonObject
        {
            ["packageUrl"] = package.PackageUrl,
            ["languages"] = new JsonArray(package.Languages.Select(language => (JsonNode?)language).ToArray()),
            ["architectures"] = new JsonArray(package.Architectures.Select(architecture => (JsonNode?)architecture).ToArray()),
            ["isSilentInstall"] = package.IsSilentInstall,
            ["packageType"] = package.PackageType
        };

        if (!string.IsNullOrWhiteSpace(package.InstallerParameters))
            json["installerParameters"] = package.InstallerParameters;
        if (!string.IsNullOrWhiteSpace(package.GenericDocUrl))
            json["genericDocUrl"] = package.GenericDocUrl;

        return json;
    }

    private static StoreSubmissionDesktopPackage CloneDesktopPackage(StoreSubmissionDesktopPackage package)
    {
        return new StoreSubmissionDesktopPackage
        {
            PackageUrl = package.PackageUrl,
            Languages = (package.Languages ?? Array.Empty<string>()).ToArray(),
            Architectures = (package.Architectures ?? Array.Empty<string>()).ToArray(),
            IsSilentInstall = package.IsSilentInstall,
            InstallerParameters = package.InstallerParameters,
            GenericDocUrl = package.GenericDocUrl,
            PackageType = package.PackageType
        };
    }

    private static HttpClient CreateSharedHttpClient()
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
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge", "1.0"));
        return client;
    }
}
