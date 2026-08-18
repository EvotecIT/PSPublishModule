using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PowerForge.Web.Cli;

internal sealed class DeploymentArtifactVerificationOptions
{
    public string[] PathPrefixes { get; set; } = Array.Empty<string>();
    public int Attempts { get; set; } = 3;
    public int DelayMilliseconds { get; set; } = 5000;
    public int RequestAttempts { get; set; } = 2;
    public int RequestRetryDelayMilliseconds { get; set; } = 250;
    public int TimeoutMilliseconds { get; set; } = 30000;
    public int MaxFiles { get; set; } = 50_000;
    public long MaxResponseBytes { get; set; } = 256L * 1024L * 1024L;
    public long MaxTotalBytes { get; set; } = 8L * 1024L * 1024L * 1024L;
}

internal sealed class DeploymentArtifactVerificationResult
{
    public int SchemaVersion { get; set; } = 1;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string[] PathPrefixes { get; set; } = Array.Empty<string>();
    public int SelectedFileCount { get; set; }
    public long SelectedBytes { get; set; }
    public int AttemptsConfigured { get; set; }
    public int AttemptsCompleted { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public DeploymentArtifactVerificationAttempt[] Attempts { get; set; } = Array.Empty<DeploymentArtifactVerificationAttempt>();
}

internal sealed class DeploymentArtifactVerificationAttempt
{
    public int Number { get; set; }
    public string CacheIdentity { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long DownloadedBytes { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public DeploymentArtifactVerificationEntry[] Entries { get; set; } = Array.Empty<DeploymentArtifactVerificationEntry>();
}

internal sealed class DeploymentArtifactVerificationEntry
{
    public string Path { get; set; } = string.Empty;
    public string RequestUrl { get; set; } = string.Empty;
    public int RequestAttempt { get; set; }
    public int? HttpStatusCode { get; set; }
    public long ExpectedBytes { get; set; }
    public long? ContentLength { get; set; }
    public long DownloadedBytes { get; set; }
    public string ExpectedSha256 { get; set; } = string.Empty;
    public string? ActualSha256 { get; set; }
    public string? CacheStatus { get; set; }
    public string? Age { get; set; }
    public string? Ray { get; set; }
    public string? ETag { get; set; }
    public string? LastModified { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

internal static class DeploymentArtifactVerifier
{
    private const string CacheQueryParameter = "_powerforge_verify";
    private static readonly int[] RetryableStatusCodes = [408, 425, 429, 500, 502, 503, 504];

    internal static DeploymentArtifactVerificationResult Verify(
        CloudflareDeploymentManifest manifest,
        DeploymentArtifactVerificationOptions? options = null,
        HttpClient? httpClient = null,
        Action<TimeSpan>? delay = null,
        Func<string>? cacheIdentityFactory = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        CloudflareDeploymentManifestStore.Validate(manifest);
        options ??= new DeploymentArtifactVerificationOptions();
        ValidateOptions(options);

        var prefixes = NormalizePrefixes(options.PathPrefixes);
        var selected = manifest.Files
            .Where(entry => prefixes.Length == 0 || prefixes.Any(prefix => entry.Path.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException(prefixes.Length == 0
                ? "Deployment manifest does not contain any files to verify."
                : $"Deployment manifest has no files under path prefix(es): {string.Join(", ", prefixes)}.");
        if (selected.Length > options.MaxFiles)
            throw new InvalidOperationException($"Deployment verification selected {selected.Length} files, exceeding the configured limit of {options.MaxFiles}.");

        long selectedBytes = 0;
        foreach (var entry in selected)
        {
            if (entry.Length > options.MaxResponseBytes)
                throw new InvalidOperationException($"Deployment manifest path '{entry.Path}' declares {entry.Length} bytes, exceeding the per-response limit of {options.MaxResponseBytes}.");
            selectedBytes = checked(selectedBytes + entry.Length);
            if (selectedBytes > options.MaxTotalBytes)
                throw new InvalidOperationException($"Deployment verification selected more than the configured aggregate limit of {options.MaxTotalBytes} bytes.");
        }

        var ownsClient = httpClient is null;
        httpClient ??= CreateHttpClient(options.TimeoutMilliseconds);
        delay ??= static duration => Thread.Sleep(duration);
        cacheIdentityFactory ??= static () => Guid.NewGuid().ToString("N");

        var stopwatch = Stopwatch.StartNew();
        var attempts = new List<DeploymentArtifactVerificationAttempt>(options.Attempts);
        try
        {
            for (var attemptNumber = 1; attemptNumber <= options.Attempts; attemptNumber++)
            {
                var identitySeed = cacheIdentityFactory();
                if (string.IsNullOrWhiteSpace(identitySeed))
                    throw new InvalidOperationException("Deployment verification cache identity factory returned an empty value.");
                var cacheIdentity = $"{identitySeed}-{attemptNumber}";
                var attemptStopwatch = Stopwatch.StartNew();
                var observations = new List<DeploymentArtifactVerificationEntry>(selected.Length);
                var attempt = new DeploymentArtifactVerificationAttempt
                {
                    Number = attemptNumber,
                    CacheIdentity = cacheIdentity
                };
                attempts.Add(attempt);

                try
                {
                    foreach (var entry in selected)
                    {
                        var target = AddCacheIdentity(
                            CloudflareDeploymentManifestStore.ResolveUrl(manifest.BaseUrl, entry.Path),
                            cacheIdentity);
                        var observation = VerifyEntry(httpClient, entry, target, options, delay);
                        observations.Add(observation);
                        attempt.DownloadedBytes = checked(attempt.DownloadedBytes + observation.DownloadedBytes);
                        if (!observation.Success)
                            throw new InvalidDataException(observation.Error);
                    }

                    attempt.Success = true;
                    attempt.Entries = observations.ToArray();
                    attempt.ElapsedMilliseconds = attemptStopwatch.ElapsedMilliseconds;
                    stopwatch.Stop();
                    return new DeploymentArtifactVerificationResult
                    {
                        Success = true,
                        Message = $"Verified {selected.Length} deployed file(s) and {selectedBytes} expected byte(s) against the build manifest on attempt {attemptNumber}.",
                        BaseUrl = manifest.BaseUrl,
                        PathPrefixes = prefixes,
                        SelectedFileCount = selected.Length,
                        SelectedBytes = selectedBytes,
                        AttemptsConfigured = options.Attempts,
                        AttemptsCompleted = attempts.Count,
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                        Attempts = attempts.ToArray()
                    };
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    attempt.Success = false;
                    attempt.Error = ex.Message;
                    attempt.Entries = observations.ToArray();
                    attempt.ElapsedMilliseconds = attemptStopwatch.ElapsedMilliseconds;
                    if (attemptNumber < options.Attempts && options.DelayMilliseconds > 0)
                        delay(TimeSpan.FromMilliseconds(options.DelayMilliseconds));
                }
            }
        }
        finally
        {
            if (ownsClient)
                httpClient.Dispose();
        }

        stopwatch.Stop();
        var lastError = attempts.LastOrDefault()?.Error ?? "unknown verification failure";
        return new DeploymentArtifactVerificationResult
        {
            Success = false,
            Message = $"Deployment verification failed after {attempts.Count} complete graph attempt(s): {lastError}",
            BaseUrl = manifest.BaseUrl,
            PathPrefixes = prefixes,
            SelectedFileCount = selected.Length,
            SelectedBytes = selectedBytes,
            AttemptsConfigured = options.Attempts,
            AttemptsCompleted = attempts.Count,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            Attempts = attempts.ToArray()
        };
    }

    private static DeploymentArtifactVerificationEntry VerifyEntry(
        HttpClient httpClient,
        CloudflareDeploymentManifestEntry expected,
        Uri target,
        DeploymentArtifactVerificationOptions options,
        Action<TimeSpan> delay)
    {
        DeploymentArtifactVerificationEntry? last = null;
        for (var requestAttempt = 1; requestAttempt <= options.RequestAttempts; requestAttempt++)
        {
            last = new DeploymentArtifactVerificationEntry
            {
                Path = expected.Path,
                RequestUrl = target.AbsoluteUri,
                RequestAttempt = requestAttempt,
                ExpectedBytes = expected.Length,
                ExpectedSha256 = expected.Sha256
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.Zero
                };
                request.Headers.Pragma.ParseAdd("no-cache");
                using var response = httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
                last.HttpStatusCode = (int)response.StatusCode;
                last.ContentLength = response.Content.Headers.ContentLength;
                last.CacheStatus = GetHeader(response, "CF-Cache-Status");
                last.Age = GetHeader(response, "Age");
                last.Ray = GetHeader(response, "CF-Ray");
                last.ETag = GetHeader(response, "ETag");
                last.LastModified = GetHeader(response, "Last-Modified");

                if (!response.IsSuccessStatusCode)
                {
                    last.Error = $"Deployment path '{expected.Path}' returned HTTP {(int)response.StatusCode}.";
                    if (requestAttempt < options.RequestAttempts && RetryableStatusCodes.Contains((int)response.StatusCode))
                    {
                        DelayRequestRetry(delay, options.RequestRetryDelayMilliseconds, requestAttempt);
                        continue;
                    }
                    return last;
                }

                if (last.ContentLength.HasValue && last.ContentLength.Value != expected.Length)
                {
                    last.Error = $"Deployment path '{expected.Path}' declared {last.ContentLength.Value} bytes; expected {expected.Length}.";
                    return last;
                }

                using var stream = response.Content.ReadAsStream();
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long downloaded = 0;
                while (true)
                {
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    downloaded = checked(downloaded + read);
                    last.DownloadedBytes = downloaded;
                    if (downloaded > expected.Length || downloaded > options.MaxResponseBytes)
                    {
                        last.Error = $"Deployment path '{expected.Path}' exceeded its expected {expected.Length} bytes while downloading.";
                        return last;
                    }
                    hash.AppendData(buffer, 0, read);
                }

                last.ActualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (downloaded != expected.Length)
                {
                    last.Error = $"Deployment path '{expected.Path}' downloaded {downloaded} bytes; expected {expected.Length}.";
                    return last;
                }
                if (!last.ActualSha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    last.Error = $"Deployment path '{expected.Path}' SHA-256 mismatch; expected {expected.Sha256}, received {last.ActualSha256}.";
                    return last;
                }

                last.Success = true;
                return last;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last.Error = $"Deployment path '{expected.Path}' request failed: {ex.Message}";
                if (requestAttempt < options.RequestAttempts)
                {
                    DelayRequestRetry(delay, options.RequestRetryDelayMilliseconds, requestAttempt);
                    continue;
                }
                return last;
            }
        }

        return last ?? throw new InvalidOperationException("Deployment verification did not execute a request attempt.");
    }

    private static HttpClient CreateHttpClient(int timeoutMilliseconds)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge-Deployment-Verify/1.0");
        return client;
    }

    private static Uri AddCacheIdentity(Uri uri, string identity)
    {
        var builder = new UriBuilder(uri);
        var token = Uri.EscapeDataString(identity);
        var existing = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existing)
            ? $"{CacheQueryParameter}={token}"
            : $"{existing}&{CacheQueryParameter}={token}";
        return builder.Uri;
    }

    private static string[] NormalizePrefixes(IEnumerable<string>? values)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values ?? Array.Empty<string>())
        {
            var prefix = (raw ?? string.Empty).Trim().TrimStart('/');
            if (string.IsNullOrWhiteSpace(prefix) ||
                prefix.Contains('\\') ||
                prefix.Contains('?') ||
                prefix.Contains('#') ||
                Uri.TryCreate(prefix, UriKind.Absolute, out _) ||
                prefix.Split('/', StringSplitOptions.None).Any(segment => segment is ".." or "."))
                throw new InvalidDataException($"Deployment verification path prefix '{raw}' is unsafe.");
            normalized.Add(prefix);
        }
        return normalized.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateOptions(DeploymentArtifactVerificationOptions options)
    {
        if (options.Attempts is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(options.Attempts), "Attempts must be between 1 and 20.");
        if (options.DelayMilliseconds is < 0 or > 300_000)
            throw new ArgumentOutOfRangeException(nameof(options.DelayMilliseconds), "DelayMilliseconds must be between 0 and 300000.");
        if (options.RequestAttempts is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(options.RequestAttempts), "RequestAttempts must be between 1 and 5.");
        if (options.RequestRetryDelayMilliseconds is < 0 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(options.RequestRetryDelayMilliseconds), "RequestRetryDelayMilliseconds must be between 0 and 30000.");
        if (options.TimeoutMilliseconds is < 1000 or > 120_000)
            throw new ArgumentOutOfRangeException(nameof(options.TimeoutMilliseconds), "TimeoutMilliseconds must be between 1000 and 120000.");
        if (options.MaxFiles is < 1 or > CloudflareDeploymentManifestStore.MaxManifestEntries)
            throw new ArgumentOutOfRangeException(nameof(options.MaxFiles), $"MaxFiles must be between 1 and {CloudflareDeploymentManifestStore.MaxManifestEntries}.");
        if (options.MaxResponseBytes is < 1 or > 2L * 1024L * 1024L * 1024L)
            throw new ArgumentOutOfRangeException(nameof(options.MaxResponseBytes), "MaxResponseBytes must be between 1 and 2147483648.");
        if (options.MaxTotalBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MaxTotalBytes), "MaxTotalBytes must be positive.");
    }

    private static void DelayRequestRetry(Action<TimeSpan> delay, int baseDelayMilliseconds, int requestAttempt)
    {
        if (baseDelayMilliseconds > 0)
            delay(TimeSpan.FromMilliseconds((long)baseDelayMilliseconds * requestAttempt));
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
            return string.Join(", ", values);
        return null;
    }
}
