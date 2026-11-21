using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private const string NativePowerForgeArchiveSource = "NativePowerForgeArchive";

    private static void SkipManagedBuildsCoveredByNativeArchives(
        PowerForgeToolReleasePlan toolPlan, IReadOnlyCollection<PowerForgeReleaseAssetEntry> nativeArchives)
    {
        var runtimes = new HashSet<string?>(nativeArchives.Select(archive => archive.Runtime), StringComparer.OrdinalIgnoreCase);
        foreach (var target in toolPlan.Targets.Where(target => target.Name.Equals("PowerForge", StringComparison.OrdinalIgnoreCase)))
        {
            target.Combinations = target.Combinations.Where(combination =>
                !runtimes.Contains(combination.Runtime) || !IsStandalonePowerForgeArtifactStyle(combination.Flavor.ToString())).ToArray();
        }
    }

    private static PowerForgeReleaseAssetEntry[] ResolveNativePowerForgeArchives(
        PowerForgeReleaseSpec spec, string configDirectory, string? version, string? archiveRoot)
    {
        var configured = spec.Outputs?.NativePowerForgeArchivePaths;
        if ((configured is null || configured.Count == 0) && string.IsNullOrWhiteSpace(archiveRoot))
            return Array.Empty<PowerForgeReleaseAssetEntry>();
        if (string.IsNullOrWhiteSpace(spec.Outputs?.PowerForgeToolManifestPath) || string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Native PowerForge archives require a tool manifest and an exact release version.");

        var inputs = string.IsNullOrWhiteSpace(archiveRoot)
            ? configured!
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["win-x64"] = Path.Combine(archiveRoot!, $"PowerForgeNative-{version}-win-x64.zip"),
                ["linux-x64"] = Path.Combine(archiveRoot!, $"PowerForgeNative-{version}-linux-x64.zip")
            };

        return inputs.Select(pair =>
        {
            if (pair.Key is not ("win-x64" or "linux-x64"))
                throw new InvalidOperationException($"The native PowerForge release does not support '{pair.Key}'.");
            var path = ResolveOutputPath(configDirectory, pair.Value);
            if (!Path.GetFileName(path).Equals($"PowerForgeNative-{version}-{pair.Key}.zip", StringComparison.Ordinal))
                throw new InvalidOperationException($"The native PowerForge archive must match release {version} and runtime {pair.Key}: {path}");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Native PowerForge archive was not found: {path}", path);

            return new PowerForgeReleaseAssetEntry
            {
                Path = path,
                Category = PowerForgeReleaseAssetCategory.Tool,
                Source = NativePowerForgeArchiveSource,
                Target = "PowerForge",
                Version = version,
                Runtime = pair.Key,
                Framework = "net10.0",
                Style = nameof(PowerForgeToolReleaseFlavor.Portable),
                IsFinalPackageOutput = true
            };
        }).ToArray();
    }

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
            (IsStandalonePowerForgeToolSelected(result) || producedAssets.Any(IsStandalonePowerForgeArchive)))
        {
            ValidatePowerForgeToolManifestStaging(spec);
            var manifestPath = ResolveOutputPath(configDirectory, toolManifestPath!);
            WritePowerForgeToolLockManifest(spec, producedAssets, sharedReleaseVersion, manifestPath);
            paths.Add(manifestPath);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void ValidatePowerForgeToolManifestStaging(PowerForgeReleaseSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Outputs?.Staging?.ToolsNameTemplate))
        {
            throw new InvalidOperationException(
                "PowerForgeToolManifestPath cannot be combined with Outputs.Staging.ToolsNameTemplate because " +
                "the installer manifest must name the exact published tool archive.");
        }
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
        var standaloneArtifacts = producedAssets.Where(IsStandalonePowerForgeArchive).ToArray();
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
        var repositoryId = owner + "/" + repository;
        if (!IsSupportedPowerForgeToolManifestRepository(repositoryId))
        {
            throw new InvalidOperationException(
                $"PowerForge tool lock manifest repository '{repositoryId}' contains characters the installer cannot consume.");
        }
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
            artifact =>
            {
                var assetName = Path.GetFileName(artifact.Path);
                if (!IsSupportedPowerForgeToolManifestArchiveName(assetName))
                {
                    throw new InvalidOperationException(
                        $"PowerForge tool lock manifest archive name '{assetName}' contains characters the installer cannot consume.");
                }
                var native = artifact.Source == NativePowerForgeArchiveSource;
                var suffix = artifact.Runtime!.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".exe" : string.Empty;
                var executableName = (native ? "powerforge" : "PowerForge") + suffix;
                using var archive = ZipFile.OpenRead(artifact.Path);
                return new
                {
                    name = assetName,
                    sha256 = ComputeSha256(artifact.Path),
                    executableSha256 = ComputePowerForgeArchiveEntrySha256(archive, executableName),
                    executableName = native ? executableName : null,
                    companionSha256 = native
                        ? ComputePowerForgeArchiveEntrySha256(archive, "full/powerforge-full" + suffix)
                        : null
                };
            },
            StringComparer.OrdinalIgnoreCase);
        var commit = NormalizePowerForgeToolManifestCommit(spec.GitHub?.Commitish);
        var document = new
        {
            schemaVersion = 2,
            repository = repositoryId,
            version,
            releaseTag,
            commit,
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

    internal static bool IsSupportedPowerForgeToolManifestRepository(string? repository)
        => !string.IsNullOrWhiteSpace(repository) &&
           Regex.IsMatch(repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);

    internal static bool IsSupportedPowerForgeToolManifestArchiveName(string? archiveName)
        => !string.IsNullOrWhiteSpace(archiveName) &&
           Regex.IsMatch(archiveName, "^[A-Za-z0-9][A-Za-z0-9._-]*\\.zip$", RegexOptions.CultureInvariant);

    internal static string? NormalizePowerForgeToolManifestCommit(string? commit)
    {
        var normalized = commit?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (!Regex.IsMatch(normalized, "^[A-Fa-f0-9]{40}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "PowerForge tool lock manifest commit must be an exact 40-character Git SHA.");
        }
        return normalized!.ToLowerInvariant();
    }

    private static bool IsStandalonePowerForgeArchive(PowerForgeReleaseAssetEntry artifact)
        => string.Equals(artifact.Target, "PowerForge", StringComparison.OrdinalIgnoreCase) &&
           artifact.Category == PowerForgeReleaseAssetCategory.Tool &&
           IsStandalonePowerForgeArtifactStyle(artifact.Style) &&
           artifact.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
           File.Exists(artifact.Path);

    private static string ComputePowerForgeArchiveEntrySha256(ZipArchive archive, string executableName)
    {
        var entries = archive.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').Equals(executableName, StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1)
        {
            throw new InvalidOperationException(
                $"PowerForge tool archive must contain exactly one '{executableName}' executable.");
        }

        using var stream = entries[0].Open();
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }
}
