using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseSigningExecutionService : IReleaseSigningExecutionService
{
    private static readonly string[] AuthenticodeDirectoryIncludes = ["*.ps1", "*.psm1", "*.psd1", "*.dll", "*.exe", "*.cat"];
    private readonly ReleaseBuildCheckpointReader _checkpointReader = new();
    private readonly PowerShellCommandRunner _powerShellCommandRunner = new();

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

        var settings = ResolveSettings();
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
        SigningSettings settings,
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
        SigningSettings settings,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(signingPath))
        {
            return FailedReceipt(rootPath, artifact, $"Signing path '{signingPath}' was not found.", timestamp);
        }

        var script = BuildRegisterCertificateScript(signingPath, includePatterns, settings);
        var execution = await _powerShellCommandRunner.RunCommandAsync(signingPath, script, cancellationToken);
        var succeeded = execution.ExitCode == 0;
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

    private static string BuildRegisterCertificateScript(string signingPath, IReadOnlyList<string> includePatterns, SigningSettings settings)
    {
        var includes = string.Join(", ", includePatterns.Select(pattern => PowerShellScriptEscaping.QuoteLiteral(pattern)));
        return string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            BuildModuleImportClause(settings.ModulePath),
            $"Register-Certificate -Path {PowerShellScriptEscaping.QuoteLiteral(signingPath)} -LocalStore {settings.StoreName} -Thumbprint {PowerShellScriptEscaping.QuoteLiteral(settings.Thumbprint!)} -TimeStampServer {PowerShellScriptEscaping.QuoteLiteral(settings.TimeStampServer)} -Include @({includes}) -Confirm:$false -WarningAction Stop -ErrorAction Stop | Out-Null"
        });
    }

    private async Task<(bool Succeeded, string? ErrorMessage)> SignNuGetPackageAsync(ReleaseSigningArtifact artifact, SigningSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(artifact.ArtifactPath))
        {
            return (false, $"Package '{artifact.ArtifactPath}' was not found.");
        }

        var sha256 = TryGetCertificateSha256(settings.Thumbprint!, settings.StoreName);
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return (false, $"Unable to resolve SHA256 certificate fingerprint for thumbprint {settings.Thumbprint}.");
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("nuget");
        process.StartInfo.ArgumentList.Add("sign");
        process.StartInfo.ArgumentList.Add(artifact.ArtifactPath);
        process.StartInfo.ArgumentList.Add("--certificate-fingerprint");
        process.StartInfo.ArgumentList.Add(sha256);
        process.StartInfo.ArgumentList.Add("--certificate-store-location");
        process.StartInfo.ArgumentList.Add(settings.StoreName);
        process.StartInfo.ArgumentList.Add("--certificate-store-name");
        process.StartInfo.ArgumentList.Add("My");
        process.StartInfo.ArgumentList.Add("--timestamper");
        process.StartInfo.ArgumentList.Add(settings.TimeStampServer);
        process.StartInfo.ArgumentList.Add("--overwrite");

        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (process.ExitCode == 0)
        {
            return (true, null);
        }

        var error = FirstLine(stdErr) ?? FirstLine(stdOut) ?? $"dotnet nuget sign failed with exit code {process.ExitCode}.";
        return (false, error);
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

    private static SigningSettings ResolveSettings()
    {
        var thumbprint = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT");
        var storeName = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_STORE");
        var timeStampServer = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL");
        var modulePath = Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH");

        modulePath = string.IsNullOrWhiteSpace(modulePath)
            ? PSPublishModuleLocator.ResolveModulePath()
            : modulePath;

        if (string.IsNullOrWhiteSpace(timeStampServer))
        {
            timeStampServer = "http://timestamp.digicert.com";
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            storeName = "CurrentUser";
        }

        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return new SigningSettings(false, null, storeName, timeStampServer, modulePath, "Signing is not configured. Set RELEASE_OPS_STUDIO_SIGN_THUMBPRINT first.");
        }

        return new SigningSettings(true, thumbprint, storeName, timeStampServer, modulePath, null);
    }

    private static string? TryGetCertificateSha256(string thumbprint, string storeName)
    {
        try
        {
            var location = string.Equals(storeName, "LocalMachine", StringComparison.OrdinalIgnoreCase)
                ? StoreLocation.LocalMachine
                : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var normalized = NormalizeThumbprint(thumbprint);
            var certificate = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(candidate => NormalizeThumbprint(candidate.Thumbprint) == normalized);
            return certificate?.GetCertHashString(HashAlgorithmName.SHA256);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static string BuildModuleImportClause(string modulePath)
    {
        if (File.Exists(modulePath))
        {
            return $"try {{ Import-Module {PowerShellScriptEscaping.QuoteLiteral(modulePath)} -Force -ErrorAction Stop }} catch {{ Import-Module PSPublishModule -Force -ErrorAction Stop }}";
        }

        return "Import-Module PSPublishModule -Force -ErrorAction Stop";
    }

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }

    private sealed record SigningSettings(
        bool IsConfigured,
        string? Thumbprint,
        string StoreName,
        string TimeStampServer,
        string ModulePath,
        string? MissingConfigurationMessage);
}
