using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string[] ResolveAdditionalReleaseAssetPaths(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion,
        IReadOnlyCollection<PowerForgeReleaseAssetEntry> producedAssets,
        bool standaloneToolOutputSelected)
    {
        var paths = (spec.Outputs?.AdditionalAssetPaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveOutputPath(configDirectory, path))
            .ToList();
        foreach (var path in paths.Where(path => !File.Exists(path)))
            throw new FileNotFoundException($"Additional release asset was not found: {path}", path);

        var toolManifestPath = spec.Outputs?.PowerForgeToolManifestPath;
        if (!string.IsNullOrWhiteSpace(toolManifestPath) &&
            standaloneToolOutputSelected &&
            IsStandalonePowerForgeToolSelected(result))
        {
            var manifestPath = ResolveOutputPath(configDirectory, toolManifestPath!);
            WritePowerForgeToolLockManifest(spec, producedAssets, sharedReleaseVersion, manifestPath);
            paths.Add(manifestPath);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsStandalonePowerForgeToolSelected(PowerForgeReleaseResult result)
        => (result.ToolPlan?.Targets ?? Array.Empty<PowerForgeToolReleaseTargetPlan>())
               .Any(static target =>
                   target.Name.Equals("PowerForge", StringComparison.OrdinalIgnoreCase) &&
                   (target.Combinations ?? Array.Empty<PowerForgeToolReleaseCombinationPlan>())
                       .Any(static combination =>
                           combination.Flavor is PowerForgeToolReleaseFlavor.SingleContained or
                               PowerForgeToolReleaseFlavor.Portable)) ||
           (result.DotNetToolPlan?.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
               .Any(static target =>
                   target.Name.Equals("PowerForge", StringComparison.OrdinalIgnoreCase) &&
                   (target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                       .Any(static combination =>
                           IsStandalonePowerForgeArtifactStyle(combination.Style.ToString())));

    private static void WritePowerForgeToolLockManifest(
        PowerForgeReleaseSpec spec,
        IReadOnlyCollection<PowerForgeReleaseAssetEntry> producedAssets,
        string? sharedReleaseVersion,
        string manifestPath)
    {
        var standaloneArtifacts = producedAssets
            .Where(static artifact =>
                string.Equals(artifact.Target, "PowerForge", StringComparison.OrdinalIgnoreCase) &&
                artifact.Category == PowerForgeReleaseAssetCategory.Tool &&
                !string.IsNullOrWhiteSpace(artifact.Path) &&
                IsStandalonePowerForgeArtifactStyle(artifact.Style) &&
                artifact.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(artifact.Path))
            .ToArray();
        var unsupportedRuntime = standaloneArtifacts.FirstOrDefault(static artifact =>
            !IsSupportedPowerForgeToolManifestRuntime(artifact.Runtime));
        if (unsupportedRuntime is not null)
        {
            throw new InvalidOperationException(
                $"PowerForge tool lock manifest does not support runtime '{unsupportedRuntime.Runtime ?? "<missing>"}'. " +
                "Supported runtimes are win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, and osx-arm64.");
        }
        var artifacts = standaloneArtifacts;
        if (artifacts.Length == 0)
            throw new InvalidOperationException("PowerForgeToolManifestPath requires zipped PowerForge SingleContained tool artifacts.");

        var versions = artifacts.Select(static artifact => artifact.Version)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var version = versions.Length == 1 ? versions[0] : sharedReleaseVersion;
        if (string.IsNullOrWhiteSpace(version) || !Regex.IsMatch(version, "^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("PowerForge tool lock manifest requires one exact x.y.z version.");

        var duplicateRuntime = artifacts.GroupBy(static artifact => artifact.Runtime, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateRuntime is not null)
            throw new InvalidOperationException($"PowerForge tool lock manifest has duplicate runtime '{duplicateRuntime.Key}'.");

        var owner = spec.GitHub?.Owner?.Trim();
        var repository = spec.GitHub?.Repository?.Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            throw new InvalidOperationException("PowerForge tool lock manifest requires unified GitHub owner and repository.");
        var tagTemplate = string.IsNullOrWhiteSpace(spec.GitHub?.TagTemplate) ? "v{Version}" : spec.GitHub!.TagTemplate!;
        if (!IsDeterministicPowerForgeToolManifestTagTemplate(tagTemplate))
        {
            throw new InvalidOperationException(
                "PowerForge tool lock manifest requires a deterministic GitHub tag template; " +
                "clock-based placeholders are not supported.");
        }
        var releaseTag = ApplyUnifiedGitHubTemplate(tagTemplate, repository!, version!);
        if (!IsSupportedPowerForgeToolManifestReleaseTag(releaseTag))
        {
            throw new InvalidOperationException(
                $"PowerForge tool lock manifest release tag '{releaseTag}' contains characters the installer cannot consume. " +
                "Use letters, numbers, periods, underscores, and hyphens only.");
        }
        var assets = artifacts.ToDictionary(
            static artifact => artifact.Runtime!,
            artifact => new
            {
                name = Path.GetFileName(artifact.Path),
                sha256 = ComputeSha256(artifact.Path),
                executableSha256 = ComputePowerForgeExecutableSha256(artifact.Path, artifact.Runtime!)
            },
            StringComparer.OrdinalIgnoreCase);
        var document = new
        {
            schemaVersion = 2,
            repository = owner + "/" + repository,
            version,
            releaseTag,
            commit = spec.GitHub?.Commitish,
            assets
        };

        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var temporaryPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    document,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(manifestPath))
                File.Replace(temporaryPath, manifestPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, manifestPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    internal static bool IsStandalonePowerForgeArtifactStyle(string? style)
        => string.Equals(style, nameof(PowerForgeToolReleaseFlavor.SingleContained), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(style, nameof(PowerForgeToolReleaseFlavor.Portable), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(style, nameof(DotNetPublishStyle.PortableCompat), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(style, nameof(DotNetPublishStyle.PortableSize), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(style, nameof(DotNetPublishStyle.AotSpeed), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(style, nameof(DotNetPublishStyle.AotSize), StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedPowerForgeToolManifestRuntime(string? runtime)
        => runtime is not null && runtime.ToLowerInvariant() is
            "win-x64" or "win-arm64" or
            "linux-x64" or "linux-arm64" or
            "osx-x64" or "osx-arm64";

    internal static bool IsDeterministicPowerForgeToolManifestTagTemplate(string tagTemplate)
        => new[] { "{Date}", "{UtcDate}", "{DateTime}", "{UtcDateTime}", "{Timestamp}", "{UtcTimestamp}" }
            .All(token => !tagTemplate.Contains(token, StringComparison.OrdinalIgnoreCase));

    internal static bool IsSupportedPowerForgeToolManifestReleaseTag(string? releaseTag)
        => !string.IsNullOrWhiteSpace(releaseTag) &&
           Regex.IsMatch(releaseTag, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);

    private static string ComputePowerForgeExecutableSha256(string archivePath, string runtime)
    {
        var executableName = runtime.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
            ? "PowerForge.exe"
            : "PowerForge";
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').Equals(executableName, StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1)
        {
            throw new InvalidOperationException(
                $"PowerForge tool archive '{archivePath}' must contain exactly one root '{executableName}' executable.");
        }

        using var stream = entries[0].Open();
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
