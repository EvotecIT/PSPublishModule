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

    private static KeyValuePair<string, string>[] SelectNativePowerForgeArchives(
        PowerForgeReleaseSpec spec, PowerForgeToolReleaseTargetPlan target, string? archiveRoot)
    {
        var eligibleRuntimes = target.Combinations.Where(combination =>
                combination.Framework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) &&
                IsStandalonePowerForgeArtifactStyle(combination.Flavor.ToString()))
            .Select(combination => combination.Runtime.ToLowerInvariant())
            .Where(runtime => runtime is "win-x64" or "linux-x64").Distinct().ToArray();
        return string.IsNullOrWhiteSpace(archiveRoot)
            ? spec.Outputs!.NativePowerForgeArchivePaths.Where(pair => eligibleRuntimes.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)).ToArray()
            : eligibleRuntimes.Select(rid => new KeyValuePair<string, string>(rid,
                Path.Combine(archiveRoot!, $"PowerForgeNative-{target.Version}-{rid}.zip"))).ToArray();
    }

    private static PowerForgeReleaseAssetEntry[] ResolveNativePowerForgeArchives(
        PowerForgeReleaseSpec spec,
        string configDirectory,
        PowerForgeToolReleaseTargetPlan target,
        KeyValuePair<string, string>[] inputs,
        TemporaryReleaseDirectory temporaryReleaseDirectory)
    {
        var version = target.Version;
        if (string.IsNullOrWhiteSpace(spec.Outputs?.PowerForgeToolManifestPath) ||
            !Regex.IsMatch(version, @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Native PowerForge archives require a tool manifest and an exact release version.");

        var archives = inputs.Select(pair =>
        {
            var runtime = pair.Key.ToLowerInvariant();
            var path = ResolveOutputPath(configDirectory, pair.Value);
            if (!Path.GetFileName(path).Equals($"PowerForgeNative-{version}-{runtime}.zip", StringComparison.Ordinal))
                throw new InvalidOperationException($"The native PowerForge archive must match release {version} and runtime {runtime}: {path}");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Native PowerForge archive was not found: {path}", path);
            var suffix = runtime == "win-x64" ? ".exe" : string.Empty;
            using var archive = ZipFile.OpenRead(path);
            var entries = PowerForgeReleaseArtifactVerifier.ValidateArchiveEntries(archive);
            PowerForgeReleaseArtifactVerifier.ValidateModuleArchiveBounds(entries);
            var launcher = GetPowerForgeArchiveEntry(archive, "powerforge" + suffix);
            var companion = GetPowerForgeArchiveEntry(archive, "full/powerforge-full" + suffix);
            var implementation = GetPowerForgeArchiveEntry(archive, "full/PowerForge.Cli.dll");
            if (launcher.Length == 0 || companion.Length == 0 || implementation.Length == 0)
                throw new InvalidDataException($"Native PowerForge archive contains an empty executable or managed implementation: {path}");
            foreach (var entry in entries.Values)
            {
                using var stream = entry.Open();
                if (PowerForgeReleaseArtifactVerifier.CopyBounded(stream, Stream.Null, entry.Length, entry.FullName) != entry.Length)
                    throw new InvalidDataException($"Native PowerForge archive entry '{entry.FullName}' is incomplete.");
            }
            if (suffix.Length > 0 && target.Sign?.Enabled == true)
            {
                var signatureRoot = temporaryReleaseDirectory.GetOrCreateSubdirectory("native-signatures");
                VerifyNativePowerForgeSigner(launcher, signatureRoot, target.Sign);
                VerifyNativePowerForgeSigner(companion, signatureRoot, target.Sign);
            }

            return new PowerForgeReleaseAssetEntry
            {
                Path = path,
                Category = PowerForgeReleaseAssetCategory.Tool,
                Source = NativePowerForgeArchiveSource,
                Target = "PowerForge",
                Version = version,
                Runtime = runtime,
                Framework = "net10.0",
                Style = nameof(PowerForgeToolReleaseFlavor.Portable),
                IsFinalPackageOutput = true,
                StagedSha256 = ComputeSha256(path)
            };
        }).ToArray();
        return archives;
    }

    private static void VerifyNativePowerForgeSigner(ZipArchiveEntry entry, string directory, DotNetPublishSignOptions sign)
    {
        if (!DotNetPublishPipelineRunner.IsWindows())
            throw new InvalidOperationException("Verify signed Windows native PowerForge archives on a Windows host.");
        var path = Path.Combine(directory, entry.Name);
        entry.ExtractToFile(path, overwrite: true);
        var signature = DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(path);
        var thumbprint = DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(sign.Thumbprint);
        var matchesPublisher = thumbprint.Length > 0
            ? signature.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase)
            : !string.IsNullOrWhiteSpace(sign.SubjectName) &&
              DotNetPublishReleaseArtifactVerifier.CertificateSubjectsEqual(signature.Subject, sign.SubjectName!);
        if (!signature.IsValid || !matchesPublisher)
            throw new InvalidDataException($"Native PowerForge archive entry '{entry.FullName}' lacks a valid signature from the configured publisher.");
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
                var archiveSha256 = ComputeSha256(artifact.Path);
                if (native && !string.Equals(archiveSha256, artifact.StagedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Native PowerForge archive changed after validation: {artifact.Path}");
                using var archive = ZipFile.OpenRead(artifact.Path);
                return new
                {
                    name = assetName,
                    sha256 = archiveSha256,
                    executableSha256 = ComputePowerForgeArchiveEntrySha256(archive, executableName),
                    executableName = native ? executableName : null,
                    companionPayloadSha256 = native ? ComputePowerForgeCompanionPayloadSha256(archive) : null
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
        return ComputePowerForgeArchiveEntrySha256(GetPowerForgeArchiveEntry(archive, executableName));
    }

    private static string ComputePowerForgeArchiveEntrySha256(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ComputePowerForgeCompanionPayloadSha256(ZipArchive archive)
    {
        var files = PowerForgeReleaseArtifactVerifier.ValidateArchiveEntries(archive)
            .Where(entry => entry.Key.StartsWith("full/", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal);
        var payload = new StringBuilder();
        // The installer hashes the same UTF-8 records, with NUL separators and ordinal paths.
        foreach (var file in files)
            payload.Append(file.Key).Append('\0').Append(ComputePowerForgeArchiveEntrySha256(file.Value)).Append('\0');

        return ComputeSha256(Encoding.UTF8.GetBytes(payload.ToString()));
    }

    private static ZipArchiveEntry GetPowerForgeArchiveEntry(ZipArchive archive, string executableName)
    {
        var entries = archive.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').Equals(executableName, StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1)
        {
            throw new InvalidOperationException(
                $"PowerForge tool archive must contain exactly one '{executableName}' entry.");
        }

        return entries[0];
    }
}
