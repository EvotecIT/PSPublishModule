using System.Security.Cryptography;
using System.Text;
using PowerForge;

namespace PowerForgeStudio.Orchestrator.Queue;

internal static class UnifiedReleaseConfigFingerprint
{
    internal static string Compute(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Unified release config was not found: {fullPath}", fullPath);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, "release", fullPath);

        var spec = PowerForgeReleaseService.LoadConfiguration(fullPath);
        if (spec.Module?.IncludesPackages == true &&
            !string.IsNullOrWhiteSpace(spec.Module.ConfigPath))
        {
            var releaseDirectory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            var repositoryRoot = string.IsNullOrWhiteSpace(spec.Module.RepositoryRoot)
                ? releaseDirectory
                : Path.IsPathRooted(spec.Module.RepositoryRoot)
                    ? Path.GetFullPath(spec.Module.RepositoryRoot)
                    : Path.GetFullPath(Path.Combine(releaseDirectory, spec.Module.RepositoryRoot));
            var moduleConfigPath = Path.IsPathRooted(spec.Module.ConfigPath)
                ? Path.GetFullPath(spec.Module.ConfigPath)
                : Path.GetFullPath(Path.Combine(repositoryRoot, spec.Module.ConfigPath));
            if (!File.Exists(moduleConfigPath))
            {
                throw new FileNotFoundException(
                    $"Module configuration referenced by the unified release was not found: {moduleConfigPath}",
                    moduleConfigPath);
            }

            AppendFile(hash, "module-packages", moduleConfigPath);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
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

    private static void AppendFile(IncrementalHash hash, string label, string path)
    {
        var labelBytes = Encoding.UTF8.GetBytes(label);
        var content = File.ReadAllBytes(path);
        hash.AppendData(BitConverter.GetBytes(labelBytes.Length));
        hash.AppendData(labelBytes);
        hash.AppendData(BitConverter.GetBytes(content.Length));
        hash.AppendData(content);
    }
}
