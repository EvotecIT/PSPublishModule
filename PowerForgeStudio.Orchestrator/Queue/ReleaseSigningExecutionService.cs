using PowerForge;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseSigningExecutionService : IReleaseSigningExecutionService
{
    private static readonly string[] AuthenticodeDirectoryIncludes = ["*.ps1", "*.psm1", "*.psd1", "*.dll", "*.exe", "*.cat"];
    private readonly ReleaseBuildCheckpointReader _checkpointReader;
    private readonly ReleaseSigningHostSettingsResolver _settingsResolver;
    private readonly CertificateFingerprintResolver _certificateFingerprintResolver;
    private readonly Func<AuthenticodeSigningHostRequest, CancellationToken, Task<AuthenticodeSigningHostResult>> _signAuthenticodeAsync;
    private readonly Func<DotNetNuGetSignRequest, CancellationToken, Task<DotNetNuGetSignResult>> _signNuGetPackageAsync;

    public ReleaseSigningExecutionService()
        : this(
            new ReleaseBuildCheckpointReader(),
            new ReleaseSigningHostSettingsResolver(PowerForgeStudioHostPaths.ResolvePSPublishModulePath),
            new CertificateFingerprintResolver(),
            (request, cancellationToken) => new AuthenticodeSigningHostService().SignAsync(request, cancellationToken),
            (request, cancellationToken) => new DotNetNuGetClient().SignPackageAsync(request, cancellationToken))
    {
    }

    internal ReleaseSigningExecutionService(
        ReleaseBuildCheckpointReader checkpointReader,
        ReleaseSigningHostSettingsResolver settingsResolver,
        CertificateFingerprintResolver certificateFingerprintResolver,
        Func<AuthenticodeSigningHostRequest, CancellationToken, Task<AuthenticodeSigningHostResult>> signAuthenticodeAsync,
        Func<DotNetNuGetSignRequest, CancellationToken, Task<DotNetNuGetSignResult>> signNuGetPackageAsync)
    {
        _checkpointReader = checkpointReader;
        _settingsResolver = settingsResolver;
        _certificateFingerprintResolver = certificateFingerprintResolver;
        _signAuthenticodeAsync = signAuthenticodeAsync;
        _signNuGetPackageAsync = signNuGetPackageAsync;
    }

    public async Task<ReleaseSigningExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var manifest = _checkpointReader.BuildSigningManifest([queueItem]);
        if (manifest.Count == 0)
        {
            return new ReleaseSigningExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "No signable artifacts were captured for this queue item.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: []);
        }

        var settings = _settingsResolver.Resolve();
        if (!settings.IsConfigured)
        {
            return new ReleaseSigningExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: settings.MissingConfigurationMessage!,
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: manifest.Select(artifact => new ReleaseSigningReceipt(
                    RootPath: queueItem.RootPath,
                    RepositoryName: artifact.RepositoryName,
                    AdapterKind: artifact.AdapterKind,
                    ArtifactPath: artifact.ArtifactPath,
                    ArtifactKind: artifact.ArtifactKind,
                    Status: ReleaseSigningReceiptStatus.Failed,
                    Summary: settings.MissingConfigurationMessage!,
                    SignedAtUtc: DateTimeOffset.UtcNow)).ToList());
        }

        var receipts = new List<ReleaseSigningReceipt>(manifest.Count);
        foreach (var artifact in manifest)
        {
            receipts.Add(await SignArtifactAsync(queueItem.RootPath, artifact, settings, cancellationToken));
        }

        var failed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Failed);
        var signed = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Signed);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseSigningReceiptStatus.Skipped);

        var summary = failed > 0
            ? $"Signing completed with {failed} failure(s), {signed} signed, {skipped} skipped."
            : $"Signing completed with {signed} signed and {skipped} skipped artifact(s).";

        return new ReleaseSigningExecutionResult(
            RootPath: queueItem.RootPath,
            Succeeded: failed == 0,
            Summary: summary,
            SourceCheckpointStateJson: queueItem.CheckpointStateJson,
            Receipts: receipts);
    }

    private async Task<ReleaseSigningReceipt> SignArtifactAsync(
        string rootPath,
        ReleaseSigningArtifact artifact,
        ReleaseSigningHostSettings settings,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var extension = Path.GetExtension(artifact.ArtifactPath);

        if (string.Equals(artifact.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase))
        {
            return await SignWithRegisterCertificateAsync(
                rootPath,
                artifact,
                artifact.ArtifactPath,
                AuthenticodeDirectoryIncludes,
                settings,
                timestamp,
                cancellationToken);
        }

        if (IsAuthenticodeFile(extension))
        {
            var parent = Path.GetDirectoryName(artifact.ArtifactPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return FailedReceipt(rootPath, artifact, "Artifact has no parent directory.", timestamp);
            }

            return await SignWithRegisterCertificateAsync(
                rootPath,
                artifact,
                parent,
                [Path.GetFileName(artifact.ArtifactPath)],
                settings,
                timestamp,
                cancellationToken);
        }

        if (IsNuGetPackage(extension))
        {
            var packageResult = await SignNuGetPackageAsync(artifact, settings, cancellationToken);
            return new ReleaseSigningReceipt(
                RootPath: rootPath,
                RepositoryName: artifact.RepositoryName,
                AdapterKind: artifact.AdapterKind,
                ArtifactPath: artifact.ArtifactPath,
                ArtifactKind: artifact.ArtifactKind,
                Status: packageResult.Succeeded ? ReleaseSigningReceiptStatus.Signed : ReleaseSigningReceiptStatus.Failed,
                Summary: packageResult.Succeeded ? "Package signed with dotnet nuget sign." : packageResult.ErrorMessage!,
                SignedAtUtc: timestamp);
        }

        return new ReleaseSigningReceipt(
            RootPath: rootPath,
            RepositoryName: artifact.RepositoryName,
            AdapterKind: artifact.AdapterKind,
            ArtifactPath: artifact.ArtifactPath,
            ArtifactKind: artifact.ArtifactKind,
            Status: ReleaseSigningReceiptStatus.Skipped,
            Summary: $"No signing strategy is configured for {extension} artifacts.",
            SignedAtUtc: timestamp);
    }

    private async Task<ReleaseSigningReceipt> SignWithRegisterCertificateAsync(
        string rootPath,
        ReleaseSigningArtifact artifact,
        string signingPath,
        IReadOnlyList<string> includePatterns,
        ReleaseSigningHostSettings settings,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(signingPath))
        {
            return FailedReceipt(rootPath, artifact, $"Signing path '{signingPath}' was not found.", timestamp);
        }

        var execution = await _signAuthenticodeAsync(new AuthenticodeSigningHostRequest {
            SigningPath = signingPath,
            IncludePatterns = includePatterns,
            ModulePath = settings.ModulePath,
            Thumbprint = settings.Thumbprint!,
            StoreName = settings.StoreName,
            TimeStampServer = settings.TimeStampServer
        }, cancellationToken);
        var succeeded = execution.Succeeded;
        var detail = succeeded
            ? "Authenticode signing completed."
            : FirstLine(execution.StandardError) ?? FirstLine(execution.StandardOutput) ?? "Register-Certificate failed.";

        return new ReleaseSigningReceipt(
            RootPath: rootPath,
            RepositoryName: artifact.RepositoryName,
            AdapterKind: artifact.AdapterKind,
            ArtifactPath: artifact.ArtifactPath,
            ArtifactKind: artifact.ArtifactKind,
            Status: succeeded ? ReleaseSigningReceiptStatus.Signed : ReleaseSigningReceiptStatus.Failed,
            Summary: detail,
            SignedAtUtc: timestamp);
    }

    private async Task<(bool Succeeded, string? ErrorMessage)> SignNuGetPackageAsync(ReleaseSigningArtifact artifact, ReleaseSigningHostSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(artifact.ArtifactPath))
        {
            return (false, $"Package '{artifact.ArtifactPath}' was not found.");
        }

        var sha256 = _certificateFingerprintResolver.ResolveSha256(settings.Thumbprint!, settings.StoreName);
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return (false, $"Unable to resolve SHA256 certificate fingerprint for thumbprint {settings.Thumbprint}.");
        }

        var result = await _signNuGetPackageAsync(
            new DotNetNuGetSignRequest(
                packagePath: artifact.ArtifactPath,
                certificateFingerprint: sha256,
                certificateStoreLocation: settings.StoreName,
                timeStampServer: settings.TimeStampServer,
                workingDirectory: Path.GetDirectoryName(artifact.ArtifactPath)),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            return (true, null);
        }

        return (false, result.ErrorMessage);
    }

    private static bool IsAuthenticodeFile(string extension)
        => extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".cat", StringComparison.OrdinalIgnoreCase);

    private static bool IsNuGetPackage(string extension)
        => extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase)
           || extension.Equals(".snupkg", StringComparison.OrdinalIgnoreCase);

    private static ReleaseSigningReceipt FailedReceipt(string rootPath, ReleaseSigningArtifact artifact, string summary, DateTimeOffset timestamp)
    {
        return new ReleaseSigningReceipt(
            RootPath: rootPath,
            RepositoryName: artifact.RepositoryName,
            AdapterKind: artifact.AdapterKind,
            ArtifactPath: artifact.ArtifactPath,
            ArtifactKind: artifact.ArtifactKind,
            Status: ReleaseSigningReceiptStatus.Failed,
            Summary: summary,
            SignedAtUtc: timestamp);
    }

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }
}
