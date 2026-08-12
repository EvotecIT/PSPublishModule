namespace PowerForge.Tests;

public sealed partial class VirusTotalMonitorReleaseTests
{
    private static VirusTotalMonitorArtifact Artifact(string sourcePath, string destinationPath)
        => new()
        {
            SourcePath = sourcePath,
            Kind = VirusTotalArtifactKind.MsiPackage,
            DestinationPath = destinationPath
        };

    private sealed class FakeClient : IVirusTotalMonitorUploadClient
    {
        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new VirusTotalMonitorUploadResponse
            {
                MonitorId = "monitor-id",
                RemotePath = artifact.DestinationPath,
                LocalSha256 = "LOCAL",
                RemoteSha256 = "REMOTE",
                VerificationStatus = VirusTotalMonitorVerificationStatus.Verified
            });

        public void Dispose()
        {
        }
    }

    private sealed class SequencedClient : IVirusTotalMonitorUploadClient
    {
        private readonly int? _failOnCall;
        private int _calls;

        public SequencedClient(int? failOnCall = null)
        {
            _failOnCall = failOnCall;
        }

        public List<string?> ExistingItemIds { get; } = new();

        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _calls++;
            ExistingItemIds.Add(artifact.ExistingItemId);
            if (_failOnCall == _calls)
                throw new InvalidOperationException("simulated upload failure");

            return Task.FromResult(new VirusTotalMonitorUploadResponse
            {
                MonitorId = artifact.ExistingItemId ?? $"item-{_calls}",
                RemotePath = artifact.DestinationPath,
                LocalSha256 = $"LOCAL-{_calls}",
                RemoteSha256 = $"LOCAL-{_calls}",
                VerificationStatus = VirusTotalMonitorVerificationStatus.Verified
            });
        }

        public void Dispose()
        {
        }
    }

    private sealed class CancelAfterUploadClient : IVirusTotalMonitorUploadClient
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelAfterUploadClient(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cancellation.Cancel();
            return Task.FromResult(new VirusTotalMonitorUploadResponse
            {
                MonitorId = "uploaded-before-cancellation",
                RemotePath = artifact.DestinationPath,
                LocalSha256 = "LOCAL",
                RemoteSha256 = "LOCAL",
                VerificationStatus = VirusTotalMonitorVerificationStatus.Verified
            });
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingSecretClient : IVirusTotalMonitorUploadClient
    {
        private readonly string _apiKey;

        public ThrowingSecretClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        public Task<VirusTotalMonitorUploadResponse> UploadAsync(
            VirusTotalMonitorArtifact artifact,
            VirusTotalMonitorPublishRequest request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Provider rejected {_apiKey}.");

        public void Dispose()
        {
        }
    }
}
