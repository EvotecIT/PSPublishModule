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
        if (!string.IsNullOrWhiteSpace(spec.Module?.ConfigPath))
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

            AppendFile(hash, "module", moduleConfigPath);
        }

        AppendApplePublicationInputs(hash, fullPath, spec.AppleApps);
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
        if (!File.Exists(path))
            throw new FileNotFoundException($"Checkpoint input file was not found: {path}", path);

        var labelBytes = Encoding.UTF8.GetBytes(label);
        var content = File.ReadAllBytes(path);
        hash.AppendData(BitConverter.GetBytes(labelBytes.Length));
        hash.AppendData(labelBytes);
        hash.AppendData(BitConverter.GetBytes(content.Length));
        hash.AppendData(content);
    }

    private static void AppendApplePublicationInputs(
        IncrementalHash hash,
        string releaseConfigPath,
        PowerForgeAppleReleaseOptions? apple)
    {
        if (apple is null)
            return;

        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var projectRoot = string.IsNullOrWhiteSpace(apple.ProjectRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, apple.ProjectRoot!);
        if (apple.SyncMetadata)
            AppendConfiguredPaths(hash, "apple-metadata", projectRoot, apple.MetadataConfigPath, apple.MetadataConfigPaths);
        if (apple.SyncAppInfo)
            AppendConfiguredPaths(hash, "apple-app-info", projectRoot, apple.AppInfoConfigPath, apple.AppInfoConfigPaths);
        if (apple.SyncScreenshots)
            AppendConfiguredPaths(hash, "apple-screenshots", projectRoot, apple.ScreenshotConfigPath, apple.ScreenshotConfigPaths);
    }

    private static void AppendConfiguredPaths(
        IncrementalHash hash,
        string label,
        string projectRoot,
        string? primaryPath,
        IEnumerable<string>? additionalPaths)
    {
        var paths = new[] { primaryPath }
            .Concat(additionalPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => PathTokenProtection.GetFullPath(projectRoot, path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < paths.Length; index++)
            AppendFile(hash, $"{label}:{index}", paths[index]);
    }
}
