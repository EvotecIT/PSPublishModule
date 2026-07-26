using System.Security.Cryptography;
using System.Text;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

internal static class ReleaseSigningArtifactIntegrity
{
    internal static ReleaseSigningReceipt Capture(ReleaseSigningReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Status is not (ReleaseSigningReceiptStatus.Signed or ReleaseSigningReceiptStatus.Skipped))
            return receipt;

        return receipt with { ContentSha256 = Compute(receipt.ArtifactPath, receipt.ArtifactKind) };
    }

    internal static string? Validate(IEnumerable<ReleaseSigningReceipt> receipts)
    {
        foreach (var receipt in receipts.Where(static receipt =>
                     receipt.Status is ReleaseSigningReceiptStatus.Signed or ReleaseSigningReceiptStatus.Skipped))
        {
            if (string.IsNullOrWhiteSpace(receipt.ContentSha256))
            {
                return $"Signed artifact integrity digest is missing: {receipt.ArtifactPath}. Re-sign before publishing.";
            }

            string actual;
            try
            {
                actual = Compute(receipt.ArtifactPath, receipt.ArtifactKind);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Signed artifact could not be verified: {receipt.ArtifactPath}. {ex.Message}";
            }

            if (!string.Equals(actual, receipt.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return $"Signed artifact changed after approval: {receipt.ArtifactPath}. Re-sign before publishing.";
            }
        }

        return null;
    }

    internal static string Compute(string artifactPath, string artifactKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        if (string.Equals(artifactKind, "Directory", StringComparison.OrdinalIgnoreCase))
            return ComputeDirectory(artifactPath);
        if (!File.Exists(artifactPath))
            throw new FileNotFoundException($"Signed artifact was not found: {artifactPath}", artifactPath);

        using var stream = File.OpenRead(artifactPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ComputeDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Signed artifact directory was not found: {directoryPath}");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(directoryPath, file).Replace('\\', '/');
            var pathBytes = Encoding.UTF8.GetBytes(relativePath);
            hash.AppendData(BitConverter.GetBytes(pathBytes.Length));
            hash.AppendData(pathBytes);
            using var stream = File.OpenRead(file);
            hash.AppendData(BitConverter.GetBytes(stream.Length));
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
