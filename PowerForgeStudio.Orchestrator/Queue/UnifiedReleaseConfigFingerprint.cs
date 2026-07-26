using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;

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
        if (spec.Module is not null)
        {
            var moduleInput = UnifiedReleaseModuleInputResolver.Resolve(fullPath, spec.Module);
            if (!string.IsNullOrWhiteSpace(moduleInput.ConfigPath))
            {
                AppendModuleConfigInputs(hash, moduleInput.ConfigPath!);
            }
            else
            {
                AppendFile(hash, "module-script", moduleInput.ScriptPath!);
            }
        }

        AppendApplePublicationInputs(hash, fullPath, spec.AppleApps);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static string ComputeModuleConfig(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var fullPath = Path.GetFullPath(configPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendModuleConfigInputs(hash, fullPath);
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

    internal static void ValidateModuleConfig(string configPath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new InvalidOperationException(
                "Module build config fingerprint is missing from the build checkpoint. Rebuild before publishing.");
        }

        var actualSha256 = ComputeModuleConfig(configPath);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Module build config changed after the build checkpoint. Rebuild and approve the updated contract before publishing.");
        }
    }

    private static void AppendModuleConfigInputs(IncrementalHash hash, string configPath)
    {
        AppendFile(hash, "module", configPath);
        var moduleContext = new ModulePipelineConfigurationService().Load(configPath);
        for (var index = 0; index < moduleContext.PackageConfigurationPaths.Length; index++)
        {
            AppendFile(
                hash,
                $"module-package:{index}",
                moduleContext.PackageConfigurationPaths[index]);
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
        {
            var screenshotConfigs = AppendConfiguredPaths(
                hash,
                "apple-screenshots",
                projectRoot,
                apple.ScreenshotConfigPath,
                apple.ScreenshotConfigPaths);
            AppendScreenshotPayloads(hash, screenshotConfigs);
        }
    }

    private static string[] AppendConfiguredPaths(
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
        return paths;
    }

    private static void AppendScreenshotPayloads(
        IncrementalHash hash,
        IReadOnlyList<string> screenshotConfigPaths)
    {
        for (var configIndex = 0; configIndex < screenshotConfigPaths.Count; configIndex++)
        {
            var configPath = screenshotConfigPaths[configIndex];
            var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(
                           File.ReadAllText(configPath),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidOperationException(
                           $"Unable to deserialize screenshot sync config: {configPath}");
            var baseDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
            for (var setIndex = 0; setIndex < spec.ScreenshotSets.Length; setIndex++)
            {
                var set = spec.ScreenshotSets[setIndex];
                var folder = PathTokenProtection.GetFullPath(baseDirectory, set.Path);
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException($"Screenshot folder was not found: {folder}");

                var filter = string.IsNullOrWhiteSpace(set.Filter) ? "*.png" : set.Filter;
                var maximum = set.MaxCount <= 0 ? 10 : Math.Min(set.MaxCount, 10);
                var files = Directory.GetFiles(folder, filter)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .Take(maximum)
                    .ToArray();
                if (files.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"No screenshots matched '{filter}' in '{folder}'.");
                }

                for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    AppendFile(
                        hash,
                        $"apple-screenshot-payload:{configIndex}:{setIndex}:{fileIndex}",
                        files[fileIndex]);
                }
            }
        }
    }
}
