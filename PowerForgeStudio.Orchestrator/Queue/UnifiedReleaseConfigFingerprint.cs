using System.Security.Cryptography;

namespace PowerForgeStudio.Orchestrator.Queue;

internal static class UnifiedReleaseConfigFingerprint
{
    internal static string Compute(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Unified release config was not found: {fullPath}", fullPath);

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath)));
    }

    internal static void Validate(string configPath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException(
                "Unified release config fingerprint is missing from the build checkpoint. Rebuild before publishing.");
        }

        var actualSha256 = Compute(configPath);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Unified release config changed after the build checkpoint. Rebuild and approve the updated contract before publishing.");
        }
    }
}
