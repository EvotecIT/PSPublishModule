using VirusTotalAnalyzer;
using VirusTotalAnalyzer.Models;

namespace PowerForge;

internal interface IVirusTotalMonitorUploadClient : IDisposable
{
    Task<VirusTotalMonitorUploadResponse> UploadAsync(
        VirusTotalMonitorArtifact artifact,
        VirusTotalMonitorPublishRequest request,
        CancellationToken cancellationToken);
}

internal sealed class VirusTotalMonitorPublisher
{
    private readonly Func<string, TimeSpan, IVirusTotalMonitorUploadClient> _clientFactory;
    private readonly Func<DateTimeOffset> _utcNow;

    public VirusTotalMonitorPublisher()
        : this(
            (apiKey, timeout) => new VirusTotalAnalyzerUploadClient(apiKey, timeout),
            () => DateTimeOffset.UtcNow)
    {
    }

    internal VirusTotalMonitorPublisher(
        Func<string, TimeSpan, IVirusTotalMonitorUploadClient> clientFactory,
        Func<DateTimeOffset>? utcNow = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<VirusTotalMonitorPublishResult> PublishAsync(
        VirusTotalMonitorPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var client = _clientFactory(request.ApiKey, request.RequestTimeout);
        var receipts = new List<VirusTotalMonitorArtifactReceipt>(request.Artifacts.Length);

        foreach (var artifact in request.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ValidateArtifact(artifact);
                var response = await client.UploadAsync(artifact, request, cancellationToken).ConfigureAwait(false);
                receipts.Add(new VirusTotalMonitorArtifactReceipt
                {
                    SourcePath = Path.GetFullPath(artifact.SourcePath),
                    Kind = artifact.Kind,
                    DestinationPath = response.RemotePath,
                    MonitorId = response.MonitorId,
                    LocalSha256 = response.LocalSha256,
                    RemoteSha256 = response.RemoteSha256,
                    VerificationStatus = response.VerificationStatus,
                    UsedLargeFileUploadUrl = response.UsedLargeFileUploadUrl,
                    CurrentDetectionCount = response.CurrentDetectionCount,
                    UploadedAtUtc = _utcNow()
                });
                await WriteCheckpointAsync(request, receipts, success: false, errorMessage: null, CancellationToken.None)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (request.VerifySha256 &&
                    response.VerificationStatus != VirusTotalMonitorVerificationStatus.Verified)
                {
                    return await WriteCheckpointAsync(
                            request,
                            receipts,
                            success: false,
                            errorMessage:
                                $"VirusTotal Monitor hash verification did not complete for '{artifact.DestinationPath}' " +
                                $"(status: {response.VerificationStatus}).",
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var failure = await WriteCheckpointAsync(
                        request,
                        receipts,
                        success: false,
                        errorMessage: RedactApiKey(exception.Message, request.ApiKey),
                        cancellationToken)
                    .ConfigureAwait(false);
                return failure;
            }
        }

        return await WriteCheckpointAsync(request, receipts, success: true, errorMessage: null, CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static async Task<VirusTotalMonitorPublishResult> WriteCheckpointAsync(
        VirusTotalMonitorPublishRequest request,
        List<VirusTotalMonitorArtifactReceipt> receipts,
        bool success,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var result = new VirusTotalMonitorPublishResult
        {
            Success = success,
            ErrorMessage = errorMessage,
            Artifacts = receipts.ToArray()
        };
        if (request.CheckpointAsync is not null)
            await request.CheckpointAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static void ValidateRequest(VirusTotalMonitorPublishRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new ArgumentException("VirusTotal Monitor publishing requires an API key.", nameof(request));
        if (request.ApiKey.IndexOf('\r') >= 0 || request.ApiKey.IndexOf('\n') >= 0)
            throw new ArgumentException("VirusTotal Monitor API key must be a single-line secret.", nameof(request));
        if (request.RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "RequestTimeout must be positive.");
        if (request.VerificationTimeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "VerificationTimeout must not be negative.");
        if (request.PollingInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(request), "PollingInterval must be positive.");
        if (request.Artifacts is null || request.Artifacts.Length == 0)
            throw new ArgumentException("VirusTotal Monitor publishing requires at least one artifact.", nameof(request));

        var duplicatePath = request.Artifacts
            .GroupBy(artifact => artifact.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePath is not null)
            throw new ArgumentException($"VirusTotal Monitor destination paths must be unique: '{duplicatePath.Key}'.", nameof(request));
    }

    private static void ValidateArtifact(VirusTotalMonitorArtifact artifact)
    {
        if (artifact is null)
            throw new ArgumentException("VirusTotal Monitor artifacts cannot contain null entries.", nameof(artifact));
        if (string.IsNullOrWhiteSpace(artifact.SourcePath) || !File.Exists(artifact.SourcePath))
            throw new FileNotFoundException("VirusTotal Monitor artifact does not exist.", artifact.SourcePath);
        if (string.IsNullOrWhiteSpace(artifact.DestinationPath) ||
            !artifact.DestinationPath.StartsWith("/", StringComparison.Ordinal) ||
            artifact.DestinationPath.EndsWith("/", StringComparison.Ordinal))
        {
            throw new ArgumentException("VirusTotal Monitor destination path must start with '/' and include a file name.", nameof(artifact));
        }
        if (artifact.ExistingItemId is { } itemId &&
            (string.IsNullOrWhiteSpace(itemId) || itemId.IndexOfAny(new[] { '\r', '\n' }) >= 0))
        {
            throw new ArgumentException("VirusTotal Monitor existing item id must be a non-empty single-line value.", nameof(artifact));
        }
    }

    internal static string RedactApiKey(string? value, string? apiKey)
    {
        var safe = value ?? string.Empty;
        return string.IsNullOrWhiteSpace(apiKey)
            ? safe
            : safe.Replace(apiKey!, "[REDACTED]");
    }
}

internal sealed class VirusTotalAnalyzerUploadClient : IVirusTotalMonitorUploadClient
{
    private readonly VirusTotalClient _client;

    public VirusTotalAnalyzerUploadClient(string apiKey, TimeSpan timeout)
    {
        _client = VirusTotalClient.Create(apiKey, "PowerForge/VirusTotalMonitor", timeout);
    }

    public async Task<VirusTotalMonitorUploadResponse> UploadAsync(
        VirusTotalMonitorArtifact artifact,
        VirusTotalMonitorPublishRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _client.UploadMonitorFileAsync(
            artifact.SourcePath,
            new MonitorUploadOptions
            {
                Path = string.IsNullOrWhiteSpace(artifact.ExistingItemId) ? artifact.DestinationPath : null,
                ExistingItemId = artifact.ExistingItemId,
                Details = artifact.Details,
                VerifySha256 = request.VerifySha256,
                VerificationTimeout = request.VerificationTimeout,
                PollingInterval = request.PollingInterval
            },
            cancellationToken).ConfigureAwait(false);

        return new VirusTotalMonitorUploadResponse
        {
            MonitorId = result.MonitorId,
            RemotePath = result.RemotePath,
            LocalSha256 = result.LocalSha256,
            RemoteSha256 = result.RemoteSha256,
            VerificationStatus = result.VerificationStatus switch
            {
                MonitorUploadVerificationStatus.NotRequested => VirusTotalMonitorVerificationStatus.NotRequested,
                MonitorUploadVerificationStatus.Pending => VirusTotalMonitorVerificationStatus.Pending,
                MonitorUploadVerificationStatus.Verified => VirusTotalMonitorVerificationStatus.Verified,
                _ => throw new InvalidOperationException("VirusTotal Monitor returned an unsupported verification status.")
            },
            UsedLargeFileUploadUrl = result.UsedLargeFileUploadUrl,
            CurrentDetectionCount = result.CurrentDetectionCount
        };
    }

    public void Dispose() => _client.Dispose();
}
